using System.Drawing;
using System.Drawing.Imaging;
using Newtonsoft.Json;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

// Independent read-only icon inspection. Window reads require an exact test-owned identity.
internal static class ProductIconProbe
{
    internal static unsafe bool TryRun(string[] args)
    {
        if (args[0] == "icon-resource-count")
        {
            var path = Path.GetFullPath(args[1]);
            uint count;
            fixed (char* source = path) count = PInvoke.ExtractIconEx(source, -1, null, null, 0);
            Console.WriteLine(JsonConvert.SerializeObject(new { count }));
            return true;
        }
        if (args[0] == "icon-exe")
        {
            var path = Path.GetFullPath(args[1]);
            HICON large = default, small = default;
            fixed (char* source = path)
                if (PInvoke.ExtractIconEx(source, 0, &large, &small, 1) == 0 || large.IsNull || small.IsNull)
                    throw new InvalidOperationException("Executable icon resources were not readable.");
            try
            {
                Directory.CreateDirectory(args[2]);
                var result = new { large = Save(large, Path.Combine(args[2], "product-exe-large.png")), small = Save(small, Path.Combine(args[2], "product-exe-small.png")) };
                Console.WriteLine(JsonConvert.SerializeObject(result));
            }
            finally { PInvoke.DestroyIcon(large); PInvoke.DestroyIcon(small); }
            return true;
        }
        if (args[0] != "icon-window") return false;
        var window = new HWND((nint)long.Parse(args[1]));
        var expectedPid = uint.Parse(args[2]); var expectedTime = ulong.Parse(args[3]);
        PInvoke.GetWindowThreadProcessId(window, out var actualPid);
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, actualPid);
        if (actualPid != expectedPid || process.IsInvalid || !PInvoke.GetProcessTimes(process, out var created, out _, out _, out _))
            throw new InvalidOperationException("Test-owned window process unavailable.");
        var creation = ((ulong)(uint)created.dwHighDateTime << 32) | (uint)created.dwLowDateTime;
        if (creation != expectedTime) throw new InvalidOperationException("Test-owned window identity changed.");
        // WM_GETICON returns borrowed handles; never destroy a window-owned icon.
        var big = new HICON((nint)PInvoke.SendMessage(window, 0x007F, new WPARAM(1), default));
        var little = new HICON((nint)PInvoke.SendMessage(window, 0x007F, new WPARAM(0), default));
        if (big.IsNull || little.IsNull) throw new InvalidOperationException("Window did not expose both native product icons.");
        Directory.CreateDirectory(args[4]);
        Console.WriteLine(JsonConvert.SerializeObject(new { large = Save(big, Path.Combine(args[4], "product-window-large.png")), small = Save(little, Path.Combine(args[4], "product-window-small.png")) }));
        return true;
    }

    private static object Save(HICON handle, string path)
    {
        using var wrapper = Icon.FromHandle((nint)handle);
        using var bitmap = wrapper.ToBitmap();
        var bluePixels = 0; var whitePixels = 0;
        for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.A > 220 && pixel.R < 15 && Math.Abs(pixel.G - 103) < 15 && Math.Abs(pixel.B - 192) < 15) bluePixels++;
            if (pixel.A > 220 && pixel.R > 240 && pixel.G > 240 && pixel.B > 240) whitePixels++;
        }
        bitmap.Save(path, ImageFormat.Png);
        return new { width = bitmap.Width, height = bitmap.Height, bluePixels, whitePixels, transparentCorner = bitmap.GetPixel(0, 0).A == 0 };
    }
}
