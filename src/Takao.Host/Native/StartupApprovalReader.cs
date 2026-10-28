using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace Takao.Host.Native;

internal sealed class StartupApprovalReader : IDisposable
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    // Only the six Shell approval keys are cached, for one slow-source read.
    private readonly Dictionary<(bool Machine, string Group), ApprovalKey> _keys = [];

    internal ApprovalState Read(bool machine, string group, string valueName)
    {
        var own = ReadValue(machine, group, valueName);
        if (own.Enabled is null) return new(null, own.Reason);

        // Windows versions/tools can leave records in both user and machine scopes.
        // Until their precedence is verified, do not report enabled over a conflicting
        // user disable record (including when the primary record is absent).
        var other = ReadValue(!machine, group, valueName);
        if (other.Enabled is null)
            return new(null, $"同名启动项的另一范围状态无法确定，不能核验是否冲突。{own.Reason} {other.Reason}");
        if (other.Present && other.Enabled != own.Enabled)
            return new(null, $"当前用户与计算机范围的同名启动批准状态冲突，未核验其优先级。{own.Reason} {other.Reason}");
        return new(own.Enabled, own.Reason);
    }

    private ApprovalValue ReadValue(bool machine, string group, string valueName)
    {
        var key = Open(machine, group);
        var source = $"{key.Source}\\{valueName}";
        if (key.Status is WIN32_ERROR.ERROR_FILE_NOT_FOUND or WIN32_ERROR.ERROR_PATH_NOT_FOUND)
            return Missing(source);
        if (key.Status != WIN32_ERROR.ERROR_SUCCESS)
            return Failure(source, key.Status, "打开启动批准状态失败");

        Span<byte> data = stackalloc byte[12];
        uint length = (uint)data.Length;
        var status = PInvoke.RegQueryValueEx(key.Handle, valueName, out var type, data, ref length);
        if (status is WIN32_ERROR.ERROR_FILE_NOT_FOUND or WIN32_ERROR.ERROR_PATH_NOT_FOUND)
            return Missing(source);
        if (status == WIN32_ERROR.ERROR_MORE_DATA)
            return new(true, null, $"启动批准记录超出已识别的12字节格式（Windows返回{length}字节）；未扩大缓冲或推断状态。来源：{source}。");
        if (status != WIN32_ERROR.ERROR_SUCCESS)
            return Failure(source, status, "读取启动批准状态失败");
        if (type != REG_VALUE_TYPE.REG_BINARY || length != 12)
            return new(true, null, $"启动批准记录类型或长度无法识别（类型{(uint)type}，{length}字节，预期REG_BINARY/12字节）。来源：{source}。");

        var state = BinaryPrimitives.ReadUInt32LittleEndian(data);
        return state switch
        {
            2 => new(true, true, $"Windows启动批准记录为已启用（状态2）；不代表程序执行成功。来源：{source}。"),
            3 => new(true, false, $"Windows启动批准记录为已禁用（状态3）。来源：{source}。"),
            _ => new(true, null, $"无法识别的Windows启动批准状态{state}；未按奇偶推断。来源：{source}。")
        };
    }

    private ApprovalKey Open(bool machine, string group)
    {
        var identity = (machine, group);
        if (_keys.TryGetValue(identity, out var key)) return key;
        using var root = new SafeRegistryHandle((nint)(machine ? HKEY.HKEY_LOCAL_MACHINE : HKEY.HKEY_CURRENT_USER), false);
        var path = $"{KeyPath}\\{group}";
        // Run32 records live in the native Shell view, not Wow6432Node.
        var status = PInvoke.RegOpenKeyEx(root, path, 0, REG_SAM_FLAGS.KEY_QUERY_VALUE | REG_SAM_FLAGS.KEY_WOW64_64KEY, out var handle);
        key = new(handle, status, $"{(machine ? "HKLM" : "HKCU")}\\{path} (Registry64)");
        _keys.Add(identity, key);
        return key;
    }

    private static ApprovalValue Missing(string source) => new(false, true,
        $"未设置Windows禁用记录，按默认启用；不代表程序执行成功。来源：{source}（键或值不存在）。");

    private static ApprovalValue Failure(string source, WIN32_ERROR status, string action) => new(true, null,
        $"{NativeSupport.Error((int)status, action).Message}（Windows错误{(uint)status}）。来源：{source}。");

    public void Dispose()
    {
        foreach (var key in _keys.Values) key.Handle.Dispose();
        _keys.Clear();
    }

    internal sealed record ApprovalState(bool? Enabled, string Reason);
    private sealed record ApprovalValue(bool Present, bool? Enabled, string Reason);
    private sealed record ApprovalKey(SafeRegistryHandle Handle, WIN32_ERROR Status, string Source);
}
