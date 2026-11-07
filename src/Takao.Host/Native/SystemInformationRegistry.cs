using System.Text;
using Microsoft.Win32.SafeHandles;
using Takao.Host.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace Takao.Host.Native;

internal static class SystemInformationRegistry
{
    internal static byte[]? Bytes(string path, string name, uint expectedType, out Unavailable? error)
    {
        error = null;
        using var root = new SafeRegistryHandle((nint)HKEY.HKEY_LOCAL_MACHINE, false);
        var opened = PInvoke.RegOpenKeyEx(root, path, 0, REG_SAM_FLAGS.KEY_QUERY_VALUE | REG_SAM_FLAGS.KEY_WOW64_64KEY, out var key);
        using (key)
        {
            if (opened != WIN32_ERROR.ERROR_SUCCESS) { error = NativeSupport.Error((int)opened, "读取系统注册表信息失败"); return null; }
            var bytes = new byte[16 * 1024]; uint length = (uint)bytes.Length;
            var result = PInvoke.RegQueryValueEx(key, name, out var type, bytes, ref length);
            if (result != WIN32_ERROR.ERROR_SUCCESS) { error = NativeSupport.Error((int)result, "读取系统注册表字段失败"); return null; }
            if ((uint)type != expectedType || length > bytes.Length) { error = new("invalidData", "系统注册表字段类型或长度无效。"); return null; }
            return bytes.AsSpan(0, (int)length).ToArray();
        }
    }
    internal static string? String(string path, string name, out Unavailable? error)
    {
        var bytes = Bytes(path, name, 1, out error);
        if (bytes is null) return null;
        if (bytes.Length < 2 || (bytes.Length & 1) != 0 || bytes[^1] != 0 || bytes[^2] != 0)
        { error = new("invalidData", "系统注册表文本没有有效Unicode终止符。"); return null; }
        var value = SystemInformationFirmware.Clean(Encoding.Unicode.GetString(bytes).TrimEnd('\0'));
        if (value is null) error = SystemInformationFirmware.Missing("系统注册表未提供有效文本。");
        return value;
    }
}
