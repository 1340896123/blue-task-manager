using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using TaskManager.Host.Models;

namespace TaskManager.Host.Native;

internal static class NativeSupport
{
    internal static ulong FileTime(FILETIME value) => ((ulong)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;
    internal static Unavailable LastError(string action) => Error(Marshal.GetLastPInvokeError(), action);
    internal static Unavailable Error(int code, string action) => new(code switch { 5 => "accessDenied", 87 or 1168 => "exited", _ => "error" }, $"{action}：{new Win32Exception(code).Message}", code);
    internal static readonly Unavailable WarmingUp = new("warmingUp", "等待下一次有效采样以计算增量。");
    internal static readonly Unavailable CounterReset = new("warmingUp", "计数器发生变化，等待下一次有效采样。");
    internal static readonly Unavailable NotCollected = new("notCollected", "本版未接入逐进程采集，不能由进程 I/O 推导。");
}
