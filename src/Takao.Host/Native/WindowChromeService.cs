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
            // Transient Acrylic is the Fluent material intended for utility
            // surfaces.  Unlike the opaque theme clear, it keeps the desktop
            // backdrop visible behind our translucent page layers.
            var backdrop = BitConverter.GetBytes((int)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW);
            var result = PInvoke.DwmSetWindowAttribute(
                (HWND)window.Handle,
                DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE,
                backdrop);
            if (!result.Succeeded)
            {
                // Mica is available on a wider range of Windows 11 builds;
                // retain a backdrop when Acrylic is rejected by the shell.
                backdrop = BitConverter.GetBytes((int)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW);
                result = PInvoke.DwmSetWindowAttribute(
                    (HWND)window.Handle,
                    DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE,
                    backdrop);
            }
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
