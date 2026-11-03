using Takao.Host.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Takao.Host.Native;

internal static class DesktopTopmost
{
    internal static bool Read(HWND window)
    {
        if (!PInvoke.IsWindow(window)) throw new ApiException(409, "windowUnavailable", "Qt 桌面窗口已经关闭。");
        return ((long)PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & 0x8) != 0;
    }

    internal static void Set(HWND window, bool enabled)
    {
        if (!PInvoke.SetWindowPos(window, new HWND(enabled ? (nint)(-1) : (nint)(-2)), 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE))
            throw new ApiException(400, "windowStateNotApplied", "Windows未能改变Qt窗口置顶状态。", System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        if (Read(window) != enabled) throw new ApiException(409, "windowStateNotApplied", "Qt窗口置顶状态未能读回确认，请重试。");
    }
}
