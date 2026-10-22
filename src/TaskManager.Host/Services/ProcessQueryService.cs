using System.Globalization;
using TaskManager.Host.Models;
using TaskManager.Host.Native;
using Windows.Win32;

namespace TaskManager.Host.Services;

public sealed class ProcessQueryService(SnapshotService snapshots)
{
    public ProcessDetails Details(uint pid, string creation)
    {
        using var handle = ProcessAccess.Open(pid, creation);
        var processes = Win32Collector.EnumerateBaseProcesses();
        var basic = processes.FirstOrDefault(p => p.Pid == pid) ?? throw new ApiException(404, "processExited", "目标进程已退出。");
        var current = snapshots.Current;
        var previous = current?.Processes.FirstOrDefault(p => p.Identity == $"{pid}:{creation}");
        var row = new ProcessDto { Identity = $"{pid}:{creation}", Pid = pid, ParentPid = basic.ParentPid, CreationTimeFileTime = creation, Name = basic.Name, ThreadCount = basic.ThreadCount,
            Publisher = previous?.Publisher, UserName = previous?.UserName, SessionId = previous?.SessionId, CanTerminate = previous?.CanTerminate ?? false,
            CpuPercent = previous?.CpuPercent, IoReadBytesPerSecond = previous?.IoReadBytesPerSecond, IoWriteBytesPerSecond = previous?.IoWriteBytesPerSecond };
        row.Path = ProcessAccess.Path(handle);
        if (PInvoke.ProcessIdToSessionId(pid, out var session)) row.SessionId = session; else row.Unavailable["sessionId"] = NativeSupport.LastError("读取会话失败");
        if (PInvoke.GetProcessTimes(handle, out var started, out _, out var kernel, out var user))
        {
            row.StartedAtUtc = DateTime.FromFileTimeUtc((long)NativeSupport.FileTime(started));
            row.UptimeSeconds = Math.Max(0, (DateTime.UtcNow - row.StartedAtUtc.Value).TotalSeconds);
            row.TotalCpuTime100ns = (NativeSupport.FileTime(kernel) + NativeSupport.FileTime(user)).ToString(CultureInfo.InvariantCulture);
        }
        else row.Unavailable["startedAtUtc"] = row.Unavailable["uptimeSeconds"] = NativeSupport.LastError("读取进程时间失败");
        Win32Collector.ReadMemory(handle, row);
        if (PInvoke.GetProcessHandleCount(handle, out var handles)) row.HandleCount = handles; else row.Unavailable["handleCount"] = NativeSupport.LastError("读取句柄数失败");
        var priority = PInvoke.GetPriorityClass(handle);
        if (priority != 0) row.Priority = ProcessAccess.PriorityLabel(priority); else row.Unavailable["priority"] = NativeSupport.LastError("读取优先级失败");
        if (PInvoke.GetProcessIoCounters(handle, out var io)) { row.IoReadBytes = io.ReadTransferCount.ToString(CultureInfo.InvariantCulture); row.IoWriteBytes = io.WriteTransferCount.ToString(CultureInfo.InvariantCulture); }
        else row.Unavailable["ioReadBytes"] = row.Unavailable["ioWriteBytes"] = NativeSupport.LastError("读取I/O计数失败");
        foreach (var field in new[] { "publisher", "userName", "cpuPercent", "ioReadBytesPerSecond", "ioWriteBytesPerSecond", "terminate" })
            if (previous?.Unavailable.TryGetValue(field, out var reason) == true) row.Unavailable[field] = reason;
        if (row.Publisher is null) row.Unavailable.TryAdd("publisher", new("loading", "文件版本信息正在后台读取。"));
        if (row.UserName is null) row.Unavailable.TryAdd("userName", new("loading", "账户信息正在后台读取。"));
        if (previous is null) foreach (var field in new[] { "cpuPercent", "ioReadBytesPerSecond", "ioWriteBytesPerSecond" }) row.Unavailable[field] = NativeSupport.WarmingUp;
        row.Unavailable["networkBytesPerSecond"] = NativeSupport.NotCollected;
        row.Unavailable["gpuPercent"] = NativeSupport.NotCollected;
        if (ProcessProtection.Family(processes).Contains(pid)) { row.CanTerminate = false; row.Unavailable["terminate"] = new("protected", "系统关键进程或本应用进程族受保护。"); }
        else if (!PInvoke.IsProcessCritical(handle, out var critical)) { row.CanTerminate = false; row.Unavailable["terminate"] = NativeSupport.LastError("无法确认进程关键状态"); }
        else if (critical) { row.CanTerminate = false; row.Unavailable["terminate"] = new("protected", "Windows关键进程受保护。"); }
        else { row.CanTerminate = true; row.Unavailable.Remove("terminate"); }
        ProcessAccess.Validate(handle, creation);
        var errors = new Dictionary<string, Unavailable>(row.Unavailable);
        if (current is null) errors["relatedServices"] = NativeSupport.WarmingUp;
        return new(row, DateTime.UtcNow, row.StartedAtUtc, row.UptimeSeconds, row.HandleCount, row.PrivateBytes, current?.Services.Where(s => s.Pid == pid && s.Pid != 0).ToArray() ?? [], errors, current?.CapturedAtUtc, current?.SlowDataCapturedAtUtc);
    }
}
