using System.Text;
using Microsoft.Win32.SafeHandles;
using Takao.Host.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace Takao.Host.Native;

internal static class StartupRegistry
{
    internal static List<StartupDto> Read(List<Unavailable> warnings, StartupApprovalReader approvals)
    {
        var result = new List<StartupDto>(); var totalBytes = 0;
        var name = new char[16384]; var data = new byte[65536];
        foreach (var (hive, rootName) in new[] { (HKEY.HKEY_CURRENT_USER, "HKCU"), (HKEY.HKEY_LOCAL_MACHINE, "HKLM") })
        foreach (var (view, viewName) in new[] { (REG_SAM_FLAGS.KEY_WOW64_64KEY, "Registry64"), (REG_SAM_FLAGS.KEY_WOW64_32KEY, "Registry32") })
        foreach (var subkey in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" })
        {
            // HKCU Run/RunOnce are shared views; HKLM views remain separate sources.
            if (rootName == "HKCU" && view == REG_SAM_FLAGS.KEY_WOW64_32KEY) continue;
            using var root = new SafeRegistryHandle((nint)hive, false);
            var source = $"{rootName}\\{subkey} ({viewName})";
            var opened = PInvoke.RegOpenKeyEx(root, subkey, 0, REG_SAM_FLAGS.KEY_QUERY_VALUE | view, out var key);
            using (key)
            {
                if (opened == WIN32_ERROR.ERROR_FILE_NOT_FOUND) continue;
                if (opened != WIN32_ERROR.ERROR_SUCCESS) { warnings.Add(NativeSupport.Error((int)opened, $"读取启动注册项失败：{source}")); continue; }
                for (uint index = 0; index < 4096; index++)
                {
                    uint nameLength = (uint)name.Length, dataLength = (uint)data.Length;
                    var status = PInvoke.RegEnumValue(key, index, name, ref nameLength, out var type, data, ref dataLength);
                    if (status == WIN32_ERROR.ERROR_NO_MORE_ITEMS) break;
                    if (status != WIN32_ERROR.ERROR_SUCCESS)
                    {
                        warnings.Add(status == WIN32_ERROR.ERROR_MORE_DATA ? new("capacity", $"启动注册值超过64KiB读取上限：{source}。") : NativeSupport.Error((int)status, $"枚举启动注册项失败：{source}"));
                        if (status == WIN32_ERROR.ERROR_MORE_DATA) continue;
                        break;
                    }
                    if (type is not (1u or 2u)) continue; // REG_SZ / REG_EXPAND_SZ, retain original unexpanded command.
                    if ((dataLength & 1) != 0 || nameLength >= name.Length || dataLength > data.Length) { warnings.Add(new("error", $"启动注册值的Unicode长度无效：{source}。")); continue; }
                    totalBytes += (int)dataLength;
                    if (result.Count >= 4096 || totalBytes > 4 * 1024 * 1024) { warnings.Add(new("capacity", "启动注册项目达到4096项/4MiB总量上限。")); return result; }
                    var valueName = new string(name, 0, (int)nameLength); var command = Encoding.Unicode.GetString(data, 0, (int)dataLength).TrimEnd('\0');
                    var approval = subkey.EndsWith(@"\RunOnce", StringComparison.OrdinalIgnoreCase)
                        ? new StartupApprovalReader.ApprovalState(null, "RunOnce为一次性启动来源，不适用Run的StartupApproved启用/禁用记录；不推断其最终执行状态。")
                        : approvals.Read(rootName == "HKLM", rootName == "HKLM" && view == REG_SAM_FLAGS.KEY_WOW64_32KEY ? "Run32" : "Run", valueName);
                    result.Add(new($"{source}\\{valueName}", valueName, command, source, approval.Enabled, approval.Reason));
                    if (index == 4095) warnings.Add(new("capacity", $"启动注册项达到单键4096项上限：{source}。"));
                }
            }
        }
        return result;
    }
}
