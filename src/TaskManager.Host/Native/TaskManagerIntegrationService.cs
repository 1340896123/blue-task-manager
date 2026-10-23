using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using TaskManager.Host.Models;
using TaskManager.Host.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace TaskManager.Host.Native;

// Standard Windows IFEO configuration, independently implemented from the public API.
// No renderer-provided paths, commands, hive, registry view or value names are accepted.
public sealed class TaskManagerIntegrationService
{
    internal const string ParentPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    internal const string KeyPath = ParentPath + @"\taskmgr.exe";
    internal const string EntryFlag = "--task-manager-entry";
    private const REG_SAM_FLAGS ReadRights = REG_SAM_FLAGS.KEY_QUERY_VALUE | REG_SAM_FLAGS.KEY_ENUMERATE_SUB_KEYS | REG_SAM_FLAGS.KEY_WOW64_64KEY;
    private readonly object gate = new();
    private static readonly UnicodeEncoding Unicode = new(false, false, true);
    public static string ExecutablePath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TaskManager.Host.exe"));
    public static string RegistrationCommand => $"\"{ExecutablePath}\" {EntryFlag}";

    public TaskManagerIntegrationState Read()
    {
        using var root = Root();
        var opened = PInvoke.RegOpenKeyEx(root, KeyPath, 0, ReadRights, out var key);
        using (key)
        {
            var configuration = opened == WIN32_ERROR.ERROR_FILE_NOT_FOUND ? new Configuration([], [], null) : opened == WIN32_ERROR.ERROR_SUCCESS ? ReadConfiguration(key) : new([], [], NativeSupport.Error((int)opened, "读取任务管理器入口配置失败"));
            var permission = ProbeWrite(root, opened == WIN32_ERROR.ERROR_FILE_NOT_FOUND);
            return Describe(configuration, permission.CanWrite, permission.Error);
        }
    }

    public TaskManagerIntegrationResult Apply(TaskManagerIntegrationRequest request)
    {
        if (request.ExpectedRevision is null || request.ExpectedRevision.Length != 64 || !request.ExpectedRevision.All(Uri.IsHexDigit)) throw new ApiException(400, "integrationRevisionRequired", "请先读取任务管理器入口的实际状态，再确认操作。");
        lock (gate)
        {
            var before = Read();
            if (!before.CanRead) throw Error(before.ReadError ?? new("error", "实际入口配置不可读取。"));
            CheckRevision(before, request.ExpectedRevision);
            if (request.Enabled)
            {
                if (before.Status == "enabled") return new(before, false, "本目录程序已接管任务管理器入口，未重复写入。");
                if (before.Status != "disabled") throw new ApiException(409, "integrationConflict", before.ConflictReason ?? "已有其他或特殊IFEO配置，未覆盖。");
                if (!before.ExecutableAvailable) throw new ApiException(409, "integrationExecutableUnavailable", "当前目录没有可信的TaskManager.Host.exe，请恢复完整程序目录后重试。");
            }
            else if (!before.OwnsDebugger)
            {
                if (before.Status == "disabled") return new(before, false, "当前没有本程序接管值，未修改Windows配置。");
                throw new ApiException(409, "integrationConflict", "当前Debugger不精确属于本目录程序，不能取消其他工具的接管。");
            }

            using var root = Root();
            var opened = PInvoke.RegOpenKeyEx(root, KeyPath, 0, ReadRights | REG_SAM_FLAGS.KEY_SET_VALUE, out var key);
            if (opened == WIN32_ERROR.ERROR_FILE_NOT_FOUND && request.Enabled)
            {
                key.Dispose();
                opened = PInvoke.RegCreateKeyEx(root, KeyPath, default, REG_OPEN_CREATE_OPTIONS.REG_OPTION_NON_VOLATILE, ReadRights | REG_SAM_FLAGS.KEY_SET_VALUE, null, out key, out _);
            }
            using (key)
            {
                if (opened != WIN32_ERROR.ERROR_SUCCESS) throw Error(NativeSupport.Error((int)opened, "Windows拒绝写入任务管理器入口；请以管理员身份运行后重试"));
                // Re-read the complete key through this exact write handle immediately before mutation.
                var actual = Describe(ReadConfiguration(key), true, null);
                if (!actual.CanRead) throw Error(actual.ReadError!);
                CheckRevision(actual, request.ExpectedRevision);
                if (request.Enabled && actual.Status != "disabled") throw new ApiException(409, "integrationConflict", "写入前发现其他或特殊配置，未覆盖。");
                if (!request.Enabled && !actual.OwnsDebugger) throw new ApiException(409, "integrationConflict", "写入前Debugger已不属于本目录程序，未删除。");
                var result = request.Enabled ? PInvoke.RegSetValueEx(key, "Debugger", REG_VALUE_TYPE.REG_SZ, Encoding.Unicode.GetBytes(RegistrationCommand + '\0')) : PInvoke.RegDeleteValue(key, "Debugger");
                if (result != WIN32_ERROR.ERROR_SUCCESS) throw Error(NativeSupport.Error((int)result, request.Enabled ? "注册任务管理器入口失败" : "取消任务管理器入口失败"));
                var after = Describe(ReadConfiguration(key), true, null);
                if (!after.CanRead) throw new ApiException(503, "integrationReadbackFailed", "Windows已受理配置修改，但读回失败；操作可能已生效，请刷新实际状态，不要盲目重试。", after.ReadError?.NativeErrorCode);
                if (request.Enabled ? after.Status != "enabled" : after.OwnsDebugger || after.CurrentDebugger is not null)
                    throw new ApiException(409, "integrationChangedAfterWrite", "修改后的实际配置与请求不一致；可能有其他工具同时更改，请刷新确认，不再自动写入。");
                return new(after, true, request.Enabled ? "已读回确认本目录程序接管任务管理器入口。" : "已读回确认仅删除本目录程序的Debugger，其他Windows配置保持原样。");
            }
        }
    }

