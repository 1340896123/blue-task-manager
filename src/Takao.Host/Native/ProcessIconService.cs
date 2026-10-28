using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Takao.Host.Models;
using Takao.Host.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Takao.Host.Native;

public sealed class ProcessIconService([FromKeyedServices("icons")] ShellDispatcher shell)
{
    private readonly Dictionary<string, (byte[]? Png, DateTime ReadAt, bool Retry)> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string? Path, DateTime ReadAt)> servicePaths = new(StringComparer.OrdinalIgnoreCase);
    public Task<byte[]?> ReadAsync(uint pid, string creation)
    {
        using var handle = ProcessAccess.Open(pid, creation);
        var path = ProcessAccess.Path(handle);
        var appId = ApplicationIdentity.Read(handle);
        return shell.InvokeAsync(() => Read(path, appId));
    }

    public Task<byte[]?> ReadApplicationAsync(string kind, string id, SnapshotDto? snapshot)
    {
        if (id.Length is 0 or > 32768) throw new ApiException(400, "invalidIconSource", "图标来源标识无效。");
        if (snapshot is null) return Task.FromResult<byte[]?>(null);
        string? path = null, appId = null;
        switch (kind)
        {
            case "process":
                var process = snapshot.Processes.FirstOrDefault(row => row.Identity == id);
                if (process is null) return Task.FromResult<byte[]?>(null);
                path = process.Path; appId = process.ApplicationUserModelId;
                break;
            case "history":
                var history = snapshot.History.FirstOrDefault(row => row.Identity == id);
                if (history is null) return Task.FromResult<byte[]?>(null);
                path = history.IconPath; appId = history.ApplicationUserModelId;
                break;
            case "startup":
                var startup = snapshot.StartupItems.FirstOrDefault(row => row.Id == id);
                if (startup is null) return Task.FromResult<byte[]?>(null);
                return shell.InvokeAsync(() => ReadSafely(() => Read(startup.LocationPath ?? ApplicationCommand.Executable(startup.Command), null)));
            case "service":
                var service = snapshot.Services.FirstOrDefault(row => row.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (service is null) return Task.FromResult<byte[]?>(null);
                // Read SCM configuration, including stopped services; a reused PID is
                // never treated as proof of a service's executable identity.
                return shell.InvokeAsync(() => ReadSafely(() => Read(ServicePath(service.Name), null)));
            default: throw new ApiException(400, "invalidIconSource", "不支持的应用图标来源。");
        }
        return shell.InvokeAsync(() => ReadSafely(() => Read(path, appId)));
    }

    private static byte[]? ReadSafely(Func<byte[]?> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or ExternalException) { return null; }
    }

    private unsafe string? ServicePath(string name)
    {
        if (servicePaths.TryGetValue(name, out var cached) && DateTime.UtcNow - cached.ReadAt < TimeSpan.FromSeconds(15)) return cached.Path;
        string? path = null;
        using var manager = PInvoke.OpenSCManager(null, null, PInvoke.SC_MANAGER_CONNECT);
        if (!manager.IsInvalid)
        {
            using var service = PInvoke.OpenService(manager, name, PInvoke.SERVICE_QUERY_CONFIG);
            if (!service.IsInvalid)
            {
                PInvoke.QueryServiceConfig(service, null, 0, out var size);
                if (size is > 0 and <= 65536)
                {
                    var bytes = new byte[size];
                    fixed (byte* buffer = bytes)
                    {
                        var config = (Windows.Win32.System.Services.QUERY_SERVICE_CONFIGW*)buffer;
                        if (PInvoke.QueryServiceConfig(service, config, size, out _)) path = ApplicationCommand.Executable(config->lpBinaryPathName.ToString());
                    }
                }
            }
        }
        if (servicePaths.Count >= 512) servicePaths.Remove(servicePaths.Keys.First());
        servicePaths[name] = (path, DateTime.UtcNow);
        return path;
    }

    private unsafe byte[]? Read(string? path, string? appId)
    {
        if (path is null && appId is null) return null;
        string key;
        try { key = $"{appId}|{(path is null ? null : Path.GetFullPath(path))}|{(path is null ? 0 : File.GetLastWriteTimeUtc(path).Ticks)}"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        if (cache.TryGetValue(key, out var cached) && (!cached.Retry || DateTime.UtcNow - cached.ReadAt < TimeSpan.FromSeconds(15))) return cached.Png;
        // A packaged desktop executable can have its own file icon, as shown
        // by Windows Properties. Only a real resource outranks the package icon;
        // SHGetFileInfo also succeeds with a generic icon for resource-less EXEs.
        var png = appId is not null && path is not null && File.Exists(path) ? ReadEmbedded(path) : null;
        if (png is null && appId is not null) png = ReadPackaged(appId);
        var retry = appId is not null && png is null;
        var info = new SHFILEINFOW();
        if (png is null && path is not null && File.Exists(path) && PInvoke.SHGetFileInfo(path, 0, ref info, SHGFI_FLAGS.SHGFI_ICON | SHGFI_FLAGS.SHGFI_SMALLICON) != 0 && !info.hIcon.IsNull)
        {
            try
            {
                using var icon = Icon.FromHandle((nint)info.hIcon.Value);
                using var bitmap = icon.ToBitmap();
                using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); png = stream.ToArray();
            }
            catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException) { png = null; }
            finally { PInvoke.DestroyIcon(info.hIcon); }
        }
        if (cache.Count >= 512) cache.Remove(cache.Keys.First()); cache[key] = (png, DateTime.UtcNow, retry || png is null);
        return png;
    }

    private static unsafe byte[]? ReadEmbedded(string path)
    {
        HICON nativeIcon = default;
        try
        {
            fixed (char* source = path)
                if (PInvoke.ExtractIconEx(source, 0, null, &nativeIcon, 1) == 0 || nativeIcon.IsNull) return null;
            using var icon = Icon.FromHandle((nint)nativeIcon.Value);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); return stream.ToArray();
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException) { return null; }
        finally { if (!nativeIcon.IsNull) PInvoke.DestroyIcon(nativeIcon); }
    }

    private static unsafe byte[]? ReadPackaged(string appId)
    {
        IShellItemImageFactory? item = null;
        HBITMAP nativeBitmap = default;
        try
        {
            var folder = new Guid("1e87508d-89c2-42f0-8a7e-645a0f50ca58");
            if (PInvoke.SHCreateItemInKnownFolder(folder, 0, appId, out item).Failed || item is null) return null;
            // Packaged apps can expose a white unplated glyph. Ask Shell for
            // the application background so it stays visible on our light surface.
            item.GetImage(new SIZE(32, 32), SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_ICONBACKGROUND, &nativeBitmap);
            if (nativeBitmap.IsNull) return null;
            // Shell returns a 32-bit DIB with premultiplied alpha. FromHbitmap loses
            // that alpha; copy its actual BGRA pixels into a managed PNG instead.
            DIBSECTION dib = default;
            if (PInvoke.GetObject(nativeBitmap, sizeof(DIBSECTION), &dib) == 0 || dib.dsBm.bmBits is null || dib.dsBm.bmBitsPixel != 32 || dib.dsBm.bmWidth is <= 0 or > 256 || dib.dsBm.bmHeight is <= 0 or > 256) return null;
            var height = dib.dsBm.bmHeight;
            using var bitmap = new Bitmap(dib.dsBm.bmWidth, height, PixelFormat.Format32bppPArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var bytes = Math.Min(Math.Abs(data.Stride), Math.Abs(dib.dsBm.bmWidthBytes));
                for (var y = 0; y < height; y++)
                {
                    var sourceY = dib.dsBmih.biHeight > 0 ? height - 1 - y : y;
                    Buffer.MemoryCopy((byte*)dib.dsBm.bmBits + sourceY * dib.dsBm.bmWidthBytes, (byte*)data.Scan0 + y * data.Stride, Math.Abs(data.Stride), bytes);
                }
            }
            finally { bitmap.UnlockBits(data); }
            using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); return stream.ToArray();
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidCastException) { return null; }
        finally
        {
            if (!nativeBitmap.IsNull) PInvoke.DeleteObject(nativeBitmap);
            if (item is not null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item);
        }
    }
}
