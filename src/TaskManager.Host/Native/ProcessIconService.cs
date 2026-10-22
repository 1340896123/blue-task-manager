using System.Drawing;
using System.Drawing.Imaging;
using TaskManager.Host.Services;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace TaskManager.Host.Native;

public sealed class ProcessIconService([FromKeyedServices("icons")] ShellDispatcher shell)
{
    private readonly Dictionary<string, byte[]?> cache = new(StringComparer.OrdinalIgnoreCase);
    public Task<byte[]?> ReadAsync(uint pid, string creation)
    {
        using var handle = ProcessAccess.Open(pid, creation);
        var path = ProcessAccess.Path(handle);
        return shell.InvokeAsync(() => Read(path));
    }
    private unsafe byte[]? Read(string path)
    {
        string key;
        try { key = $"{Path.GetFullPath(path)}|{File.GetLastWriteTimeUtc(path).Ticks}"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        if (cache.TryGetValue(key, out var png)) return png;
        var info = new SHFILEINFOW();
        if (PInvoke.SHGetFileInfo(path, 0, ref info, SHGFI_FLAGS.SHGFI_ICON | SHGFI_FLAGS.SHGFI_SMALLICON) != 0 && !info.hIcon.IsNull)
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
        if (cache.Count >= 512) cache.Remove(cache.Keys.First()); cache[key] = png;
        return png;
    }
}