    private static SafeRegistryHandle Root() => new((nint)HKEY.HKEY_LOCAL_MACHINE, false);
    private static void CheckRevision(TaskManagerIntegrationState state, string expected)
    { if (!string.Equals(state.Revision, expected, StringComparison.Ordinal)) throw new ApiException(409, "integrationChanged", "Windows任务管理器入口配置已经变化，未执行修改；请刷新后重新确认。"); }
    private static ApiException Error(Unavailable error) => new(error.NativeErrorCode == 5 ? 403 : 503, error.NativeErrorCode == 5 ? "integrationAccessDenied" : "integrationUnavailable", error.Message, error.NativeErrorCode);

    private static (bool? CanWrite, Unavailable? Error) ProbeWrite(SafeRegistryHandle root, bool missing)
    {
        var rights = (missing ? REG_SAM_FLAGS.KEY_CREATE_SUB_KEY : REG_SAM_FLAGS.KEY_SET_VALUE) | REG_SAM_FLAGS.KEY_WOW64_64KEY;
        var result = PInvoke.RegOpenKeyEx(root, missing ? ParentPath : KeyPath, 0, rights, out var probe);
        using (probe)
        {
            if (result == WIN32_ERROR.ERROR_SUCCESS) return (true, null);
            return (result == WIN32_ERROR.ERROR_ACCESS_DENIED ? false : null, NativeSupport.Error((int)result, "当前账户无法写入计算机范围的任务管理器入口；需要管理员写权限"));
        }
    }

