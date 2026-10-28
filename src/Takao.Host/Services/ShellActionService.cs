using System.Runtime.InteropServices;
using Takao.Host.Models;
using Takao.Host.Native;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Takao.Host.Services;

public sealed class ShellActionService(ShellDispatcher shell, SnapshotService snapshots)
{
    public Task<string> ProcessFileAsync(uint pid, FileActionRequest request)
    {
        if (request.Action is not ("openLocation" or "properties")) throw new ApiException(400, "invalidFileAction", "只允许打开文件位置或Windows属性。");
        ProcessAccess.Parse(request.CreationTimeFileTime);
        return shell.InvokeAsync(() =>
        {
            using var handle = ProcessAccess.Open(pid, request.CreationTimeFileTime);
            var path = ProcessAccess.Path(handle);
            if (request.Action == "openLocation") Reveal(path); else Execute(path, "properties", true);
            return "Windows已接受打开请求。";
        });
    }
    public Task<string> OpenSystemLocationAsync(string location)
    {
        var path = location switch
        {
            "startupSettings" => "ms-settings:startupapps",
            "userStartup" => Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "commonStartup" => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            "services" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "services.msc"),
            _ => throw new ApiException(400, "invalidSystemLocation", "只允许启动应用设置、当前/公共Startup文件夹或服务管理器。")
        };
        if (string.IsNullOrWhiteSpace(path)) throw new ApiException(404, "systemLocationUnavailable", "Windows未提供该系统位置。");
        return shell.InvokeAsync(() => { Execute(path, "open", false); return "Windows已接受打开请求。"; });
    }
    public Task<string> StartupLocationAsync(string id)
    {
        if (snapshots.Current?.StartupItems.Any(p => p.Id == id) != true) throw new ApiException(404, "startupNotFound", "启动项不存在或已变化，请刷新列表。");
        return shell.InvokeAsync(() =>
        {
            var entry = SlowDataReader.ReadStartup([]).FirstOrDefault(p => p.Id == id) ?? throw new ApiException(404, "startupNotFound", "该启动来源已变化，请刷新列表。");
            if (!entry.CanOpenLocation || entry.LocationPath is null) throw new ApiException(400, "startupLocationUnavailable", entry.LocationReason ?? "该来源无法可靠定位。");
            Reveal(entry.LocationPath);
            return "Windows已接受打开来源文件位置的请求；原始注册表来源仍以列表为准。";
        });
    }
    private static void Reveal(string path)
    {
        // Standard Explorer /select command. The path comes only from an
        // identity-validated existing executable or a re-read startup source;
        // Windows filenames cannot contain a quote, and no shell is involved.
        Execute(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "open", false, $"/select,\"{path}\"");
    }
    private static unsafe void Execute(string path, string verb, bool properties, string? arguments = null)
    {
        fixed (char* pathPointer = path)
        fixed (char* verbPointer = verb)
        fixed (char* argumentsPointer = arguments)
        {
            var info = new SHELLEXECUTEINFOW { cbSize = (uint)sizeof(SHELLEXECUTEINFOW), fMask = PInvoke.SEE_MASK_FLAG_NO_UI | (properties ? PInvoke.SEE_MASK_INVOKEIDLIST : 0u), lpFile = new PCWSTR(pathPointer), lpVerb = new PCWSTR(verbPointer), lpParameters = new PCWSTR(argumentsPointer), nShow = 1 /* SW_SHOWNORMAL */ };
            if (!PInvoke.ShellExecuteEx(ref info)) ProcessAccess.Throw("Windows Shell未能打开目标");
        }
    }
}
