namespace TaskManager.Host.Models;

public sealed record Unavailable(string Code, string Message, int? NativeErrorCode = null);
public sealed record Capability(bool Available, string Source, string? Reason = null);
public sealed record SamplingSettings(int IntervalMs = 1000, bool Paused = false);
public sealed record HistoryPoint(DateTime CapturedAtUtc, double? CpuPercent, double? MemoryPercent, double? NetworkReceiveBytesPerSecond, double? NetworkSendBytesPerSecond);

public sealed class ProcessDto
{
    public required string Identity { get; init; }
    public uint Pid { get; init; }
    public uint ParentPid { get; init; }
    public string? CreationTimeFileTime { get; set; }
    public required string Name { get; init; }
    public string? Path { get; set; }
    public string? Publisher { get; set; }
    public uint? SessionId { get; set; }
    public string? UserName { get; set; }
    public uint ThreadCount { get; init; }
    public string? Priority { get; set; }
    public double? CpuPercent { get; set; }
    public long? WorkingSetBytes { get; set; }
    public double? IoReadBytesPerSecond { get; set; }
    public double? IoWriteBytesPerSecond { get; set; }
    public string? TotalCpuTime100ns { get; set; }
    public string? IoReadBytes { get; set; }
    public string? IoWriteBytes { get; set; }
    public double? NetworkBytesPerSecond => null;
    public double? GpuPercent => null;
    public bool CanTerminate { get; set; }
    public string State { get; set; } = "running";
    public Dictionary<string, Unavailable> Unavailable { get; init; } = new();
}

public sealed class SystemDto
{
    public double? CpuPercent { get; set; }
    public double? MemoryPercent { get; set; }
    public long? TotalPhysicalBytes { get; set; }
    public long? AvailablePhysicalBytes { get; set; }
    public long? UsedPhysicalBytes { get; set; }
    public double? NetworkReceiveBytesPerSecond { get; set; }
    public double? NetworkSendBytesPerSecond { get; set; }
    public int NetworkInterfaceCount { get; set; }
    public int ProcessCount { get; set; }
    public int LogicalProcessorCount { get; init; } = Environment.ProcessorCount;
    public double UptimeSeconds { get; init; }
    public Dictionary<string, Unavailable> Unavailable { get; init; } = new();
}
public sealed record ServiceDto(string Name, string DisplayName, string State, uint Pid);
public sealed record StartupDto(string Id, string Name, string Command, string Source, string? Enabled, string EnabledReason);
public sealed class UserDto
{
    public uint SessionId { get; init; }
    public string? UserName { get; init; }
    public required string StationName { get; init; }
    public required string State { get; init; }
    public int ProcessCount { get; set; }
    public double? CpuPercent { get; set; }
    public long? WorkingSetBytes { get; set; }
    public int UnreadableProcessCount { get; set; }
    public Dictionary<string, Unavailable> Unavailable { get; init; } = new();
}
public sealed record ProcessHistoryDto(string Identity, uint Pid, string Name, DateTime FirstObservedAtUtc, DateTime LastObservedAtUtc, bool IsRunning, string? ObservedCpuTime100ns, string? ObservedIoReadBytes, string? ObservedIoWriteBytes);
public sealed record SnapshotDto(long SampleId, DateTime CapturedAtUtc, DateTime SessionStartedAtUtc, int IntervalMs, bool Paused,
    SystemDto System, IReadOnlyList<ProcessDto> Processes, IReadOnlyList<ServiceDto> Services, IReadOnlyList<StartupDto> StartupItems,
    IReadOnlyList<UserDto> Users, IReadOnlyList<ProcessHistoryDto> History, IReadOnlyList<HistoryPoint> PerformanceHistory,
    IReadOnlyDictionary<string, Capability> Capabilities, IReadOnlyList<Unavailable> Warnings, DateTime? SlowDataCapturedAtUtc);
public sealed record StartRequest(string Executable, string? Arguments);
public sealed record TerminateRequest(string CreationTimeFileTime);
public sealed record SettingsRequest(int IntervalMs, bool Paused);
public sealed record ProcessActionResult(uint Pid, string? CreationTimeFileTime, string? Identity, string Message);
public sealed record ErrorBody(string Code, string Message, int? NativeErrorCode = null);
public sealed record ErrorEnvelope(ErrorBody Error);
