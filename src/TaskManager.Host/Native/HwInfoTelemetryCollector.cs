using System.Diagnostics;
using TaskManager.Host.Models;

namespace TaskManager.Host.Native;

/// <summary>Lazy serialized read worker. HTTP reads only immutable cache; map/mutex waits never hold the snapshot lock.</summary>
internal sealed class HwInfoTelemetryCollector(Func<SamplingSettings> settings) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private bool _paused;
    private HwInfoSensorsDto _current = HwInfoSensorsDto.WarmingUp;
    private DateTime? _lastPoll;
    private long _pollChangedAt;
    public HwInfoSensorsDto Current
    {
        get
        {
            var limit = TimeSpan.FromMilliseconds(Math.Max(30_000, settings().IntervalMs * 3));
            lock (_gate)
            {
                if (_current.Status != "available" || _current.PollTimeUtc is not { } poll) return _current;
                if (DateTime.UtcNow - poll <= limit && (_pollChangedAt == 0 || Stopwatch.GetElapsedTime(_pollChangedAt) <= limit)) return _current;
                return WithoutValues(_current, "stale", new("stale", "HWiNFO pollTime 已停止推进，当前读取值已过期。"));
            }
        }
    }
    public void Request()
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) return;
            _worker ??= Task.Run(RunAsync);
            if (_signal.CurrentCount == 0) _signal.Release();
        }
    }
    // Settings calls this while holding its own gate. Worker never acquires that gate while owning this one.
    public void SetPaused(bool value) { lock (_gate) _paused = value; }
    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await _signal.WaitAsync(_stop.Token);
                if (settings().Paused) continue;
                HwInfoSensorsDto? sample = null;
                Unavailable? error = null;
                try { sample = HwInfoSharedMemory.Read(); }
                catch (HwInfoException ex) { error = ex.Reason; }
                catch (Exception ex) { error = new("error", "HWiNFO共享内存读取失败：" + ex.Message); }
                // Check before taking the cache gate: settings owns the snapshot gate.
                if (settings().Paused || _stop.IsCancellationRequested) continue;
                lock (_gate)
                {
                    if (_paused || _stop.IsCancellationRequested) continue;
                    if (sample is not null)
                    {
                        if (_lastPoll != sample.PollTimeUtc) { _lastPoll = sample.PollTimeUtc; _pollChangedAt = Stopwatch.GetTimestamp(); }
                        _current = sample;
                    }
                    else _current = WithoutValues(_current, "unavailable", error!);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private static HwInfoSensorsDto WithoutValues(HwInfoSensorsDto source, string status, Unavailable reason) => source with
    {
        Status = status,
        Unavailable = new Dictionary<string, Unavailable> { ["source"] = reason },
        Sensors = source.Sensors.Select(sensor => sensor with { Readings = sensor.Readings.Select(reading => reading with
            { Value = null, Unavailable = new Dictionary<string, Unavailable>(reading.Unavailable) { ["value"] = reason } }).ToArray() }).ToArray()
    };
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        Task? worker; lock (_gate) worker = _worker;
        if (worker is not null) await worker;
        _stop.Dispose(); _signal.Dispose();
    }
}
