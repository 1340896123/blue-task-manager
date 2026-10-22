using System.Runtime.InteropServices;
using TaskManager.Host.Models;
using Windows.Win32;

namespace TaskManager.Host.Native;

internal static class VersionMetadata
{
    // File version strings are metadata, not verified digital signatures. Follow
    // the file's actual Translation table; never guess a language/codepage.
    internal static unsafe (string? Publisher, Unavailable? Error) Company(string path)
    {
        var size = PInvoke.GetFileVersionInfoSize(path, out _);
        if (size == 0) return (null, Marshal.GetLastPInvokeError() is var error && error != 0 ? NativeSupport.Error(error, "文件版本信息不可读") : new("notCollected", "文件未提供版本信息；该字段不是签名验证结果。"));
        if (size > 4 * 1024 * 1024) return (null, new("capacity", "文件版本信息超过读取上限。"));
        var data = new byte[size];
        fixed (byte* buffer = data)
        {
            if (!PInvoke.GetFileVersionInfo(path, data)) return (null, NativeSupport.LastError("文件版本信息读取失败"));
            if (!PInvoke.VerQueryValue(buffer, "\\VarFileInfo\\Translation", out var translations, out var bytes) || bytes < 4)
                return (null, new("notCollected", "文件未提供版本语言表；该字段不是签名验证结果。"));
            // Translation consists of LANGID/codepage WORD pairs.
            if ((byte*)translations < buffer || (byte*)translations > buffer + size || bytes > (ulong)(buffer + size - (byte*)translations))
                return (null, new("error", "文件版本语言表超出实际资源边界。"));
            var pairs = (ushort*)translations;
            for (var i = 0; i < Math.Min(bytes / 4, 256); i++)
            {
                var key = $"\\StringFileInfo\\{pairs[i * 2]:x4}{pairs[i * 2 + 1]:x4}\\CompanyName";
                if (!PInvoke.VerQueryValue(buffer, key, out var value, out var characters) || characters == 0 || characters > size / 2) continue;
                if ((byte*)value < buffer || (byte*)value > buffer + size || (ulong)characters * 2 > (ulong)(buffer + size - (byte*)value)) continue;
                var company = Marshal.PtrToStringUni((nint)value, (int)characters)?.TrimEnd('\0').Trim();
                if (!string.IsNullOrWhiteSpace(company)) return (company, null);
            }
        }
        return (null, new("notCollected", "文件未提供 CompanyName；该字段不是签名验证结果。"));
    }
}
