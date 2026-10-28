using System.Globalization;
using ElectronNET.API;
using Takao.Host.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Takao.Host.Native;

internal static class DesktopTopmost
{
    private static async Task<HWND> HandleAsync(BrowserWindow window)
    {
        // The official framework returns the native HWND as a hexadecimal string.
        // No renderer-provided HWND or PID is accepted by this application.
        var text = await window.GetNativeWindowHandle();
        var value = text.StartsWith('-') ? -long.Parse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : long.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var handle = new HWND((nint)value);
        if (!PInvoke.IsWindow(handle)) throw new ApiException(409, "windowUnavailable", "桌面窗口已经关闭。");
        return handle;
    }
    internal static async Task<bool> ReadAsync(BrowserWindow window)
    {
        var handle = await HandleAsync(window);
        return ((long)PInvoke.GetWindowLongPtr(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & 0x8) != 0;
    }
    internal static async Task SetAsync(BrowserWindow window, bool enabled)
    {
        var handle = await HandleAsync(window);
        if (!PInvoke.SetWindowPos(handle, new HWND(enabled ? (nint)(-1) : (nint)(-2)), 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE))
            throw new ApiException(400, "windowStateNotApplied", "Windows未能改变窗口置顶状态。", System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        if (await ReadAsync(window) != enabled) throw new ApiException(409, "windowStateNotApplied", "窗口置顶状态未能读回确认，请重试。");
    }
}
