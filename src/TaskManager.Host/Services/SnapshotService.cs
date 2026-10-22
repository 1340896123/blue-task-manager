using TaskManager.Host.Models;
using TaskManager.Host.Native;
using System.Diagnostics;

namespace TaskManager.Host.Services;

public sealed class SnapshotService(ILogger<SnapshotService> logger, PreferencesService preferences) : BackgroundService
{
    private readonly object _gate = new();
    private readonly Win32Collector _collector = new();
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
        ["system"] = new(true, "CsWin32 GetSystemTimes / GlobalMemoryStatusEx / GetTickCount64"),
        ["network"] = new(true, "CsWin32 GetIfTable2：活动非回环、非隧道网卡总量，可能含虚拟接口"),
        ["services"] = new(true, "CsWin32 SCM EnumServicesStatusEx"),
        ["users"] = new(true, "CsWin32 WTS + 进程令牌 SID，按明确用户/会话聚合"),
        ["startup"] = new(true, "只读 Registry Run/RunOnce + Startup 文件夹"),
        ["history"] = new(true, "本次会话相邻观测样本的 CPU/I/O 增量"),
        ["processNetwork"] = new(false, "未接入", "未接入逐进程网络采集；不能从进程 I/O 推导。"),
        ["processGpu"] = new(false, "未接入", "未接入逐进程 GPU 采集。"),
        ["diskActivity"] = new(false, "未接入", "进程 I/O 不是物理磁盘吞吐或活动时间。")
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
                return _serialized ??= new($"\"{_instance}-{_revision}\"", System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(_snapshot, SnapshotJson.Settings)));
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
        preferences.SaveInterval(request.IntervalMs);
        lock (_gate)
        {
            _settings = new(request.IntervalMs, request.Paused);
            if (_snapshot is not null) _snapshot = _snapshot with { IntervalMs = _settings.IntervalMs, Paused = _settings.Paused };
            Invalidate();
            return _settings;
        }
    }
    public void ResetHistory() { lock (_gate) { _sessionStartedAt = DateTime.UtcNow; _resetHistory = true; if (_snapshot is not null) _snapshot = _snapshot with { History = Array.Empty<ProcessHistoryDto>(), SessionStartedAtUtc = _sessionStartedAt }; Invalidate(); } }
    public void RefreshSlowData() { lock (_gate) _forceSlowRefresh = true; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SlowData? slow = null;
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
                    var capturedAt = DateTime.UtcNow;
                    bool refreshSlow;
                    lock (_gate) { refreshSlow = _forceSlowRefresh; _forceSlowRefresh = false; }
                    if (slow is null || capturedAt - slow.CapturedAtUtc > TimeSpan.FromSeconds(15) || refreshSlow) slow = SlowDataReader.Read();
                    var users = AggregateUsers(slow.Users, raw.Processes);
                    lock (_gate)
                    {
                        if (_resetHistory) { _history.Clear(); _historyPrevious.Clear(); _resetHistory = false; }
                        Accumulate(raw, capturedAt);
                        _performance.Enqueue(new(capturedAt, raw.System.CpuPercent, raw.System.MemoryPercent, raw.System.NetworkReceiveBytesPerSecond, raw.System.NetworkSendBytesPerSecond));
                        while (_performance.Count > 120) _performance.Dequeue();
                        _snapshot = new(++sampleId, capturedAt, _sessionStartedAt, _settings.IntervalMs, _settings.Paused, raw.System, raw.Processes,
                            slow.Services, slow.StartupItems, users, _history.Values.Select(item => item.ToDto()).OrderByDescending(item => item.IsRunning).ThenBy(item => item.Name).ToArray(),
                            _performance.ToArray(), Capabilities, preferences.Warning is { } warning ? slow.Warnings.Append(warning).ToArray() : slow.Warnings, slow.CapturedAtUtc)
                        { SamplingDurationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds };
                        Invalidate();
                    }
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

    public override void Dispose() { base.Dispose(); _collector.Dispose(); }

    private void Accumulate(RawCollection raw, DateTime capturedAt)
    {
        foreach (var row in _history.Values) row.IsRunning = false;
        foreach (var process in raw.Processes)
        {
            if (process.CreationTimeFileTime is null) continue;
            if (!_history.TryGetValue(process.Identity, out var history)) _history[process.Identity] = history = new(process, capturedAt);
            history.IsRunning = true;
            history.LastObserved = capturedAt;
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
        public ProcessHistoryDto ToDto() => new(Identity, Pid, Name, FirstObserved, LastObserved, IsRunning, HasCpu ? Cpu.ToString(System.Globalization.CultureInfo.InvariantCulture) : null, HasRead ? Read.ToString(System.Globalization.CultureInfo.InvariantCulture) : null, HasWrite ? Write.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }
}
