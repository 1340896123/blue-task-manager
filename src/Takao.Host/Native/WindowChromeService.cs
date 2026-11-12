using Aprillz.MewUI;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Takao.Host.Native;

/// <summary>
/// Applies the optional Windows 11 system backdrop to the custom MewUI title
/// bar. DWM is an enhancement only: transparent MewUI surfaces remain usable
/// when the attribute is unavailable (older Windows, remote sessions or a
/// composition policy that rejects the request).
/// </summary>
internal static class WindowChromeService
{
    public static bool Apply(Window window)
    {
        if (window.Handle == IntPtr.Zero) return false;

        try
        {
            var backdrop = BitConverter.GetBytes((int)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW);
            var result = PInvoke.DwmSetWindowAttribute(
                (HWND)window.Handle,
                DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE,
                backdrop);
            return result.Succeeded;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
