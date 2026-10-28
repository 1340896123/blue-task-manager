namespace Takao.Host.Models;

public sealed record Unavailable(string Code, string Message, int? NativeErrorCode = null);
public sealed record Capability(bool Available, string Source, string? Reason = null);
public sealed record SamplingSettings(int IntervalMs = 1000, bool Paused = false);
public sealed record ResourceHistoryPoint(string Id, double? Percent, double? ReadBytesPerSecond, double? WriteBytesPerSecond);
public sealed record LogicalProcessorHistoryPoint(string Id, double? Percent);
public sealed record LogicalProcessorDto(string Id, uint Group, uint Index, double? Percent, IReadOnlyDictionary<string, Unavailable> Unavailable);
public sealed record HistoryPoint(DateTime CapturedAtUtc, double? CpuPercent, double? MemoryPercent, double? NetworkReceiveBytesPerSecond, double? NetworkSendBytesPerSecond)
{
    public DateTime? TelemetryCapturedAtUtc { get; init; }
    public DateTime? CpuTelemetryCapturedAtUtc { get; init; }
    public IReadOnlyList<LogicalProcessorHistoryPoint> LogicalProcessors { get; init; } = [];
    public double? GpuPercent { get; init; }
    public double? DiskActivePercent { get; init; }
    public IReadOnlyList<ResourceHistoryPoint> NetworkAdapters { get; init; } = [];
    public IReadOnlyList<ResourceHistoryPoint> PhysicalDisks { get; init; } = [];
    public IReadOnlyList<ResourceHistoryPoint> GpuAdapters { get; init; } = [];
}
public sealed record NetworkAdapterDto(string Id, uint InterfaceIndex, string Alias, string Description, string Status, uint InterfaceType,
    bool IsHardware, bool IsVirtual, bool IsLoopback, bool IsTunnel, string ReceiveLinkSpeedBitsPerSecond, string TransmitLinkSpeedBitsPerSecond,
    string ReceivedBytes, string SentBytes, double? ReceiveBytesPerSecond, double? SendBytesPerSecond, IReadOnlyDictionary<string, Unavailable> Unavailable);
public sealed record PhysicalDiskDto(string Id, string Name, bool IsTotal, double? ReadBytesPerSecond, double? WriteBytesPerSecond, double? ActivePercent, IReadOnlyDictionary<string, Unavailable> Unavailable)
{
    public uint? DeviceNumber { get; init; }
    public string? Model { get; init; }
    public string? MediaType { get; init; }
    public string? BusType { get; init; }
    public DateTime? IdentityCapturedAtUtc { get; init; }
}
public sealed record GpuAdapterDto(string Id, string AdapterLuid, uint PhysicalIndex, double? UtilizationPercent, string? BusiestEngine, int EngineCount, IReadOnlyDictionary<string, Unavailable> Unavailable)
{
    public string? Description { get; init; }
    public bool? IsSoftware { get; init; }
    public DateTime? IdentityCapturedAtUtc { get; init; }
}
public sealed record TelemetryStatusDto(long SampleId, DateTime? CapturedAtUtc, double? CollectionDurationMs, int GpuInstanceCount, IReadOnlyDictionary<string, Unavailable> Unavailable)
{
    public DateTime? CpuCapturedAtUtc { get; init; }
    public double? QueryDurationMs { get; init; }
    public double? InitializationDurationMs { get; init; }
}

