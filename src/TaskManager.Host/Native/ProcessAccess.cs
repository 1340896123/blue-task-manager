using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TaskManager.Host.Services;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace TaskManager.Host.Native;

internal static class ProcessAccess
{
    internal static SafeFileHandle Open(uint pid, string creation, PROCESS_ACCESS_RIGHTS extra = 0, bool write = false, IReadOnlySet<uint>? protectedIds = null)
    {
        Parse(creation);
        if (write && (protectedIds ?? ProcessProtection.Family(Win32Collector.EnumerateBaseProcesses())).Contains(pid)) throw new ApiException(403, "protectedProcess", "系统关键进程或本应用进程族受保护。");
        var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION | extra, false, pid);
        try
        {
            if (handle.IsInvalid) Throw("无法打开目标进程");
            Validate(handle, creation);
            if (write)
            {
                if (!PInvoke.IsProcessCritical(handle, out var critical)) Throw("无法确认进程关键状态，已保守拒绝操作", true);
                if (critical) throw new ApiException(403, "criticalProcess", "Windows 将目标标记为关键进程，禁止修改。");
            }
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
    internal static ulong Parse(string creation)
    {
        if (!ulong.TryParse(creation, NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected == 0 || expected > long.MaxValue) throw new ApiException(400, "identityRequired", "操作必须提供有效的创建 FILETIME 身份。");
        return expected;
    }
    internal static void Validate(SafeFileHandle handle, string creation)
    {
        if (!PInvoke.GetProcessTimes(handle, out var actual, out var exit, out _, out _)) Throw("无法确认目标身份");
        if (NativeSupport.FileTime(actual) != Parse(creation)) throw new ApiException(409, "identityChanged", "目标创建时间已改变；该 PID 已被另一进程使用，未执行操作。");
        if (NativeSupport.FileTime(exit) != 0) throw new ApiException(404, "processExited", "目标进程已退出。");
    }
    internal static string Path(SafeFileHandle handle)
    {
        var buffer = new char[32768]; uint length = (uint)buffer.Length;
        if (!PInvoke.QueryFullProcessImageName(handle, 0, buffer, ref length)) Throw("读取目标文件路径失败");
        return new string(buffer, 0, (int)length);
    }
    internal static void Throw(string action, bool forbidden = false)
    {
        var code = Marshal.GetLastPInvokeError(); var error = NativeSupport.Error(code, action);
        throw new ApiException(forbidden || code == 5 ? 403 : code is 87 or 1168 ? 404 : 400, error.Code, error.Message, code);
    }
    internal static string PriorityName(uint priority) => priority switch { 0x20 => "normal", 0x40 => "idle", 0x80 => "high", 0x100 => "realtime", 0x4000 => "belowNormal", 0x8000 => "aboveNormal", _ => "unknown" };
    internal static string PriorityLabel(uint priority) => priority switch { 0x20 => "正常", 0x40 => "空闲", 0x80 => "高", 0x100 => "实时", 0x4000 => "低于正常", 0x8000 => "高于正常", _ => priority.ToString(CultureInfo.InvariantCulture) };
}
