using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskManager.Host.Native;

internal sealed record TaskManagerEntryPreparation(string[] Arguments, bool Forwarded, string? Message);
internal static class TaskManagerEntry
{
    internal static TaskManagerEntryPreparation Prepare(string[] args)
    {
        var index = Array.IndexOf(args, TaskManagerIntegrationService.EntryFlag);
        if (index < 0) return new(args, false, null);
        if (Array.LastIndexOf(args, TaskManagerIntegrationService.EntryFlag) != index) throw new ArgumentException("任务管理器入口标记不能重复。");
        if (index + 1 < args.Length && !IsSystemTaskManager(args[index + 1])) throw new ArgumentException("入口标记后只接受Windows taskmgr.exe的系统路径及其追加参数。");
        // IFEO appends the original target and its arguments after the fixed debugger command.
        // Keep normal host options before our flag; never feed taskmgr options to ASP.NET config.
        var cleaned = args[..index];
        var currentSession = PInvoke.ProcessIdToSessionId((uint)Environment.ProcessId, out var session) ? session : (uint?)null;
        using var current = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)Environment.ProcessId);
        var owner = current.IsInvalid ? null : ReadSid(current);
        if (currentSession is null || owner is null) return new(cleaned, false, null);
        var expectedElectron = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".electron", "node_modules", "electron", "dist", "electron.exe"));
        var processes = Win32Collector.EnumerateBaseProcesses().ToDictionary(p => p.Pid);
        HWND existing = default; var foreground = false;
        PInvoke.EnumWindows((window, unused) =>
        {
            if (!PInvoke.IsWindowVisible(window) || ClassName(window) != "Chrome_WidgetWin_1" || Title(window) is not ("蓝域任务管理器" or "蓝域 · 任务管理器")) return true;
            PInvoke.GetWindowThreadProcessId(window, out var pid);
            if (!PInvoke.ProcessIdToSessionId(pid, out var candidateSession) || candidateSession != currentSession) return true;
            using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process.IsInvalid || !PInvoke.GetProcessTimes(process, out var created, out var exited, out _, out _) || NativeSupport.FileTime(exited) != 0) return true;
            if (!string.Equals(ReadPath(process), expectedElectron, StringComparison.OrdinalIgnoreCase) || ReadSid(process) != owner) return true;
            var childTime = NativeSupport.FileTime(created); var childPid = pid; var seen = new HashSet<uint>(); var verifiedHost = false;
            for (var depth = 0; depth < 16 && processes.TryGetValue(childPid, out var child) && seen.Add(childPid); depth++)
            {
                if (child.ParentPid is 0 or 4 || child.ParentPid == Environment.ProcessId) break;
                using var parent = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, child.ParentPid);
                if (parent.IsInvalid || !PInvoke.GetProcessTimes(parent, out var parentCreated, out var parentExited, out _, out _) || NativeSupport.FileTime(parentExited) != 0 || NativeSupport.FileTime(parentCreated) > childTime) break;
                if (string.Equals(ReadPath(parent), TaskManagerIntegrationService.ExecutablePath, StringComparison.OrdinalIgnoreCase) && ReadSid(parent) == owner) { verifiedHost = true; break; }
                childPid = child.ParentPid; childTime = NativeSupport.FileTime(parentCreated);
            }
            if (!verifiedHost || !PInvoke.IsWindow(window)) return true;
            PInvoke.GetWindowThreadProcessId(window, out var actualPid);
            if (actualPid != pid) return true;
            if (PInvoke.IsIconic(window)) PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_RESTORE);
            else PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOW);
            PInvoke.SetForegroundWindow(window);
            foreground = PInvoke.GetForegroundWindow() == window;
            existing = window; return false;
        }, default);
        return existing.IsNull ? new(cleaned, false, null) : new(cleaned, true, foreground ? "已有本目录任务管理器窗口已恢复并切换到前台；未启动重复主机。" : "已有本目录任务管理器窗口已恢复；Windows限制了前台切换，未启动重复主机。");
    }

    private static bool IsSystemTaskManager(string path)
    {
        if (path.Equals("taskmgr.exe", StringComparison.OrdinalIgnoreCase)) return true;
        if (!Path.IsPathFullyQualified(path)) return false;
        try
        {
            var full = Path.GetFullPath(path);
            return new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskmgr.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "taskmgr.exe") }.Contains(full, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { return false; }
    }
    private static string? ReadPath(SafeFileHandle process)
    { var path = new char[32768]; uint size = (uint)path.Length; return PInvoke.QueryFullProcessImageName(process, 0, path, ref size) ? new string(path, 0, (int)size) : null; }
    private static string ClassName(HWND window) { Span<char> text = stackalloc char[256]; var length = PInvoke.GetClassName(window, text); return new string(text[..length]); }
    private static string Title(HWND window) { Span<char> text = stackalloc char[256]; var length = PInvoke.GetWindowText(window, text); return new string(text[..length]); }
    private static unsafe string? ReadSid(SafeFileHandle process)
    {
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token)) return null;
        using (token)
        {
            PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenUser, null, 0, out var size);
            if (size is 0 or > 65536) return null;
            var buffer = new byte[size];
            fixed (byte* bytes = buffer)
            {
                if (!PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenUser, bytes, size, out _)) return null;
                var sid = ((TOKEN_USER*)bytes)->User.Sid; var length = PInvoke.GetLengthSid(sid);
                if (length is 0 or > 65536) return null;
                var data = new byte[length]; Marshal.Copy((nint)sid.Value, data, 0, (int)length); return Convert.ToHexString(data);
            }
        }
    }
}
