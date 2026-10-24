using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TaskManager.Host.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.IpHelper;
using Windows.Win32.Security;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.ProcessStatus;
using Windows.Win32.System.SystemInformation;
using Windows.Win32.System.Threading;

namespace TaskManager.Host.Native;

internal sealed record ProcessCounters(ulong? Cpu, ulong? Read, ulong? Write, long Tick);
internal sealed record ProcessMetadata(string? Path, string? Publisher, string? UserName, Dictionary<string, Unavailable> Unavailable)
{
    public string? ApplicationUserModelId { get; init; }
}
internal sealed record RawCollection(SystemDto System, List<ProcessDto> Processes, Dictionary<string, ProcessCounters> Counters, IReadOnlyList<NetworkAdapterDto> NetworkAdapters);

internal sealed class Win32Collector : IDisposable
{
    private readonly ProcessMetadataCache _metadata = new();
    private Dictionary<string, ProcessCounters> _previous = new();
    private (ulong Idle, ulong Total)? _systemPrevious;
    private readonly NetworkCollector _network = new();

    internal static unsafe List<ProcessDto> EnumerateBaseProcesses()
    {
        using var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "无法枚举 Windows 进程");
        var entry = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
        var result = new List<ProcessDto>();
        if (!PInvoke.Process32FirstW(snapshot, ref entry)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "无法读取进程快照");
        do
        {
            var name = entry.szExeFile.ToString();
            result.Add(new ProcessDto { Identity = $"{entry.th32ProcessID}:unknown", Pid = entry.th32ProcessID,
                ParentPid = entry.th32ParentProcessID, Name = name, ThreadCount = entry.cntThreads });
        } while (PInvoke.Process32NextW(snapshot, ref entry));
        if (Marshal.GetLastPInvokeError() is not 18) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "进程快照未能完整读取");
        return result;
    }

    internal RawCollection Collect()
    {
        var processes = EnumerateBaseProcesses();
        var tick = Stopwatch.GetTimestamp();
        var counters = new Dictionary<string, ProcessCounters>();
        for (var i = 0; i < processes.Count; i++) processes[i] = ReadProcess(processes[i], tick, counters);
        var protectedIds = ProcessProtection.Family(processes);
        foreach (var process in processes)
        {
            if (protectedIds.Contains(process.Pid))
            {
                process.CanTerminate = false;
                process.Unavailable["terminate"] = new("protected", "系统关键进程或本应用进程族受保护。");
            }
        }
        _previous = counters;
        var identities = counters.Keys.ToHashSet();
        _metadata.Retain(identities);
        var system = ReadSystem(tick);
        system.ProcessCount = processes.Count;
        return new(system, processes, counters, _network.Collect(system, Stopwatch.GetTimestamp()));
    }

    private ProcessDto ReadProcess(ProcessDto basic, long tick, Dictionary<string, ProcessCounters> counters)
    {
        using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, basic.Pid);
        var openError = handle.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
        var unavailable = basic.Unavailable;
        if (PInvoke.ProcessIdToSessionId(basic.Pid, out var sessionId)) basic.SessionId = sessionId;
        else unavailable["sessionId"] = NativeSupport.LastError("读取会话失败");
        if (handle.IsInvalid)
        {
            var reason = NativeSupport.Error(openError, "打开进程失败");
            foreach (var field in new[] { "creationTimeFileTime", "cpuPercent", "totalCpuTime100ns", "workingSetBytes", "privateBytes", "handleCount", "startedAtUtc", "uptimeSeconds", "path", "publisher", "userName", "priority", "ioReadBytes", "ioWriteBytes", "ioReadBytesPerSecond", "ioWriteBytesPerSecond", "terminate" }) unavailable[field] = reason;
            basic.State = "restricted";
            return basic;
        }
        ulong? cpu = null;
        if (PInvoke.GetProcessTimes(handle, out var creation, out _, out var kernel, out var user))
        {
            basic.CreationTimeFileTime = NativeSupport.FileTime(creation).ToString(CultureInfo.InvariantCulture);
            cpu = NativeSupport.FileTime(kernel) + NativeSupport.FileTime(user);
            basic.TotalCpuTime100ns = cpu.Value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            var error = NativeSupport.LastError("读取进程时间失败");
            unavailable["creationTimeFileTime"] = unavailable["totalCpuTime100ns"] = unavailable["cpuPercent"] = error;
        }
        var identity = $"{basic.Pid}:{basic.CreationTimeFileTime ?? "unknown"}";
        // A new object ensures identities never change in a snapshot already published to the UI.
        var row = new ProcessDto { Identity = identity, Pid = basic.Pid, ParentPid = basic.ParentPid, Name = basic.Name,
            ThreadCount = basic.ThreadCount, CreationTimeFileTime = basic.CreationTimeFileTime, TotalCpuTime100ns = basic.TotalCpuTime100ns,
            SessionId = basic.SessionId, Unavailable = unavailable };
        var metadata = _metadata.Read(handle, basic.Pid, identity);
        row.Path = metadata.Path;
        row.ApplicationUserModelId = metadata.ApplicationUserModelId;
        row.Publisher = metadata.Publisher;
        row.UserName = metadata.UserName;
        foreach (var missing in metadata.Unavailable) unavailable[missing.Key] = missing.Value;
        var priority = PInvoke.GetPriorityClass(handle);
        if (priority != 0) row.Priority = priority switch { 0x20 => "正常", 0x40 => "空闲", 0x80 => "高", 0x100 => "实时", 0x4000 => "低于正常", 0x8000 => "高于正常", _ => priority.ToString(CultureInfo.InvariantCulture) };
        else unavailable["priority"] = NativeSupport.LastError("读取优先级失败");

        ReadMemory(handle, row);
        if (PInvoke.GetProcessHandleCount(handle, out var handleCount)) row.HandleCount = handleCount;
        else unavailable["handleCount"] = NativeSupport.LastError("读取进程句柄数失败");
        if (row.CreationTimeFileTime is { } time && long.TryParse(time, out var rawTime))
        {
            row.StartedAtUtc = DateTime.FromFileTimeUtc(rawTime);
            row.UptimeSeconds = Math.Max(0, (DateTime.UtcNow - row.StartedAtUtc.Value).TotalSeconds);
        }
        ulong? read = null, write = null;
        if (PInvoke.GetProcessIoCounters(handle, out var io))
        {
            read = io.ReadTransferCount;
            write = io.WriteTransferCount;
            row.IoReadBytes = read.Value.ToString(CultureInfo.InvariantCulture);
            row.IoWriteBytes = write.Value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            var error = NativeSupport.LastError("读取进程 I/O 失败");
            foreach (var key in new[] { "ioReadBytes", "ioWriteBytes", "ioReadBytesPerSecond", "ioWriteBytesPerSecond" }) unavailable[key] = error;
        }
        var current = new ProcessCounters(cpu, read, write, tick);
        if (row.CreationTimeFileTime is not null)
        {
            counters[identity] = current;
            _previous.TryGetValue(identity, out var previous);
            var seconds = previous is null ? 0 : Stopwatch.GetElapsedTime(previous.Tick, tick).TotalSeconds;
            row.CpuPercent = Rate(cpu, previous?.Cpu, seconds, 100.0 / (10_000_000 * Environment.ProcessorCount), "cpuPercent", unavailable);
            row.IoReadBytesPerSecond = Rate(read, previous?.Read, seconds, 1, "ioReadBytesPerSecond", unavailable);
            row.IoWriteBytesPerSecond = Rate(write, previous?.Write, seconds, 1, "ioWriteBytesPerSecond", unavailable);
        }
        else
        {
            unavailable["terminate"] = new("notCollected", "创建时间不可读，无法安全确认进程身份。");
            foreach (var field in new[] { "cpuPercent", "ioReadBytesPerSecond", "ioWriteBytesPerSecond" })
                unavailable.TryAdd(field, new("notCollected", "创建时间不可读，无法安全关联相邻进程身份以计算增量。"));
        }
        if (row.CreationTimeFileTime is not null)
        {
            if (!PInvoke.IsProcessCritical(handle, out var critical)) unavailable["terminate"] = NativeSupport.LastError("无法确认进程关键状态，已保守拒绝结束");
            else if (critical) unavailable["terminate"] = new("protected", "Windows 将该进程标记为关键进程。");
            else row.CanTerminate = true;
        }
        return row;
    }

    private static double? Rate(ulong? current, ulong? previous, double seconds, double multiplier, string field, Dictionary<string, Unavailable> unavailable)
    {
        if (!current.HasValue) return null;
        if (!previous.HasValue || seconds <= 0) { unavailable[field] = NativeSupport.WarmingUp; return null; }
        if (current < previous) { unavailable[field] = NativeSupport.CounterReset; return null; }
        return (current.Value - previous.Value) / seconds * multiplier;
    }

    internal static unsafe void ReadMemory(SafeFileHandle handle, ProcessDto row)
    {
        // Windows 10/11 accept QUERY_LIMITED_INFORMATION. VM_READ was an XP/2003 requirement.
        var memory = new PROCESS_MEMORY_COUNTERS_EX { cb = (uint)sizeof(PROCESS_MEMORY_COUNTERS_EX) };
        if (PInvoke.K32GetProcessMemoryInfo(new HANDLE(handle.DangerousGetHandle()), (PROCESS_MEMORY_COUNTERS*)&memory, (uint)sizeof(PROCESS_MEMORY_COUNTERS_EX)))
        {
            row.WorkingSetBytes = checked((long)memory.WorkingSetSize);
            row.PrivateBytes = checked((long)memory.PrivateUsage);
        }
        else row.Unavailable["workingSetBytes"] = row.Unavailable["privateBytes"] = NativeSupport.LastError("读取工作集/专用提交失败");
        GC.KeepAlive(handle);
    }
    public void Dispose() => _metadata.Dispose();

    private unsafe SystemDto ReadSystem(long tick)
    {
        var system = new SystemDto { UptimeSeconds = PInvoke.GetTickCount64() / 1000.0 };
        var memory = new MEMORYSTATUSEX { dwLength = (uint)sizeof(MEMORYSTATUSEX) };
        if (PInvoke.GlobalMemoryStatusEx(ref memory))
        {
            system.TotalPhysicalBytes = checked((long)memory.ullTotalPhys);
            system.AvailablePhysicalBytes = checked((long)memory.ullAvailPhys);
            system.UsedPhysicalBytes = system.TotalPhysicalBytes - system.AvailablePhysicalBytes;
            system.MemoryPercent = 100.0 * system.UsedPhysicalBytes / system.TotalPhysicalBytes;
        }
        else foreach (var field in new[] { "totalPhysicalBytes", "availablePhysicalBytes", "usedPhysicalBytes", "memoryPercent" }) system.Unavailable[field] = NativeSupport.LastError("读取物理内存失败");
        if (PInvoke.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            var idle = NativeSupport.FileTime(idleTime);
            var total = NativeSupport.FileTime(kernelTime) + NativeSupport.FileTime(userTime);
            if (_systemPrevious is { } previous && total > previous.Total && idle >= previous.Idle && idle - previous.Idle <= total - previous.Total)
                system.CpuPercent = 100.0 * ((total - previous.Total) - (idle - previous.Idle)) / (total - previous.Total);
            else system.Unavailable["cpuPercent"] = NativeSupport.WarmingUp;
            _systemPrevious = (idle, total);
        }
        else system.Unavailable["cpuPercent"] = NativeSupport.LastError("读取系统 CPU 失败");
        var performance = new PERFORMANCE_INFORMATION { cb = (uint)sizeof(PERFORMANCE_INFORMATION) };
        if (PInvoke.GetPerformanceInfo(ref performance, (uint)sizeof(PERFORMANCE_INFORMATION)))
        {
            system.CommitBytes = checked((long)(performance.CommitTotal * performance.PageSize));
            system.CommitLimitBytes = checked((long)(performance.CommitLimit * performance.PageSize));
            system.CacheBytes = checked((long)(performance.SystemCache * performance.PageSize));
            system.ThreadCount = performance.ThreadCount;
            system.HandleCount = performance.HandleCount;
        }
        else foreach (var field in new[] { "commitBytes", "commitLimitBytes", "cacheBytes", "threadCount", "handleCount" }) system.Unavailable[field] = NativeSupport.LastError("读取系统提交/缓存/句柄失败");
        return system;
    }
}
