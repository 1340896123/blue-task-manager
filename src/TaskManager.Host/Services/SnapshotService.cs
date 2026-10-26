using TaskManager.Host.Models;
using TaskManager.Host.Native;
using System.Diagnostics;

namespace TaskManager.Host.Services;

public sealed class SnapshotService(ILogger<SnapshotService> logger, PreferencesService preferences) : BackgroundService
{
    private readonly object _gate = new();
    private readonly Win32Collector _collector = new();
    private PdhTelemetryCollector? _telemetry;
    private HwInfoTelemetryCollector? _hwInfo;
    private ProcessNetworkCollector? _processNetwork;
    public HwInfoSensorsDto HwInfo => _hwInfo?.Current ?? HwInfoSensorsDto.WarmingUp;
    private Task<SlowData>? _slowRead;
    private SamplingSettings _settings = new(preferences.IntervalMs, false);
    private readonly string _instance = Guid.NewGuid().ToString("N");
    private long _revision;
    private SnapshotRepresentation? _serialized;
    private SnapshotDto? _snapshot;
    private DateTime _sessionStartedAt = DateTime.UtcNow;
    private bool _resetHistory;
    private bool _forceSlowRefresh;
    private readonly Dictionary<string, HistoryAccumulator> _history = new();
    private Dictionary<string, ProcessCounters> _historyPrevious = new();
    private readonly Queue<HistoryPoint> _performance = new();
    public static readonly IReadOnlyDictionary<string, Capability> Capabilities = new Dictionary<string, Capability>
    {
        ["processes"] = new(true, "CsWin32 Toolhelp / GetProcessTimes / PSAPI / GetProcessIoCounters"),
        ["system"] = new(true, "CsWin32 GetSystemTimes / GlobalMemoryStatusEx / GetPerformanceInfo / GetTickCount64"),
        ["logicalProcessors"] = new(true, "CsWin32 PDH English Processor Information % Processor Time；真实group,index、独立来源时间及相邻样本"),
        ["hwInfo"] = new(true, "CsWin32 HWiNFO SM2只读共享内存；独立后台缓存/有界互斥同步", "可选来源：需HWiNFO运行并由其当前版本/许可启用共享内存。"),
        ["network"] = new(true, "CsWin32 GetIfTable2：仅Up状态真实硬件适配器总量；排除过滤、虚拟、回环和隧道接口"),
        ["services"] = new(true, "CsWin32 SCM EnumServicesStatusEx"),
        ["users"] = new(true, "CsWin32 WTS + 进程令牌 SID，按明确用户/会话聚合"),
        ["startup"] = new(true, "只读 Registry Run/RunOnce + Startup 文件夹"),
        ["history"] = new(true, "本次会话相邻观测样本的 CPU/I/O 增量"),
        ["processDetails"] = new(true, "CsWin32 身份句柄实时查询；速率来自最近成功快照"),
        ["processConnections"] = new(true, "CsWin32 GetExtendedTcpTable/GetExtendedUdpTable IPv4/IPv6 OWNER_PID；端点不是吞吐"),
        ["processPriority"] = new(true, "CsWin32 SetPriorityClass/GetPriorityClass，五档非实时优先级"),
        ["processTree"] = new(true, "Toolhelp真实关系/FILETIME身份/同句柄预检/叶到根退出确认"),
        ["processIcons"] = new(true, "CsWin32 EXE ExtractIconEx / SHGetFileInfo / AppsFolder IShellItemImageFactory；独立COM STA按需PNG，覆盖进程/历史/启动/服务"),
        ["serviceControl"] = new(true, "CsWin32 SCM；最小动作权限、同句柄状态/PID核验、15秒有界观察", "成功取决于当前用户权限，不自动提权或停止依赖服务。"),
        ["shellLocations"] = new(true, "CsWin32 Shell公开接口/固定Windows系统入口"),
        ["efficiency"] = new(true, "CsWin32 ProcessPowerThrottling + SetPriorityClass + 原生读回", "每个目标需按身份读取实际能力；外部效率模式不覆盖，没有恢复基线不关闭。"),
        ["processNetwork"] = new(true, "CsWin32 Windows ETW TCP/UDP IPv4/IPv6；独立系统实时logger、完整消费窗口、PID+创建FILETIME", "包括Windows报告的回环/虚拟网络，不等于硬件网卡合计；权限、预热、丢事件和过期逐项反馈。"),
        ["processGpu"] = new(true, "CsWin32 PDH English GPU Engine；最忙单引擎，两样本与创建身份校验", "需驱动提供有效计数器；无实例、预热或受限时null并给原因。"),
        ["gpuAdapters"] = new(true, "CsWin32 PDH；同适配器引擎先跨PID汇总，再取最忙引擎，含软件适配器", "按计数器LUID/phys索引标识；phys索引不是Windows GPU编号或实体硬件保证，真实DXGI描述单独匹配。"),
        ["gpuIdentity"] = new(true, "CsWin32 DXGI CreateDXGIFactory1 / IDXGIAdapter1.GetDesc1，按完整AdapterLuid精确匹配", "描述/软件标志按实际读取，失败不影响PDH指标；不推断Windows GPU编号。"),
        ["networkAdapters"] = new(true, "CsWin32 GetIfTable2；HardwareInterface且非FilterInterface、非回环/隧道，仅真实硬件；断开硬件保留原状态"),
        ["diskActivity"] = new(true, "CsWin32 PDH English PhysicalDisk；100-% Idle Time", "系统活动百分比为最忙物理磁盘；系统读写为非_Total实例吞吐之和，进程I/O不参与。"),
        ["diskIdentity"] = new(true, "CsWin32 CreateFile / DeviceIoControl；核验STORAGE_DEVICE_NUMBER后读取存储描述符和寻道惩罚", "型号与总线来自设备报告；HDD/SSD按Windows寻道惩罚判定，复合或虚拟设备介质保持未知。")
    };

