using ElectronNET.API;
using ElectronNET.API.Entities;
using Takao.Host.Models;
using Takao.Host.Native;

namespace Takao.Host.Services;

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
            Width = saved.Width, Height = saved.Height, UseContentSize = true, MinWidth = 960, MinHeight = 640,
            Frame = false, Show = false, AutoHideMenuBar = true,
            Title = "Takao", BackgroundColor = "#F3F7FC",
            Icon = Path.Combine(AppContext.BaseDirectory, ".electron", "takao.ico"),
            WebPreferences = new WebPreferences
            {
                ContextIsolation = true, NodeIntegration = false, Sandbox = true, BackgroundThrottling = true,
                Preload = Path.Combine(AppContext.BaseDirectory, ".electron", "window-preload.js")
            }
        }, origin);
        window.OnReadyToShow += () => window?.Show();
        // Electron.NET 0.6.0 adds a legacy Windows 10 frame compensation at
        // creation, including on Windows 11. Use the official content setter
        // and actual readback to restore the requested client size without any
        // hard-coded border subtraction.
        window.SetContentSize(saved.Width, saved.Height);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var actual = await window.GetContentBoundsAsync();
            if (actual.Width == saved.Width && actual.Height == saved.Height) break;
            await Task.Delay(50);
        }
        window.SetMenuBarVisibility(false);
        try { await DesktopTopmost.SetAsync(window, saved.AlwaysOnTop); }
        catch (ApiException) { /* A failed optional preference must not prevent the main window from showing. */ }
        window.WebContents.SetZoomFactor(zoomFactor);
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
                var next = !await DesktopTopmost.ReadAsync(current);
                await DesktopTopmost.SetAsync(current, next);
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
    public async Task<object> StateAsync() => new { available = Available, alwaysOnTop = window is { } current && await DesktopTopmost.ReadAsync(current), zoomFactor = window is { } zoomWindow ? await zoomWindow.WebContents.GetZoomFactorAsync() : zoomFactor };
    private async Task SaveBoundsAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(600, token);
            var current = window; if (current is null || await current.IsMaximizedAsync() || await current.IsMinimizedAsync()) return;
            var bounds = await current.GetContentBoundsAsync(); token.ThrowIfCancellationRequested();
            preferences.SaveWindow((preferences.Current.Preferences.Window ?? new()) with { Width = Math.Clamp(bounds.Width, 960, 3840), Height = Math.Clamp(bounds.Height, 640, 2160) });
        }
        catch (OperationCanceledException) { }
        catch (ApiException) { /* PreferencesService exposes persistence failures to the UI. */ }
    }
}