    private static Configuration ReadConfiguration(SafeRegistryHandle key)
    {
        var values = new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase); var subkeys = new List<string>();
        var name = new char[16384]; var bytes = new byte[65536];
        for (uint index = 0; ; index++)
        {
            uint nameLength = (uint)name.Length, length = (uint)bytes.Length;
            var result = PInvoke.RegEnumValue(key, index, name, ref nameLength, out var type, bytes, ref length);
            if (result == WIN32_ERROR.ERROR_NO_MORE_ITEMS) break;
            if (result != WIN32_ERROR.ERROR_SUCCESS) return new(values, subkeys, NativeSupport.Error((int)result, "读取IFEO值失败，未允许修改"));
            if (index >= 64 || length > bytes.Length || nameLength >= name.Length) return new(values, subkeys, new("capacity", "IFEO值超过64项/64KiB单值上限，未允许修改。"));
            values[new string(name, 0, (int)nameLength)] = new(type, bytes.AsSpan(0, (int)length).ToArray());
        }
        for (uint index = 0; ; index++)
        {
            uint length = (uint)name.Length;
            var result = PInvoke.RegEnumKeyEx(key, index, name, ref length, default);
            if (result == WIN32_ERROR.ERROR_NO_MORE_ITEMS) break;
            if (result != WIN32_ERROR.ERROR_SUCCESS) return new(values, subkeys, NativeSupport.Error((int)result, "读取IFEO过滤子键失败，未允许修改"));
            if (index >= 64 || length >= name.Length) return new(values, subkeys, new("capacity", "IFEO子键超过64项上限，未允许修改。"));
            subkeys.Add(new string(name, 0, (int)length));
        }
        return new(values, subkeys, null);
    }

    private static TaskManagerIntegrationState Describe(Configuration configuration, bool? canWrite, Unavailable? writeError)
    {
        string? debugger = null, conflict = null; uint? filter = null;
        if (configuration.Values.TryGetValue("Debugger", out var value))
        {
            if (value.Type != (uint)REG_VALUE_TYPE.REG_SZ || (value.Data.Length & 1) != 0) conflict = "现有Debugger不是普通REG_SZ文本，不能安全覆盖。";
            else
            {
                try { debugger = Unicode.GetString(value.Data).TrimEnd('\0'); if (debugger.Contains('\0')) conflict = "现有Debugger包含异常空字符，不能安全覆盖。"; }
                catch (DecoderFallbackException) { conflict = "现有Debugger的Unicode文本无效，不能安全覆盖。"; }
            }
        }
        var owns = debugger == RegistrationCommand && value?.Type == (uint)REG_VALUE_TYPE.REG_SZ;
        if (configuration.Values.TryGetValue("UseFilter", out var useFilter))
        {
            if (useFilter.Type != (uint)REG_VALUE_TYPE.REG_DWORD || useFilter.Data.Length != 4) conflict ??= "UseFilter不是有效DWORD，无法确定实际入口，未允许接管。";
            else { filter = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(useFilter.Data); if (filter != 0) conflict ??= "已启用UseFilter过滤入口，本应用不覆盖其特殊配置。"; }
        }
        if (configuration.Subkeys.Count != 0 || configuration.Values.Keys.Any(name => !name.Equals("Debugger", StringComparison.OrdinalIgnoreCase) && !name.Equals("UseFilter", StringComparison.OrdinalIgnoreCase))) conflict ??= "当前存在过滤子键或其他特殊IFEO配置，本应用不覆盖；仅可取消精确属于本程序的Debugger。";
        if (!string.IsNullOrEmpty(debugger) && !owns) conflict ??= "任务管理器入口已配置为其他工具或其他目录/命令，本应用不会覆盖。";
        var status = configuration.Error is not null ? "unavailable" : conflict is not null ? "conflict" : owns ? "enabled" : "disabled";
        var executableAvailable = File.Exists(ExecutablePath);
        var fingerprint = JsonConvert.SerializeObject(new { command = RegistrationCommand, canWrite, values = configuration.Values.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => new { name = p.Key.ToUpperInvariant(), type = p.Value.Type, data = Convert.ToBase64String(p.Value.Data) }), subkeys = configuration.Subkeys.Order(StringComparer.OrdinalIgnoreCase), error = configuration.Error });
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        return new(status, status == "enabled" ? true : status == "disabled" ? false : null, owns, configuration.Error is null, canWrite, status == "disabled" && canWrite == true && executableAvailable, configuration.Error is null && owns && canWrite == true,
            executableAvailable, "machine", ExecutablePath, RegistrationCommand, debugger, filter, revision, DateTime.UtcNow, conflict, configuration.Error, writeError);
    }
    private sealed record RegistryValue(uint Type, byte[] Data);
    private sealed record Configuration(Dictionary<string, RegistryValue> Values, List<string> Subkeys, Unavailable? Error);
}
