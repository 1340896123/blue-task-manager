using ElectronNET.API;
using ElectronNET.API.Entities;
using TaskManager.Host.Models;

namespace TaskManager.Host.Services;

public sealed class DesktopWindowService
{
    private BrowserWindow? window;
    private double zoomFactor = 1;
    public bool Available => window is not null;

    public async Task OpenAsync(string origin, IHostApplicationLifetime lifetime)
    {
        Electron.Menu.SetApplicationMenu([]);
        window = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions
        {
            Width = 1380, Height = 860, MinWidth = 960, MinHeight = 640,
            Frame = false, Show = false, AutoHideMenuBar = true,
            Title = "蓝域任务管理器", BackgroundColor = "#F3F7FC",
            WebPreferences = new WebPreferences { ContextIsolation = true, NodeIntegration = false, Sandbox = true }
        }, origin);
        window.SetMenuBarVisibility(false);
        window.OnReadyToShow += () => window?.Show();
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
            default: throw new ApiException(400, "invalidWindowAction", "未知窗口操作。");
        }
    }
}
