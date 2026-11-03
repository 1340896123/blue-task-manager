using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Takao.Host.Native;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.JobObjects;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Takao.Host.Services;

// The unnamed Windows job owns this exact native child, including abrupt Host exit.
public sealed class DesktopWindowService(PreferencesService preferences) : IAsyncDisposable
{
    private SafeFileHandle? job;
    private SafeFileHandle? process;
    private uint processId;
    private string? creationTime;
    private Task? watcher;
    private int stopped;
    public uint? ProcessId => processId == 0 ? null : processId;
    public bool Available => process is { IsClosed: false, IsInvalid: false } current && PInvoke.WaitForSingleObject(current, 0) == WAIT_EVENT.WAIT_TIMEOUT;

    public async Task OpenAsync(string origin, IHostApplicationLifetime lifetime)
    {
        if (process is not null) throw new InvalidOperationException("The Qt desktop has already been started.");
        StartChild(origin);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (!Available)
            {
                uint exitCode = 0;
                if (process is not null) PInvoke.GetExitCodeProcess(process, out exitCode);
                throw new ApiException(503, "qtDesktopExited", $"Qt桌面在创建主窗口前退出（代码 {exitCode}）。请检查完整Qt运行库与platforms插件。", unchecked((int)exitCode));
            }
            if (!Window(required: false).IsNull) { watcher = WatchExitAsync(lifetime); return; }
            await Task.Delay(100, lifetime.ApplicationStopping);
        }
        throw new ApiException(503, "qtWindowStartupTimeout", "Qt桌面20秒内未创建可见主窗口，已停止本次主机；请查看完整发布目录和启动日志。");
    }

    private unsafe void StartChild(string origin)
    {
        var executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Takao.Qt.exe"));
        if (!File.Exists(executable)) throw new ApiException(503, "qtDesktopMissing", "未找到同目录 Takao.Qt.exe。请运行完整Qt桌面构建或使用完整发布目录。");
        // Only public routing/lifetime metadata enters the command line. Qt
        // obtains its in-memory write token from /api/bootstrap.
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1" || endpoint.Port is < 1024 or > 65535)
            throw new ArgumentException("Qt requires an explicit local HTTP origin.", nameof(origin));
        job = PInvoke.CreateJobObject(null, null);
        if (job.IsInvalid) ThrowNative("创建Qt进程作业失败");
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!PInvoke.SetInformationJobObject(job, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, new ReadOnlySpan<byte>(&limits, sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
            ThrowNative("配置Qt进程退出清理失败");
        Span<char> command = ($"\"{executable}\" --origin={origin} --host-pid={Environment.ProcessId}" + '\0').ToCharArray();
        var startup = new STARTUPINFOW { cb = (uint)sizeof(STARTUPINFOW) };
        if (!PInvoke.CreateProcess(executable, ref command, null, null, false,
                PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT | PROCESS_CREATION_FLAGS.CREATE_SUSPENDED,
                null, AppContext.BaseDirectory, startup, out var child)) ThrowNative("启动Qt桌面失败");
        process = new SafeFileHandle((nint)child.hProcess.Value, ownsHandle: true);
        try
        {
            if (!PInvoke.AssignProcessToJobObject(job, process)) ThrowNative("将Qt桌面加入本次进程作业失败");
            if (!PInvoke.GetProcessTimes(process, out var created, out _, out _, out _)) ThrowNative("确认Qt桌面身份失败");
            processId = child.dwProcessId;
            creationTime = NativeSupport.FileTime(created).ToString(CultureInfo.InvariantCulture);
            ProcessProtection.RegisterOwned(processId, creationTime);
            if (PInvoke.ResumeThread(child.hThread) == uint.MaxValue) ThrowNative("恢复Qt桌面主线程失败");
        }
        catch
        {
            // Assignment can fail before the child enters our job. Use the
            // original suspended process handle, never a PID lookup, to clean it.
            PInvoke.TerminateProcess(process, 1);
            process.Dispose(); process = null;
            job.Dispose(); job = null;
            throw;
        }
        finally { PInvoke.CloseHandle(child.hThread); }
    }

    private async Task WatchExitAsync(IHostApplicationLifetime lifetime)
    {
        while (Volatile.Read(ref stopped) == 0 && Available) await Task.Delay(100);
        if (Volatile.Read(ref stopped) == 0) lifetime.StopApplication();
    }

    private HWND Window(bool required = true)
    {
        HWND found = default;
        if (Available)
        {
            PInvoke.EnumWindows((window, unused) =>
            {
                PInvoke.GetWindowThreadProcessId(window, out var pid);
                if (pid != processId || !PInvoke.IsWindowVisible(window)) return true;
                Span<char> title = stackalloc char[256];
                var length = PInvoke.GetWindowText(window, title);
                if (new string(title[..length]) != "Takao") return true;
                // The original lifetime handle authenticates this PID. HWNDs
                // are discovered locally and are never accepted from HTTP.
                if (process is null || !PInvoke.GetProcessTimes(process, out var created, out var exited, out _, out _) ||
                    NativeSupport.FileTime(created).ToString(CultureInfo.InvariantCulture) != creationTime || NativeSupport.FileTime(exited) != 0) return false;
                found = window; return false;
            }, default);
        }
        if (required && found.IsNull) throw new ApiException(503, "qtWindowUnavailable", "Qt主窗口尚未创建、已经关闭或当前以无窗口方式运行，请稍后重试。");
        return found;
    }

    public Task ExecuteAsync(string action)
    {
        var window = Window();
        switch (action)
        {
            case "minimize": PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_MINIMIZE); break;
            case "maximize": PInvoke.ShowWindow(window, PInvoke.IsZoomed(window) ? SHOW_WINDOW_CMD.SW_RESTORE : SHOW_WINDOW_CMD.SW_MAXIMIZE); break;
            case "close":
                if (!PInvoke.PostMessage(window, 0x0010, default, default)) ThrowNative("请求关闭Qt窗口失败");
                break;
            case "always-on-top":
                var next = !DesktopTopmost.Read(window);
                DesktopTopmost.Set(window, next);
                preferences.SaveWindow((preferences.Current.Preferences.Window ?? new()) with { AlwaysOnTop = next });
                break;
            case "zoom-in": case "zoom-out": case "zoom-reset":
                throw new ApiException(501, "qtLocalDisplaySetting", "Qt原生界面没有网页缩放；请使用Qt界面中的显示设置。");
            default: throw new ApiException(400, "invalidWindowAction", "未知窗口操作。");
        }
        return Task.CompletedTask;
    }

    public Task<object> BrowseExecutableAsync() => throw new ApiException(501, "qtLocalFileDialog", "程序选择器由Qt原生界面本地打开，请使用运行新任务对话框中的浏览按钮。");

    public Task<object> StateAsync()
    {
        var window = Window(required: false);
        return Task.FromResult<object>(new
        {
            available = !window.IsNull,
            framework = "Qt 6 Widgets",
            processId = ProcessId,
            alwaysOnTop = window.IsNull ? (bool?)null : DesktopTopmost.Read(window),
            zoomFactor = (double?)null,
            zoomSupported = false
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        var window = Window(required: false);
        if (!window.IsNull) PInvoke.PostMessage(window, 0x0010, default, default);
        for (var attempt = 0; attempt < 30 && Available; attempt++) await Task.Delay(100);
        // Closing the last non-inheritable job handle terminates only our child.
        job?.Dispose(); job = null;
        if (watcher is not null) await watcher;
        process?.Dispose(); process = null;
    }

    private static void ThrowNative(string action)
    {
        var code = Marshal.GetLastPInvokeError();
        throw new ApiException(503, "qtDesktopStartupFailed", NativeSupport.Error(code, action).Message, code);
    }
}
