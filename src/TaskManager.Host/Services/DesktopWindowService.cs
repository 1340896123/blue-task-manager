using ElectronNET.API;
using ElectronNET.API.Entities;
using TaskManager.Host.Models;

namespace TaskManager.Host.Services;

public sealed class DesktopWindowService(PreferencesService preferences)
{
    private BrowserWindow? window;
    private double zoomFactor = 1;
    private int browsing;
    private CancellationTokenSource? saveBounds;
    public bool Available => window is not null;

    public async Task OpenAsync(string origin, IHostApplicationLifetime lifetime)
    {
        Electron.Menu.SetApplicationMenu([]);
        var saved = preferences.Current.Preferences.Window ?? new();
        zoomFactor = saved.ZoomFactor;
        window = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions
        {
            Width = saved.Width, Height = saved.Height, MinWidth = 960, MinHeight = 640,
            Frame = false, Show = false, AutoHideMenuBar = true,
            Title = "蓝域任务管理器", BackgroundColor = "#F3F7FC",
            WebPreferences = new WebPreferences { ContextIsolation = true, NodeIntegration = false, Sandbox = true }
        }, origin);
        window.SetMenuBarVisibility(false);
        window.SetAlwaysOnTop(saved.AlwaysOnTop);
        window.WebContents.SetZoomFactor(zoomFactor);
        window.OnReadyToShow += () => window?.Show();
        window.OnResize += () => { saveBounds?.Cancel(); saveBounds = new(); _ = SaveBoundsAsync(saveBounds.Token); };
        window.OnClosed += () => { window = null; lifetime.StopApplication(); };
    }

    public async Task ExecuteAsync(string action)
    {
        var current = window ?? throw new ApiException(503, "headlessUnavailable", "当前主机以无窗口方式运行，窗口操作不可用。");
        switch (action)
        {
            case "minimize": current.Minimize(); break;
            case "maximize": if (await current.IsMaximizedAsync()) current.Unmaximize(); else current.Maximize(); break;
            case "close": current.Close(); break;
            case "zoom-in": zoomFactor = Math.Min(2, zoomFactor + .25); current.WebContents.SetZoomFactor(zoomFactor); break;
            case "zoom-out": zoomFactor = Math.Max(.75, zoomFactor - .25); current.WebContents.SetZoomFactor(zoomFactor); break;
            case "zoom-reset": zoomFactor = 1; current.WebContents.SetZoomFactor(1); break;
            case "always-on-top":
                var next = !await current.IsAlwaysOnTopAsync(); current.SetAlwaysOnTop(next);
                if (await current.IsAlwaysOnTopAsync() != next) throw new ApiException(409, "windowStateNotApplied", "窗口置顶状态未能读回确认，请重试。");
                preferences.SaveWindow((preferences.Current.Preferences.Window ?? new()) with { AlwaysOnTop = next });
                break;
            default: throw new ApiException(400, "invalidWindowAction", "未知窗口操作。");
        }
        if (action.StartsWith("zoom-", StringComparison.Ordinal)) preferences.SaveWindow((preferences.Current.Preferences.Window ?? new()) with { ZoomFactor = zoomFactor });
    }

    public async Task<object> BrowseExecutableAsync()
    {
        var current = window ?? throw new ApiException(503, "headlessUnavailable", "当前无桌面窗口，无法打开本机程序选择器。");
        if (Interlocked.CompareExchange(ref browsing, 1, 0) != 0) throw new ApiException(409, "fileDialogBusy", "程序选择器已打开，请先完成或取消现有选择。");
        try
        {
            var paths = await Electron.Dialog.ShowOpenDialogAsync(current, new OpenDialogOptions { Title = "选择要运行的程序", Properties = [OpenDialogProperty.openFile], Filters = [new FileFilter { Name = "Windows可执行程序", Extensions = ["exe"] }] });
            if (paths is null || paths.Length == 0) return new { cancelled = true, path = (string?)null };
            var path = Path.GetFullPath(paths[0]);
            if (!File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ApiException(400, "invalidExecutable", "请选择现存的.exe可执行程序。");
            return new { cancelled = false, path };
        }
        finally { Volatile.Write(ref browsing, 0); }
    }
    public async Task<object> StateAsync() => new { available = Available, alwaysOnTop = window is { } current && await current.IsAlwaysOnTopAsync(), zoomFactor };
    private async Task SaveBoundsAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(600, token);
            var current = window; if (current is null || await current.IsMaximizedAsync()) return;
            var bounds = await current.GetBoundsAsync(); token.ThrowIfCancellationRequested();
            preferences.SaveWindow((preferences.Current.Preferences.Window ?? new()) with { Width = Math.Clamp(bounds.Width, 960, 3840), Height = Math.Clamp(bounds.Height, 640, 2160) });
        }
        catch (OperationCanceledException) { }
        catch (ApiException) { /* PreferencesService exposes persistence failures to the UI. */ }
    }
}