    public SamplingSettings Settings { get { lock (_gate) return _settings; } }
    public SnapshotDto? Current { get { lock (_gate) { SynchronizePreferenceWarning(); return _snapshot; } } }
    public SnapshotRepresentation? Representation
    {
        get
        {
            lock (_gate)
            {
                SynchronizePreferenceWarning();
                if (_snapshot is null) return null;
                // Weak ETags describe the same JSON representation across negotiated gzip/identity encoding.
                return _serialized ??= new($"W/\"{_instance}-{_revision}\"", System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(_snapshot, SnapshotJson.Settings)));
            }
        }
    }
    private void Invalidate() { _revision++; _serialized = null; }
    private void SynchronizePreferenceWarning()
    {
        if (_snapshot is null) return;
        var before = _snapshot.Warnings.FirstOrDefault(value => value.Code.StartsWith("preferences", StringComparison.Ordinal));
        var after = preferences.Warning;
        if (before == after) return;
        var warnings = _snapshot.Warnings.Where(value => !value.Code.StartsWith("preferences", StringComparison.Ordinal));
        _snapshot = _snapshot with { Warnings = (after is null ? warnings : warnings.Append(after)).ToArray() };
        Invalidate();
    }
    public SamplingSettings ApplySettings(SettingsRequest request)
    {
        if (request.IntervalMs is not (1000 or 2000 or 5000)) throw new ApiException(400, "invalidInterval", "采样周期只允许 1000、2000、5000 毫秒。");
        lock (_gate)
        {
            preferences.SaveInterval(request.IntervalMs);
            _settings = new(request.IntervalMs, request.Paused);
            _hwInfo?.SetPaused(request.Paused);
            _processNetwork?.SetPaused(request.Paused);
            if (_snapshot is not null) _snapshot = _snapshot with { IntervalMs = _settings.IntervalMs, Paused = _settings.Paused };
            Invalidate();
            return _settings;
        }
    }
    public void ResetHistory() { lock (_gate) { _sessionStartedAt = DateTime.UtcNow; _resetHistory = true; if (_snapshot is not null) _snapshot = _snapshot with { History = Array.Empty<ProcessHistoryDto>(), SessionStartedAtUtc = _sessionStartedAt }; Invalidate(); } }
    public void RefreshSlowData() { lock (_gate) _forceSlowRefresh = true; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _telemetry = new(() => Settings);
        _hwInfo = new(() => Settings);
        _processNetwork = new(() => Settings);
        var slow = new SlowData([], [], [], [new Unavailable("loading", "服务、启动项和会话正在后台读取；基础进程采样继续。")], null);
        var nextSlowAttempt = DateTime.MinValue;
        long sampleId = 0;
        long nextSample = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = Settings;
            if (!settings.Paused && Stopwatch.GetTimestamp() >= nextSample)
            {
                try
                {
                    var started = Stopwatch.GetTimestamp();
                    var raw = _collector.Collect();
                    var telemetry = FreshTelemetry(_telemetry.Current, settings.IntervalMs);
                    var hwInfo = _hwInfo.Current;
                    ApplyTelemetry(raw, telemetry, settings.IntervalMs);
                    var processNetwork = ProcessNetworkCollector.Apply(raw.Processes, _processNetwork.Current, settings.IntervalMs);
                    var capturedAt = DateTime.UtcNow;
                    bool refreshSlow;
                    lock (_gate) { refreshSlow = _forceSlowRefresh; _forceSlowRefresh = false; }
                    if (_slowRead is { IsCompleted: true } completed)
                    {
                        _slowRead = null;
                        try { slow = await completed; }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "慢来源读取失败；保留上次来源，基础采样继续");
                            slow = slow with { Warnings = slow.Warnings.Where(w => w.Code != "slowSourceError").Append(new Unavailable("slowSourceError", "服务、启动项或会话读取失败，保留上次来源并稍后重试。")).TakeLast(20).ToList() };
                            nextSlowAttempt = DateTime.UtcNow.AddSeconds(5);
                        }
                    }
                    if ((slow.CapturedAtUtc is null || capturedAt - slow.CapturedAtUtc.Value > TimeSpan.FromSeconds(15) || refreshSlow) && capturedAt >= nextSlowAttempt)
                    {
                        if (_slowRead is null) _slowRead = Task.Run(SlowDataReader.Read, CancellationToken.None);
                        else if (refreshSlow) lock (_gate) _forceSlowRefresh = true;
                    }
                    var users = AggregateUsers(slow.Users, raw.Processes);
                    lock (_gate)
                    {
                        // A pause accepted during the native collection must not
                        // publish a later sample/history point after its response.
                        if (_settings.Paused) continue;
                        if (_resetHistory) { _history.Clear(); _historyPrevious.Clear(); _resetHistory = false; }
                        Accumulate(raw, capturedAt);
                        _performance.Enqueue(new(capturedAt, raw.System.CpuPercent, raw.System.MemoryPercent, raw.System.NetworkReceiveBytesPerSecond, raw.System.NetworkSendBytesPerSecond)
                        {
                            TelemetryCapturedAtUtc = telemetry.Status.CapturedAtUtc, GpuPercent = raw.System.GpuPercent, DiskActivePercent = raw.System.DiskActivePercent,
                            CpuTelemetryCapturedAtUtc = telemetry.Status.CpuCapturedAtUtc,
                            LogicalProcessors = telemetry.LogicalProcessors.Select(row => new LogicalProcessorHistoryPoint(row.Id, row.Percent)).ToArray(),
                            NetworkAdapters = raw.NetworkAdapters.Where(row => row.ReceiveBytesPerSecond.HasValue || row.SendBytesPerSecond.HasValue).Select(row => new ResourceHistoryPoint(row.Id, null, row.ReceiveBytesPerSecond, row.SendBytesPerSecond)).ToArray(),
                            PhysicalDisks = telemetry.Disks.Select(row => new ResourceHistoryPoint(row.Id, row.ActivePercent, row.ReadBytesPerSecond, row.WriteBytesPerSecond)).ToArray(),
                            GpuAdapters = telemetry.Gpus.Select(row => new ResourceHistoryPoint(row.Id, row.UtilizationPercent, null, null)).ToArray()
                        });
                        while (_performance.Count > 120) _performance.Dequeue();
                        _snapshot = new(++sampleId, capturedAt, _sessionStartedAt, _settings.IntervalMs, _settings.Paused, raw.System, raw.Processes,
                            slow.Services, slow.StartupItems, users, _history.Values.Select(item => item.ToDto()).OrderByDescending(item => item.IsRunning).ThenBy(item => item.Name).ToArray(),
                            _performance.ToArray(), Capabilities, preferences.Warning is { } warning ? slow.Warnings.Append(warning).ToArray() : slow.Warnings, slow.CapturedAtUtc)
                        { SamplingDurationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds, NetworkAdapters = raw.NetworkAdapters,
                            PhysicalDisks = telemetry.Disks, GpuAdapters = telemetry.Gpus, Telemetry = telemetry.Status,
                            LogicalProcessors = telemetry.LogicalProcessors, HwInfo = hwInfo.Summary, ProcessNetwork = processNetwork };
                        Invalidate();
                    }
                    _telemetry.Request(raw.Processes);
                    _hwInfo.Request();
                    _processNetwork.Request(raw.Processes);
                    nextSample = Stopwatch.GetTimestamp() + settings.IntervalMs * Stopwatch.Frequency / 1000;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Windows 采样失败；保留最后成功快照");
                    lock (_gate) { if (_snapshot is not null) _snapshot = _snapshot with { Warnings = _snapshot.Warnings.Append(new Unavailable("error", "最新系统采样失败；当前数据是上次成功快照。")).TakeLast(20).ToArray() }; Invalidate(); }
                    nextSample = Stopwatch.GetTimestamp() + settings.IntervalMs * Stopwatch.Frequency / 1000;
                }
            }
            // Settings react within 100ms while sampling itself remains single-threaded and bounded.
            await Task.Delay(100, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_telemetry is not null) await _telemetry.DisposeAsync();
        if (_hwInfo is not null) await _hwInfo.DisposeAsync();
        if (_processNetwork is not null) await _processNetwork.DisposeAsync();
        if (_slowRead is { } pending) { try { await pending.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken); } catch (Exception ex) { logger.LogWarning(ex, "慢来源关闭时读取失败"); } }
    }
    public override void Dispose() { base.Dispose(); _collector.Dispose(); }

    private static void ApplyTelemetry(RawCollection raw, PdhSample sample, int intervalMs)
    {
        var stale = sample.Status.CapturedAtUtc is { } captured && DateTime.UtcNow - captured > TimeSpan.FromMilliseconds(Math.Max(5000, intervalMs * 3));
        var staleReason = new Unavailable("stale", "PDH最新有效采样已过期，等待后台查询恢复。");
        foreach (var process in raw.Processes)
        {
            process.GpuCapturedAtUtc = sample.Status.CapturedAtUtc;
            if (!stale && sample.Processes.TryGetValue(process.Identity, out var gpu))
            {
                process.GpuPercent = gpu.Percent; process.GpuEngine = gpu.Engine;
                if (gpu.Unavailable is { } reason) process.Unavailable["gpuPercent"] = reason;
            }
            else process.Unavailable["gpuPercent"] = stale ? staleReason : sample.Status.Unavailable.GetValueOrDefault("gpu")
                ?? (sample.Status.SampleId < 2 ? NativeSupport.WarmingUp : new("noObservedEngine", "本身份在相邻有效PDH样本中没有可安全关联的GPU Engine；无实例不等于0。"));
        }
        var gpuReason = stale ? staleReason : sample.Status.Unavailable.GetValueOrDefault("gpu")
            ?? sample.Gpus.Select(row => row.Unavailable.GetValueOrDefault("utilizationPercent")).FirstOrDefault(reason => reason is not null) ?? NativeSupport.WarmingUp;
        if (!stale && sample.Gpus.Count > 0 && sample.Gpus.All(row => row.UtilizationPercent.HasValue)) raw.System.GpuPercent = sample.Gpus.Max(row => row.UtilizationPercent!.Value);
        else raw.System.Unavailable["gpuPercent"] = gpuReason;
        foreach (var field in new[] { "diskActivePercent", "diskReadBytesPerSecond", "diskWriteBytesPerSecond" })
            raw.System.Unavailable[field] = stale ? staleReason : sample.Status.Unavailable.GetValueOrDefault("disk") ?? new("partial", "部分物理磁盘计数器不可用。");
        if (!stale && sample.Disks.Count > 0)
        {
            if (sample.Disks.All(row => row.ActivePercent.HasValue)) { raw.System.DiskActivePercent = sample.Disks.Max(row => row.ActivePercent!.Value); raw.System.Unavailable.Remove("diskActivePercent"); }
            if (sample.Disks.All(row => row.ReadBytesPerSecond.HasValue)) { raw.System.DiskReadBytesPerSecond = sample.Disks.Sum(row => row.ReadBytesPerSecond!.Value); raw.System.Unavailable.Remove("diskReadBytesPerSecond"); }
            if (sample.Disks.All(row => row.WriteBytesPerSecond.HasValue)) { raw.System.DiskWriteBytesPerSecond = sample.Disks.Sum(row => row.WriteBytesPerSecond!.Value); raw.System.Unavailable.Remove("diskWriteBytesPerSecond"); }
        }
    }

    private static PdhSample FreshTelemetry(PdhSample sample, int intervalMs)
    {
        sample = sample with { Disks = sample.Disks.Select(DiskIdentityCache.FreshIdentity).ToArray() };
        if (sample.Status.CpuCapturedAtUtc is { } cpuCaptured && DateTime.UtcNow - cpuCaptured > TimeSpan.FromMilliseconds(Math.Max(5000, intervalMs * 3)))
        {
            var cpuStale = new Unavailable("stale", "逻辑处理器最新PDH采样已过期，等待后台查询恢复。");
            sample = sample with
            {
                Status = sample.Status with { Unavailable = new Dictionary<string, Unavailable>(sample.Status.Unavailable) { ["logicalProcessors"] = cpuStale } },
                LogicalProcessors = sample.LogicalProcessors.Select(row => row with { Percent = null, Unavailable = new Dictionary<string, Unavailable> { ["percent"] = cpuStale } }).ToArray()
            };
        }
        if (sample.Status.CapturedAtUtc is not { } captured || DateTime.UtcNow - captured <= TimeSpan.FromMilliseconds(Math.Max(5000, intervalMs * 3))) return sample;
        var stale = new Unavailable("stale", "PDH最新采样已过期，等待后台查询恢复。");
        return sample with
        {
            Status = sample.Status with { Unavailable = new Dictionary<string, Unavailable>(sample.Status.Unavailable) { ["gpu"] = stale, ["disk"] = stale, ["logicalProcessors"] = stale } },
            Processes = new Dictionary<string, GpuReading>(),
            Gpus = sample.Gpus.Select(row => row with { UtilizationPercent = null, BusiestEngine = null, Unavailable = new Dictionary<string, Unavailable>(row.Unavailable) { ["utilizationPercent"] = stale } }).ToArray(),
            Disks = sample.Disks.Select(row => row with { ReadBytesPerSecond = null, WriteBytesPerSecond = null, ActivePercent = null,
                Unavailable = new Dictionary<string, Unavailable>(row.Unavailable) { ["readBytesPerSecond"] = stale, ["writeBytesPerSecond"] = stale, ["activePercent"] = stale } }).ToArray()
        };
    }

    private void Accumulate(RawCollection raw, DateTime capturedAt)
    {
        foreach (var row in _history.Values) row.IsRunning = false;
        foreach (var process in raw.Processes)
        {
            if (process.CreationTimeFileTime is null) continue;
            if (!_history.TryGetValue(process.Identity, out var history)) _history[process.Identity] = history = new(process, capturedAt);
            history.IsRunning = true;
            history.LastObserved = capturedAt;
            history.IconPath ??= process.Path;
            history.ApplicationUserModelId ??= process.ApplicationUserModelId;
            if (raw.Counters.TryGetValue(process.Identity, out var current))
            {
                if (_historyPrevious.TryGetValue(process.Identity, out var previous)) history.Add(previous, current);
                history.HasCpu |= current.Cpu.HasValue;
                history.HasRead |= current.Read.HasValue;
                history.HasWrite |= current.Write.HasValue;
            }
        }
        _historyPrevious = raw.Counters;
        // Keep the session useful without allowing indefinitely running telemetry to grow memory forever.
        if (_history.Count > 20_000)
            foreach (var item in _history.Values.Where(item => !item.IsRunning).OrderBy(item => item.LastObserved).Take(_history.Count - 20_000).ToArray()) _history.Remove(item.Identity);
    }

    private static List<UserDto> AggregateUsers(List<UserDto> source, List<ProcessDto> processes)
    {
        return source.Select(user =>
        {
            var owned = processes.Where(process => process.SessionId == user.SessionId && string.Equals(process.UserName, user.UserName, StringComparison.OrdinalIgnoreCase)).ToList();
            var unknown = processes.Count(process => process.SessionId == user.SessionId && process.UserName is null);
            var unavailable = new Dictionary<string, Unavailable>(user.Unavailable);
            if (owned.Count == 0 || owned.Any(process => process.CpuPercent is null)) unavailable["cpuPercent"] = new("partial", "聚合仅包含可确定归属且可读取的进程；部分 CPU 不可用。");
            if (owned.Count == 0 || owned.Any(process => process.WorkingSetBytes is null)) unavailable["workingSetBytes"] = new("partial", "聚合仅包含可确定归属且可读取的进程；部分工作集不可用。");
            return new UserDto { SessionId = user.SessionId, UserName = user.UserName, StationName = user.StationName, State = user.State,
                ProcessCount = owned.Count, UnreadableProcessCount = unknown, CpuPercent = owned.Count > 0 && owned.All(process => process.CpuPercent.HasValue) ? owned.Sum(process => process.CpuPercent!.Value) : null,
                WorkingSetBytes = owned.Count > 0 && owned.All(process => process.WorkingSetBytes.HasValue) ? owned.Sum(process => process.WorkingSetBytes!.Value) : null, Unavailable = unavailable };
        }).ToList();
    }

    private sealed class HistoryAccumulator(ProcessDto process, DateTime timestamp)
    {
        public string Identity { get; } = process.Identity;
        private uint Pid { get; } = process.Pid;
        private string Name { get; } = process.Name;
        public string? IconPath { get; set; } = process.Path;
        public string? ApplicationUserModelId { get; set; } = process.ApplicationUserModelId;
        private DateTime FirstObserved { get; } = timestamp;
        public DateTime LastObserved { get; set; } = timestamp;
        public bool IsRunning { get; set; }
        public bool HasCpu { get; set; }
        public bool HasRead { get; set; }
        public bool HasWrite { get; set; }
        private ulong Cpu { get; set; }
        private ulong Read { get; set; }
        private ulong Write { get; set; }
        public void Add(ProcessCounters previous, ProcessCounters current)
        {
            if (previous.Cpu.HasValue && current.Cpu >= previous.Cpu) Cpu += current.Cpu.Value - previous.Cpu.Value;
            if (previous.Read.HasValue && current.Read >= previous.Read) Read += current.Read.Value - previous.Read.Value;
            if (previous.Write.HasValue && current.Write >= previous.Write) Write += current.Write.Value - previous.Write.Value;
        }
        public ProcessHistoryDto ToDto() => new(Identity, Pid, Name, FirstObserved, LastObserved, IsRunning, HasCpu ? Cpu.ToString(System.Globalization.CultureInfo.InvariantCulture) : null, HasRead ? Read.ToString(System.Globalization.CultureInfo.InvariantCulture) : null, HasWrite ? Write.ToString(System.Globalization.CultureInfo.InvariantCulture) : null)
        { IconPath = IconPath, ApplicationUserModelId = ApplicationUserModelId };
    }
}
