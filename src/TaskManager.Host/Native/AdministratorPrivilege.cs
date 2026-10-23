using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace TaskManager.Host.Native;

// The manifest protects the apphost. This check also protects direct DLL and headless entry.
internal static class AdministratorPrivilege
{
    internal static unsafe void EnsureElevated()
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)Environment.ProcessId);
        if (process.IsInvalid) throw QueryFailed("打开当前进程令牌查询句柄失败");
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token)) throw QueryFailed("打开当前进程令牌失败");
        using (token)
        {
            TOKEN_ELEVATION elevation = default;
            var size = (uint)sizeof(TOKEN_ELEVATION);
            if (!PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation, &elevation, size, out var returned)) throw QueryFailed("查询当前进程的管理员提升状态失败");
            if (returned != size) throw new StartupPrivilegeException("administratorTokenQueryFailed", "Windows返回的令牌提升状态长度无效，程序未启动。", 13);
            if (elevation.TokenIsElevated == 0) throw new StartupPrivilegeException("administratorRequired", "蓝域任务管理器需要管理员权限。请通过 TaskManager.Host.exe 启动并同意 Windows UAC 请求；未提升的 DLL、无窗口和维护入口均不能运行。", 740);
        }
    }

    private static StartupPrivilegeException QueryFailed(string action)
    {
        var code = Marshal.GetLastPInvokeError();
        return new("administratorTokenQueryFailed", NativeSupport.Error(code, action).Message + "；无法确认实际管理员权限，程序未启动。", code == 0 ? 1 : code);
    }
}

internal sealed class StartupPrivilegeException(string code, string message, int exitCode) : Exception(message)
{
    internal string Code { get; } = code;
    internal int ExitCode { get; } = exitCode;
}
