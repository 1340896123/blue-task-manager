using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using TaskManager.Host.Models;
using Windows.Win32;
using Windows.Win32.System.Performance;
using Windows.Win32.System.Threading;

namespace TaskManager.Host.Native;

internal sealed record GpuReading(double? Percent, string? Engine, Unavailable? Unavailable);
internal sealed record PdhSample(TelemetryStatusDto Status, IReadOnlyDictionary<string, GpuReading> Processes,
    IReadOnlyList<PhysicalDiskDto> Disks, IReadOnlyList<GpuAdapterDto> Gpus);

/// <summary>One deferred worker and one long-lived batch query. Never holds the snapshot/settings lock.</summary>
internal sealed partial class PdhTelemetryCollector(Func<SamplingSettings> settings) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private Dictionary<uint, string>? _pending;
    private PdhSample _current = new(new(0, null, null, 0, new Dictionary<string, Unavailable>
        { ["gpu"] = NativeSupport.WarmingUp, ["disk"] = NativeSupport.WarmingUp }), new Dictionary<string, GpuReading>(), [], []);
    private Dictionary<uint, string> _previousIdentities = new();
    private PdhCloseQuerySafeHandle? _query;
    private Counter? _gpu, _read, _write, _idle;
    private long _sampleId;
    private bool _collected;
    private DateTime _previousCapture;
    private double? _initializationDurationMs;

    public PdhSample Current { get { lock (_gate) return _current; } }
    public void Request(IReadOnlyList<ProcessDto> processes)
    {
        var identities = processes.Where(row => row.CreationTimeFileTime is not null).Take(20_000).ToDictionary(row => row.Pid, row => row.Identity);
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) return;
            _pending = identities;
            _worker ??= Task.Run(RunAsync);
            if (_signal.CurrentCount == 0) _signal.Release();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await _signal.WaitAsync(_stop.Token);
                Dictionary<uint, string>? requested;
                lock (_gate) { requested = _pending; _pending = null; }
                if (requested is null || settings().Paused) continue;
                try
                {
                    if (_query is null) Initialize(); // Provider initialization can take seconds: only this worker waits.
                    if (settings().Paused || _stop.IsCancellationRequested) continue;
                    var sample = Collect(requested);
                    lock (_gate) _current = sample; // Publish a new graph; never mutate a published DTO.
                }
                catch (Exception exception)
                {
                    var reason = exception is PdhException native ? native.Reason : new Unavailable("error", "PDH读取失败：" + exception.Message);
                    lock (_gate) _current = new(new(++_sampleId, DateTime.UtcNow, null, 0, new Dictionary<string, Unavailable> { ["gpu"] = reason, ["disk"] = reason }), new Dictionary<string, GpuReading>(), [], []);
                    _previousIdentities.Clear(); _collected = false;
                    // No tight retry loop. The next basic sample requests the next attempt.
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { _query?.Dispose(); _query = null; }
    }

    private void Initialize()
    {
        var started = Stopwatch.GetTimestamp();
        var status = PInvoke.PdhOpenQuery(null, 0, out var query);
        if (status != 0) { query.Dispose(); throw new PdhException(status, "创建PDH查询失败"); }
        _query = query;
        _gpu = Add(@"\GPU Engine(*)\Utilization Percentage");
        _read = Add(@"\PhysicalDisk(*)\Disk Read Bytes/sec");
        _write = Add(@"\PhysicalDisk(*)\Disk Write Bytes/sec");
        _idle = Add(@"\PhysicalDisk(*)\% Idle Time");
        _initializationDurationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private Counter Add(string path)
    {
        var status = PInvoke.PdhAddEnglishCounter(_query!, path, 0, out var handle);
        return new(handle, status == 0 ? null : PdhError(status, "添加English计数器失败"));
    }

    private unsafe PdhSample Collect(Dictionary<uint, string> requested)
    {
        var started = Stopwatch.GetTimestamp();
        var captureStarted = DateTime.UtcNow;
        var status = PInvoke.PdhCollectQueryData((PDH_HQUERY)_query!.DangerousGetHandle());
        var queryDuration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (status != 0) throw new PdhException(status, "批量采集PDH失败");
        var errors = new Dictionary<string, Unavailable>();
        var gpu = Read(_gpu!, out var gpuError);
        var read = Read(_read!, out var readError);
        var write = Read(_write!, out var writeError);
        var idle = Read(_idle!, out var idleError);
        if (gpuError is not null) errors["gpu"] = gpuError;
        if (readError is not null || writeError is not null || idleError is not null) errors["disk"] = readError ?? writeError ?? idleError!;
        var identities = new Dictionary<uint, string>();
        var checkedPids = new HashSet<uint>();
        var invalidPids = new HashSet<uint>();
        var values = new Dictionary<string, GpuReading>();
        var engines = new Dictionary<(string Adapter, uint Physical, string Engine), double>();
        var adapterErrors = new HashSet<(string Adapter, uint Physical)>();
        var adapterEngines = new Dictionary<(string Adapter, uint Physical), HashSet<string>>();
        var engineCount = 0;
        foreach (var item in gpu)
        {
            var match = GpuInstance().Match(item.Key);
            if (!match.Success || !uint.TryParse(match.Groups[1].Value, out var pid) || !uint.TryParse(match.Groups[3].Value, out var physical)) continue;
            engineCount++;
            var adapter = match.Groups[2].Value;
            var engine = match.Groups[4].Value;
            var key = (adapter, physical);
            if (!adapterEngines.TryGetValue(key, out var engineNames)) adapterEngines[key] = engineNames = new();
            engineNames.Add(engine);
            if (requested.TryGetValue(pid, out var requestIdentity) && checkedPids.Add(pid) && ValidateIdentity(pid, requestIdentity))
            {
                identities[pid] = requestIdentity;
                values[requestIdentity] = new(null, null, NativeSupport.WarmingUp);
            }
            if (item.Value.Value is not { } percent) { adapterErrors.Add(key); invalidPids.Add(pid); continue; }
            if (!_collected) continue;
            var engineKey = (adapter, physical, engine);
            engines[engineKey] = engines.GetValueOrDefault(engineKey) + percent;
            if (!requested.TryGetValue(pid, out var identity)) continue;
            if (!identities.ContainsKey(pid) || !_previousIdentities.TryGetValue(pid, out var previous) || previous != identity) continue;
            // The new identity must predate the previous PDH sampling boundary, not just the latest base snapshot.
            if (!long.TryParse(identity.AsSpan(identity.IndexOf(':') + 1), CultureInfo.InvariantCulture, out var creation) || creation > _previousCapture.ToFileTimeUtc()) continue;
            var normalized = Math.Clamp(percent, 0, 100);
            if (!values.TryGetValue(identity, out var old) || old.Percent is null || normalized > old.Percent)
                values[identity] = new(normalized, $"{adapter}_phys_{physical}/{engine}", null);
        }
        foreach (var pid in invalidPids) if (requested.TryGetValue(pid, out var identity) && values.ContainsKey(identity)) values[identity] = new(null, null, new("warmingUp", "本身份部分GPU引擎尚无有效CStatus，等待完整相邻样本。"));
        if (adapterEngines.Count > 256) errors["gpu"] = new("capacity", "GPU物理适配器超过256项边界。");
        var gpus = adapterEngines.Count > 256 ? [] : adapterEngines.Select(pair =>
        {
            var present = engines.Where(item => item.Key.Adapter == pair.Key.Adapter && item.Key.Physical == pair.Key.Physical).ToArray();
            var available = _collected && present.Length > 0 && !adapterErrors.Contains(pair.Key) && gpuError is null;
            var busiest = present.OrderByDescending(item => item.Value).FirstOrDefault();
            return new GpuAdapterDto($"{pair.Key.Adapter}_phys_{pair.Key.Physical}", pair.Key.Adapter, pair.Key.Physical,
                available ? Math.Clamp(busiest.Value, 0, 100) : null, available ? busiest.Key.Engine : null, pair.Value.Count,
                available ? new Dictionary<string, Unavailable>() : new Dictionary<string, Unavailable> { ["utilizationPercent"] = gpuError ?? (_collected ? new("partial", "部分GPU引擎计数器无有效CStatus；等待完整有效样本。") : NativeSupport.WarmingUp) });
        }).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var names = read.Keys.Concat(write.Keys).Concat(idle.Keys).Where(name => !name.Equals("_Total", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal).ToArray();
        if (names.Length > 256) { errors["disk"] = new("capacity", "物理磁盘实例超过256项边界。"); names = []; }
        var disks = names.Select(name =>
        {
            var missing = new Dictionary<string, Unavailable>();
            var r = Value(read, name, readError, "readBytesPerSecond", missing);
            var w = Value(write, name, writeError, "writeBytesPerSecond", missing);
            var i = Value(idle, name, idleError, "activePercent", missing);
            return new PhysicalDiskDto(name, name, false, r, w, i.HasValue ? 100 - Math.Clamp(i.Value, 0, 100) : null, missing);
        }).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (gpu.Count == 0 && !errors.ContainsKey("gpu")) errors["gpu"] = new("notSupported", "当前GPU驱动未提供GPU Engine实例。");
        if (disks.Length == 0 && !errors.ContainsKey("disk")) errors["disk"] = new("notSupported", "当前系统未提供PhysicalDisk实例。");
        if (!_collected) { errors.TryAdd("gpu", NativeSupport.WarmingUp); errors.TryAdd("disk", NativeSupport.WarmingUp); }
        _previousIdentities = identities;
        _previousCapture = captureStarted;
        _collected = true;
        return new(new(++_sampleId, DateTime.UtcNow, Stopwatch.GetElapsedTime(started).TotalMilliseconds, engineCount, errors)
            { QueryDurationMs = queryDuration, InitializationDurationMs = _initializationDurationMs }, values, disks, gpus);
    }

    private double? Value(Dictionary<string, CounterValue> source, string name, Unavailable? sourceError, string field, Dictionary<string, Unavailable> missing)
    {
        if (!_collected) { missing[field] = sourceError ?? NativeSupport.WarmingUp; return null; }
        if (source.TryGetValue(name, out var result) && result.Value is { } value) return value;
        missing[field] = sourceError ?? (source.TryGetValue(name, out result) ? result.Error : null) ?? new("notCollected", "该物理磁盘实例没有提供此计数器。");
        return null;
    }

    private static bool ValidateIdentity(uint pid, string identity)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        return !process.IsInvalid && PInvoke.GetProcessTimes(process, out var creation, out _, out _, out _) && identity == $"{pid}:{NativeSupport.FileTime(creation)}";
    }

    private static unsafe Dictionary<string, CounterValue> Read(Counter counter, out Unavailable? error)
    {
        error = counter.Error;
        var result = new Dictionary<string, CounterValue>(StringComparer.Ordinal);
        if (error is not null) return result;
        // Re-query size on a racing MORE_DATA response; bounded native memory and retries.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            uint size = 0, count;
            var status = PInvoke.PdhGetFormattedCounterArray(counter.Handle, PDH_FMT.PDH_FMT_DOUBLE, &size, &count, null);
            if (status != PInvoke.PDH_MORE_DATA) { error = PdhError(status, "读取PDH数组长度失败"); return result; }
            if (size is 0 or > 8 * 1024 * 1024 || count > 65_536) { error = new("capacity", "PDH实例数组超过8MiB/65536项边界。"); return result; }
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                status = PInvoke.PdhGetFormattedCounterArray(counter.Handle, PDH_FMT.PDH_FMT_DOUBLE, &size, &count, (PDH_FMT_COUNTERVALUE_ITEM_W*)buffer);
                if (status == PInvoke.PDH_MORE_DATA) continue;
                if (status != 0) { error = PdhError(status, "读取PDH格式化数组失败"); return result; }
                if (count > 65_536 || (ulong)count * (uint)sizeof(PDH_FMT_COUNTERVALUE_ITEM_W) > size) { error = new("capacity", "PDH数组项数量超出有效缓冲。"); return result; }
                for (var index = 0; index < count; index++)
                {
                    var item = ((PDH_FMT_COUNTERVALUE_ITEM_W*)buffer)[index];
                    var name = item.szName.ToString();
                    var valid = item.FmtValue.CStatus is PInvoke.PDH_CSTATUS_VALID_DATA or PInvoke.PDH_CSTATUS_NEW_DATA && double.IsFinite(item.FmtValue.doubleValue) && item.FmtValue.doubleValue >= 0;
                    result[name] = valid ? new(item.FmtValue.doubleValue, null) : new(null, PdhError(item.FmtValue.CStatus, "PDH实例尚无有效增量"));
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        error = new("warmingUp", "PDH实例集合在读取时变化，等待下个样本。");
        return result;
    }

    private static Unavailable PdhError(uint status, string action) => new(status switch
        { PInvoke.PDH_NO_DATA or PInvoke.PDH_INVALID_DATA or PInvoke.PDH_CSTATUS_NO_INSTANCE or PInvoke.PDH_CSTATUS_INVALID_DATA
            or PInvoke.PDH_CALC_NEGATIVE_DENOMINATOR or PInvoke.PDH_CALC_NEGATIVE_TIMEBASE or PInvoke.PDH_CALC_NEGATIVE_VALUE => "warmingUp",
            PInvoke.PDH_ACCESS_DENIED => "accessDenied", PInvoke.PDH_CSTATUS_NO_COUNTER or PInvoke.PDH_CSTATUS_NO_OBJECT => "notSupported", _ => "error" },
        $"{action}（PDH 0x{status:X8}）。", unchecked((int)status));
    private sealed record Counter(PDH_HCOUNTER Handle, Unavailable? Error);
    private sealed record CounterValue(double? Value, Unavailable? Error);
    private sealed class PdhException(uint status, string action) : Exception(action) { internal Unavailable Reason { get; } = PdhError(status, action); }
    [GeneratedRegex(@"^pid_(\d+)_(luid_0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)_phys_(\d+)_(eng_\d+_engtype_.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex GpuInstance();

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        Task? worker; lock (_gate) worker = _worker;
        if (worker is not null) await worker;
        _stop.Dispose(); _signal.Dispose();
    }
}