public sealed class ProcessDto
{
    public required string Identity { get; init; }
    public uint Pid { get; init; }
    public uint ParentPid { get; init; }
    public string? CreationTimeFileTime { get; set; }
    public required string Name { get; init; }
    public string? Path { get; set; }
    [Newtonsoft.Json.JsonIgnore]
    public string? ApplicationUserModelId { get; set; }
    public string? Publisher { get; set; }
    public uint? SessionId { get; set; }
    public string? UserName { get; set; }
    public uint ThreadCount { get; init; }
    public string? Priority { get; set; }
    public double? CpuPercent { get; set; }
    public long? WorkingSetBytes { get; set; }
    public long? PrivateBytes { get; set; }
    public uint? HandleCount { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public double? UptimeSeconds { get; set; }
    public double? IoReadBytesPerSecond { get; set; }
    public double? IoWriteBytesPerSecond { get; set; }
    public string? TotalCpuTime100ns { get; set; }
    public string? IoReadBytes { get; set; }
    public string? IoWriteBytes { get; set; }
    public double? NetworkReceiveBytesPerSecond { get; set; }
    public double? NetworkSendBytesPerSecond { get; set; }
    public double? NetworkBytesPerSecond => NetworkReceiveBytesPerSecond.HasValue && NetworkSendBytesPerSecond.HasValue
        ? NetworkReceiveBytesPerSecond.Value + NetworkSendBytesPerSecond.Value : null;
    public DateTime? NetworkCapturedAtUtc { get; set; }
    public double? GpuPercent { get; set; }
    public DateTime? GpuCapturedAtUtc { get; set; }
    public string? GpuEngine { get; set; }
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
    public long? CommitBytes { get; set; }
    public long? CommitLimitBytes { get; set; }
    public long? CacheBytes { get; set; }
    public uint? ThreadCount { get; set; }
    public uint? HandleCount { get; set; }
    public double? NetworkReceiveBytesPerSecond { get; set; }
    public double? NetworkSendBytesPerSecond { get; set; }
    public int NetworkInterfaceCount { get; set; }
    public double? GpuPercent { get; set; }
    public double? DiskActivePercent { get; set; }
    public double? DiskReadBytesPerSecond { get; set; }
    public double? DiskWriteBytesPerSecond { get; set; }
    public int ProcessCount { get; set; }
    public int LogicalProcessorCount { get; init; } = Environment.ProcessorCount;
    public double UptimeSeconds { get; init; }
    public Dictionary<string, Unavailable> Unavailable { get; init; } = new();
}
public sealed record ServiceDto(string Name, string DisplayName, string State, uint Pid);
public sealed record StartupDto(string Id, string Name, string Command, string Source, bool? Enabled, string EnabledReason)
{
    public string? LocationPath { get; init; }
    public bool CanOpenLocation { get; init; }
    public string? LocationReason { get; init; }
}
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
public sealed record ProcessHistoryDto(string Identity, uint Pid, string Name, DateTime FirstObservedAtUtc, DateTime LastObservedAtUtc, bool IsRunning, string? ObservedCpuTime100ns, string? ObservedIoReadBytes, string? ObservedIoWriteBytes)
{
    // A process may exit before the history page is opened. Keep its observed source,
    // without exporting paths or requiring the old PID to remain alive.
    [Newtonsoft.Json.JsonIgnore]
    public string? IconPath { get; init; }
    [Newtonsoft.Json.JsonIgnore]
    public string? ApplicationUserModelId { get; init; }
}
public sealed record SnapshotDto(long SampleId, DateTime CapturedAtUtc, DateTime SessionStartedAtUtc, int IntervalMs, bool Paused,
    SystemDto System, IReadOnlyList<ProcessDto> Processes, IReadOnlyList<ServiceDto> Services, IReadOnlyList<StartupDto> StartupItems,
    IReadOnlyList<UserDto> Users, IReadOnlyList<ProcessHistoryDto> History, IReadOnlyList<HistoryPoint> PerformanceHistory,
    IReadOnlyDictionary<string, Capability> Capabilities, IReadOnlyList<Unavailable> Warnings, DateTime? SlowDataCapturedAtUtc)
{
    public double SamplingDurationMs { get; init; }
    public IReadOnlyList<NetworkAdapterDto> NetworkAdapters { get; init; } = [];
    public IReadOnlyList<PhysicalDiskDto> PhysicalDisks { get; init; } = [];
    public IReadOnlyList<GpuAdapterDto> GpuAdapters { get; init; } = [];
    public TelemetryStatusDto? Telemetry { get; init; }
    public IReadOnlyList<LogicalProcessorDto> LogicalProcessors { get; init; } = [];
    public HwInfoSummaryDto HwInfo { get; init; } = HwInfoSensorsDto.WarmingUp.Summary;
    public ProcessNetworkStatusDto ProcessNetwork { get; init; } = ProcessNetworkStatusDto.WarmingUp;
}
public sealed record ProcessNetworkStatusDto(DateTime? CapturedAtUtc, string Status, uint EventsLost, uint BuffersLost,
    double? CollectionDurationMs, IReadOnlyDictionary<string, Unavailable> Unavailable)
{
    public double? WindowDurationMs { get; init; }
    public uint FlushedBuffers { get; init; }
    public uint ConsumedBuffers { get; init; }
    public IReadOnlyList<string> EventLayouts { get; init; } = [];
    public static ProcessNetworkStatusDto WarmingUp { get; } = new(null, "warmingUp", 0, 0, null,
        new Dictionary<string, Unavailable> { ["source"] = new("warmingUp", "Windows ETW网络采集正在初始化/预热。") });
}
public sealed record StartRequest(string Executable, string? Arguments);
public sealed record TerminateRequest(string CreationTimeFileTime);
public sealed record SettingsRequest(int IntervalMs, bool Paused);
public sealed record ProcessActionResult(uint Pid, string? CreationTimeFileTime, string? Identity, string Message, bool? Completed = null);
public sealed record ErrorBody(string Code, string Message, int? NativeErrorCode = null);
public sealed record ErrorEnvelope(ErrorBody Error);
public sealed record PriorityRequest(string CreationTimeFileTime, string Priority);
public sealed record PriorityResult(uint Pid, string CreationTimeFileTime, string Identity, string Priority, string Message);
public sealed record TreeMember(uint Pid, string? CreationTimeFileTime, string Identity, string Name, string? ParentIdentity, bool CanTerminate, string? Reason);
public sealed record TreePreview(string Identity, DateTime CapturedAtUtc, IReadOnlyList<TreeMember> Members, bool CanTerminate, string Message);
public sealed record ProcessIdentity(uint Pid, string CreationTimeFileTime);
public sealed record TerminateTreeRequest(string CreationTimeFileTime, IReadOnlyList<ProcessIdentity> Members);
public sealed record TreeActionItem(string Identity, uint Pid, string Status, string Message, int? NativeErrorCode = null);
public sealed record TreeActionResult(bool Completed, IReadOnlyList<TreeActionItem> Results, string Message);
public sealed record FileActionRequest(string CreationTimeFileTime, string Action);
public sealed record ProcessDetails(ProcessDto Process, DateTime CapturedAtUtc, DateTime? StartedAtUtc, double? UptimeSeconds, uint? HandleCount, long? PrivateBytes, IReadOnlyList<ServiceDto> RelatedServices, IReadOnlyDictionary<string, Unavailable> Unavailable, DateTime? SnapshotCapturedAtUtc, DateTime? RelatedServicesCapturedAtUtc);
public sealed record ConnectionDto(string Protocol, string AddressFamily, string LocalAddress, int LocalPort, string? RemoteAddress, int? RemotePort, string? State, uint Pid);
public sealed record ConnectionsResult(DateTime CapturedAtUtc, string Identity, IReadOnlyList<ConnectionDto> Connections, IReadOnlyDictionary<string, Unavailable> Unavailable, bool Truncated);
public sealed record ServiceControlRequest(string Command, string ExpectedState, uint ExpectedPid);
public sealed record ServiceControlResult(string Name, string State, uint? Pid, bool Completed, bool TimedOut, string Message);
public sealed record ServiceDetails(string Name, string DisplayName, string State, uint Pid, string? BinaryPath, string? StartType, string? Account, IReadOnlyList<string> Dependencies, bool CanStart, bool CanStop, bool CanRestart, DateTime CapturedAtUtc, IReadOnlyDictionary<string, Unavailable> Unavailable);
public sealed record SystemLocationRequest(string Location);
public sealed record StartupLocationRequest(string Id);
public sealed record EfficiencyRequest(string CreationTimeFileTime, bool Enabled);
public sealed record EfficiencyState(string Identity, bool Available, bool Enabled, bool CanRestore, string? Priority, uint? ControlMask, uint? StateMask, string? Reason);
