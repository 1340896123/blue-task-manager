using System.Globalization;
using System.Runtime.InteropServices;
using TaskManager.Host.Models;
using TaskManager.Host.Native;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace TaskManager.Host.Services;

public sealed class ProcessActionService
{
    public unsafe ProcessActionResult Start(StartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Executable) || request.Executable.Length > 32700 || request.Executable.Contains('\0') || request.Executable.Contains('"') || (request.Arguments?.Contains('\0') ?? false))
            throw new ApiException(400, "invalidExecutable", "请输入有效的可执行文件路径；程序路径与参数需分别填写。");
        var executable = ResolveExecutable(Environment.ExpandEnvironmentVariables(request.Executable.Trim()));
        var text = $"\"{executable}\"{(string.IsNullOrEmpty(request.Arguments) ? "" : " " + request.Arguments)}";
        if (text.Length > 32766) throw new ApiException(400, "commandTooLong", "程序路径和参数过长。");
        Span<char> command = (text + '\0').ToCharArray();
        var startup = new STARTUPINFOW { cb = (uint)sizeof(STARTUPINFOW) };
        if (!PInvoke.CreateProcess(executable, ref command, null, null, false, PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT, null,
                Path.GetDirectoryName(executable), startup, out var process)) ThrowNative("启动任务失败");
        try
        {
            var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle((nint)process.hProcess.Value, ownsHandle: false);
            using (handle)
            {
                if (PInvoke.GetProcessTimes(handle, out var creation, out _, out _, out _))
                {
                    var creationTime = NativeSupport.FileTime(creation).ToString(CultureInfo.InvariantCulture);
                    ProcessProtection.Register(process.dwProcessId, creationTime);
                    return new(process.dwProcessId, creationTime, $"{process.dwProcessId}:{creationTime}", "任务已由 Windows 创建。");
                }
                return new(process.dwProcessId, null, null, "Windows 已创建任务，但它已退出或创建时间无法读取；请刷新列表确认。");
            }
        }
        finally { PInvoke.CloseHandle(process.hThread); PInvoke.CloseHandle(process.hProcess); }
    }

    public ProcessActionResult Terminate(uint pid, TerminateRequest request)
    {
        if (!ulong.TryParse(request.CreationTimeFileTime, NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected == 0)
            throw new ApiException(400, "identityRequired", "结束进程必须提供有效的创建 FILETIME 身份。");
        if (pid is 0 or 4 || pid == Environment.ProcessId) throw new ApiException(403, "protectedProcess", "系统或本应用主机进程受保护。");
        var processes = Win32Collector.EnumerateBaseProcesses();
        if (ProcessProtection.Family(processes).Contains(pid)) throw new ApiException(403, "protectedProcess", "系统关键进程或本应用进程族受保护。");
        // Opening once and checking/acting through this exact handle closes the PID reuse race.
        using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE | PROCESS_ACCESS_RIGHTS.PROCESS_SYNCHRONIZE, false, pid);
        if (handle.IsInvalid) ThrowNative("无法打开结束目标");
        if (!PInvoke.GetProcessTimes(handle, out var creation, out var exit, out _, out _)) ThrowNative("无法确认结束目标身份");
        if (NativeSupport.FileTime(creation) != expected) throw new ApiException(409, "identityChanged", "目标创建时间已改变；该 PID 已被另一进程使用，未执行结束。");
        if (NativeSupport.FileTime(exit) != 0) throw new ApiException(404, "processExited", "目标进程已退出。");
        if (!PInvoke.IsProcessCritical(handle, out var critical)) ThrowNative("无法确认进程关键状态，已保守拒绝结束", forceForbidden: true);
        if (critical) throw new ApiException(403, "criticalProcess", "Windows 将目标标记为关键进程，禁止结束。");
        if (!PInvoke.TerminateProcess(handle, 1)) ThrowNative("结束任务失败");
        var completed = PInvoke.WaitForSingleObject(handle, 3000) == Windows.Win32.Foundation.WAIT_EVENT.WAIT_OBJECT_0;
        return new(pid, request.CreationTimeFileTime, $"{pid}:{request.CreationTimeFileTime}", completed ? "已确认目标进程退出。" : "Windows已接受结束请求，但3秒内未确认退出；请刷新实际状态，勿盲目重试。", completed);
    }

    private static string ResolveExecutable(string value)
    {
        if (Path.IsPathFullyQualified(value)) return value;
        var candidates = new[] { Environment.GetFolderPath(Environment.SpecialFolder.System), Environment.GetFolderPath(Environment.SpecialFolder.Windows) }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)).Where(path => !string.IsNullOrWhiteSpace(path));
        foreach (var folder in candidates)
        {
            try
            {
                var full = Path.Combine(folder.Trim('"'), value);
                if (File.Exists(full)) return Path.GetFullPath(full);
                if (!Path.HasExtension(value) && File.Exists(full + ".exe")) return Path.GetFullPath(full + ".exe");
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { }
        }
        throw new ApiException(400, "executableNotFound", "未找到可执行文件，请输入完整路径。", 2);
    }

    private static void ThrowNative(string action, bool forceForbidden = false)
    {
        var code = Marshal.GetLastPInvokeError();
        var error = NativeSupport.Error(code, action);
        throw new ApiException(forceForbidden || code == 5 ? 403 : code is 87 or 1168 ? 404 : 400, error.Code, error.Message, code);
    }
}
