using Aprillz.MewUI;
using Takao.Host.Native;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Takao.Host.Services;

/// <summary>
/// In-process desktop window adapter. MewUI owns the native window and the
/// ASP.NET host owns the same process; no renderer child process is created.
/// </summary>
public sealed class DesktopWindowService(PreferencesService preferences) : IAsyncDisposable
{
    private readonly object gate = new();
    private Window? window;

    public uint? ProcessId => window is null ? null : (uint)Environment.ProcessId;
    public bool Available => window is { Handle: not 0 };

    public void Attach(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        lock (gate) window = mainWindow;
    }

    public void Detach(Window mainWindow)
    {
        lock (gate)
        {
            if (ReferenceEquals(window, mainWindow)) window = null;
        }
    }

    public Task ExecuteAsync(string action)
    {
        var current = CurrentWindow();
        var hwnd = new HWND(current.Handle);
        switch (action)
        {
            case "minimize":
                PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_MINIMIZE);
                break;
            case "maximize":
                PInvoke.ShowWindow(hwnd, PInvoke.IsZoomed(hwnd) ? SHOW_WINDOW_CMD.SW_RESTORE : SHOW_WINDOW_CMD.SW_MAXIMIZE);
                break;
            case "close":
                if (Application.IsRunning && Application.Current.Dispatcher is { } dispatcher)
                    dispatcher.BeginInvoke(current.Close);
                else
                    current.Close();
                break;
            case "always-on-top":
                var next = !DesktopTopmost.Read(hwnd);
                DesktopTopmost.Set(hwnd, next);
                preferences.SaveWindow((preferences.Current.Preferences.Window ?? new()) with { AlwaysOnTop = next });
                break;
            case "zoom-in":
            case "zoom-out":
            case "zoom-reset":
                throw new ApiException(501, "mewUiLocalDisplaySetting", "MewUI 原生界面不使用网页缩放；请使用 Windows 显示设置。");
            default:
                throw new ApiException(400, "invalidWindowAction", "未知窗口操作。");
        }
        return Task.CompletedTask;
    }

    public Task<object> BrowseExecutableAsync()
        => throw new ApiException(501, "mewUiLocalFileDialog", "程序选择器由 MewUI 原生界面本地打开，请使用运行新任务对话框中的浏览按钮。");

    public Task<object> StateAsync()
    {
        var current = TryGetWindow();
        if (current is null)
            return Task.FromResult<object>(new { available = false, framework = "MewUI Win32", processId = ProcessId, alwaysOnTop = (bool?)null, zoomFactor = (double?)null, zoomSupported = false });
        var hwnd = new HWND(current.Handle);
        return Task.FromResult<object>(new
        {
            available = true,
            framework = "MewUI Win32",
            processId = ProcessId,
            alwaysOnTop = DesktopTopmost.Read(hwnd),
            zoomFactor = (double?)null,
            zoomSupported = false
        });
    }

    public ValueTask DisposeAsync()
    {
        var current = TryGetWindow();
        if (current is not null && Application.IsRunning) current.Close();
        return ValueTask.CompletedTask;
    }

    private Window CurrentWindow()
        => TryGetWindow() ?? throw new ApiException(503, "mewUiWindowUnavailable", "MewUI 主窗口尚未创建或已经关闭。");

    private Window? TryGetWindow()
    {
        lock (gate) return window is { Handle: not 0 } current ? current : null;
    }
}
