using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using TaskManager.Host.Models;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace TaskManager.Host.Native;

// Slow file-version and account lookups never run on the sampling thread. One bounded worker
// also prevents a burst of new processes from creating unbounded threadpool work or handles.
internal sealed class ProcessMetadataCache : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, ProcessMetadata> identities = new();
    private readonly HashSet<string> pending = new();
    private readonly Dictionary<string, (string? Publisher, Unavailable? Error)> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string? Name, Unavailable? Error)> accounts = new();
    private readonly Channel<(uint Pid, string Identity, string? Path)> queue = Channel.CreateBounded<(uint, string, string?)>(new BoundedChannelOptions(4096) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource stopping = new();
    private readonly Task worker;
    private static readonly Unavailable Pending = new("loading", "静态文件/用户信息正在后台读取，基础采样继续。");

    public ProcessMetadataCache() => worker = Task.Run(WorkAsync);

    internal ProcessMetadata Read(SafeFileHandle handle, uint pid, string identity)
    {
        lock (gate) if (identities.TryGetValue(identity, out var cached)) return cached;
        var errors = new Dictionary<string, Unavailable> { ["publisher"] = Pending, ["userName"] = Pending };
        var buffer = new char[32768];
        uint length = (uint)buffer.Length;
        string? path = null;
        if (PInvoke.QueryFullProcessImageName(handle, 0, buffer, ref length)) path = new string(buffer, 0, (int)length);
        else errors["path"] = NativeSupport.LastError("读取路径失败");
        var value = new ProcessMetadata(path, null, null, errors);
        lock (gate)
        {
            if (identities.Count >= 20_000) identities.Remove(identities.Keys.First());
            identities[identity] = value;
            if (pending.Add(identity) && !queue.Writer.TryWrite((pid, identity, path))) pending.Remove(identity);
        }
        return value;
    }

    internal void Retain(IReadOnlySet<string> live)
    {
        lock (gate)
        {
            foreach (var key in identities.Keys.Where(key => !live.Contains(key)).ToArray()) identities.Remove(key);
            // Queue overflow is retried in a later sample instead of leaving a permanent loading state.
            foreach (var pair in identities.Where(pair => pair.Value.Unavailable.Values.Contains(Pending) && !pending.Contains(pair.Key)).Take(32))
                if (uint.TryParse(pair.Key.Split(':')[0], out var pid) && queue.Writer.TryWrite((pid, pair.Key, pair.Value.Path))) pending.Add(pair.Key);
        }
    }

    private async Task WorkAsync()
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stopping.Token))
            {
                var errors = new Dictionary<string, Unavailable>();
                string? publisher = null, user = null;
                try
                {
                    if (item.Path is not null)
                    {
                        var stamp = File.GetLastWriteTimeUtc(item.Path).Ticks;
                        var key = $"{Path.GetFullPath(item.Path)}|{stamp}";
                        if (!files.TryGetValue(key, out var file))
                        {
                            try
                            {
                                publisher = FileVersionInfo.GetVersionInfo(item.Path).CompanyName;
                                file = (string.IsNullOrWhiteSpace(publisher) ? null : publisher, string.IsNullOrWhiteSpace(publisher) ? new("notCollected", "文件未提供 CompanyName；该字段不是签名验证结果。") : null);
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { file = (null, new("error", "无法读取文件版本发布者。")); }
                            if (files.Count >= 2048) files.Remove(files.Keys.First());
                            files[key] = file;
                        }
                        publisher = file.Publisher;
                        if (file.Error is not null) errors["publisher"] = file.Error;
                    }
                    else errors["publisher"] = new("notCollected", "无法读取进程文件路径。");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
                { errors["publisher"] = new("error", "文件元数据不可读；基础进程信息不受影响。"); }
                try
                {
                    using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, item.Pid);
                    if (!handle.IsInvalid && PInvoke.GetProcessTimes(handle, out var creation, out _, out _, out _) && item.Identity == $"{item.Pid}:{NativeSupport.FileTime(creation)}") user = ReadOwner(handle, errors);
                    else errors["userName"] = new("exited", "后台读取时该进程身份已退出或不可读。");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                { errors["userName"] = new("accessDenied", "后台用户信息不可读；文件信息不受影响。"); }
                if (user is null) errors.TryAdd("userName", new("notCollected", "后台用户信息尚不可读；基础进程信息不受影响。"));
                lock (gate)
                {
                    pending.Remove(item.Identity);
                    if (identities.TryGetValue(item.Identity, out var previous))
                    {
                        if (previous.Unavailable.TryGetValue("path", out var error)) errors["path"] = error;
                        identities[item.Identity] = new(item.Path, publisher, user, errors);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    private unsafe string? ReadOwner(SafeFileHandle handle, Dictionary<string, Unavailable> errors)
    {
        if (!PInvoke.OpenProcessToken(handle, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token)) { errors["userName"] = NativeSupport.LastError("读取进程令牌失败"); return null; }
        using (token)
        {
            PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenUser, null, 0, out var required);
            if (required == 0 || required > 1024 * 1024) { errors["userName"] = NativeSupport.LastError("读取令牌用户长度失败"); return null; }
            var bytes = new byte[required];
            fixed (byte* data = bytes)
            {
                if (!PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenUser, data, required, out _)) { errors["userName"] = NativeSupport.LastError("读取令牌用户失败"); return null; }
                var user = (TOKEN_USER*)data;
                var sidLength = PInvoke.GetLengthSid(user->User.Sid);
                if (sidLength is 0 or > 65536) { errors["userName"] = new("error", "Windows SID 长度无效。"); return null; }
                var sidBytes = new byte[sidLength];
                Marshal.Copy((nint)user->User.Sid.Value, sidBytes, 0, (int)sidLength);
                var key = Convert.ToHexString(sidBytes);
                if (!accounts.TryGetValue(key, out var account))
                {
                    var name = new char[512]; var domain = new char[512]; uint nameSize = 512, domainSize = 512;
                    if (!PInvoke.LookupAccountSid(null, user->User.Sid, name, ref nameSize, domain, ref domainSize, out _)) account = (null, NativeSupport.LastError("解析用户 SID 失败"));
                    else account = (domainSize > 0 ? $"{new string(domain, 0, (int)domainSize)}\\{new string(name, 0, (int)nameSize)}" : new string(name, 0, (int)nameSize), null);
                    if (accounts.Count >= 1024) accounts.Remove(accounts.Keys.First());
                    accounts[key] = account;
                }
                if (account.Error is not null) errors["userName"] = account.Error;
                return account.Name;
            }
        }
    }

    public void Dispose() { stopping.Cancel(); queue.Writer.TryComplete(); /* worker owns in-flight native handles */ }
}
