using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Takao.Host.Native;

internal static class ApplicationIdentity
{
    internal static string? Read(SafeFileHandle handle)
    {
        // APPMODEL_ERROR_NO_APPLICATION means an ordinary desktop process, not a failure.
        uint length = 0;
        if (PInvoke.GetApplicationUserModelId(handle, ref length, null) != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER || length is 0 or > 1024) return null;
        var value = new char[length];
        return PInvoke.GetApplicationUserModelId(handle, ref length, value) == 0 ? new string(value).TrimEnd('\0') : null;
    }
}
