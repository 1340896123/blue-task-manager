using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TaskManager.Host.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Diagnostics.Etw;
using Windows.Win32.System.Threading;

namespace TaskManager.Host.Native;

internal sealed record ProcessNetworkReading(double? Receive, double? Send, Unavailable? Unavailable);
internal sealed record ProcessNetworkSample(ProcessNetworkStatusDto Status, IReadOnlyDictionary<string, ProcessNetworkReading> Processes);
internal readonly record struct NetworkTransfer(uint Pid, uint Size, bool Receive, long FileTime);

/// <summary>Only the documented classic TCP/UDP transfer events; never packet contents or endpoint data.</summary>
internal static class NetworkEventParser
{
    internal static readonly Guid Tcp = new("9a280ac0-c8e0-11d1-84e2-00c04fb998a2");
    internal static readonly Guid Udp = new("bf3a50c5-a9c9-4988-a005-2df0b7c80f80");
    internal static bool IsTransfer(Guid provider, byte opcode) => (provider == Tcp || provider == Udp) && opcode is 10 or 11 or 26 or 27;

    internal static bool TryParse(Guid provider, byte version, byte opcode, ReadOnlySpan<byte> payload, long fileTime, out NetworkTransfer transfer)
    {
        transfer = default;
        if (!IsTransfer(provider, opcode)) return false;
        // Windows 10/11 uses version 2. Older v0/v1 reorder PID/size and must not be guessed.
        // The shared v2 prefix is followed by 2 addresses and 2 ports (20 / 44 bytes).
        if (version != 2 || payload.Length < (opcode is 26 or 27 ? 44 : 20) || fileTime <= 0) return false;
        transfer = new(BinaryPrimitives.ReadUInt32LittleEndian(payload), BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]), opcode is 11 or 27, fileTime);
        return true;
    }
}

/// <summary>Deferred ETW controller + one native consumer. Native waits never hold the snapshot/settings lock.</summary>
internal sealed class ProcessNetworkCollector : IAsyncDisposable
{
    private const int MaxIdentities = 20_000;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<SamplingSettings> _settings;
    private Task? _worker;
    private RequestState? _pending;
    private ProcessNetworkSample _current = new(ProcessNetworkStatusDto.WarmingUp, new Dictionary<string, ProcessNetworkReading>());
    private long _epoch;
    private bool _paused;
    private sealed record Identity(string Key, long Created, long Observed);
    private sealed record RequestState(Dictionary<uint, Identity> Identities, bool Capacity, long Epoch);
    private Dictionary<uint, Identity> _identities = new();
    private EtwNetworkSession? _session;

    internal ProcessNetworkCollector(Func<SamplingSettings> settings) => _settings = settings;
    internal ProcessNetworkSample Current { get { lock (_gate) return _current; } }
    internal void SetPaused(bool paused)
    {
        lock (_gate)
        {
            if (_paused == paused) return;
            _paused = paused;
            _epoch++;
            _pending = null;
            _current = new(ProcessNetworkStatusDto.WarmingUp with { Status = paused ? "paused" : "warmingUp", Unavailable = new Dictionary<string, Unavailable>
                { ["source"] = paused ? new("paused", "网络采集已暂停，恢复后重新预热完整窗口。") : NativeSupport.WarmingUp } }, new Dictionary<string, ProcessNetworkReading>());
        }
    }
    internal void Request(IReadOnlyList<ProcessDto> processes)
    {
        var now = DateTime.UtcNow.ToFileTimeUtc();
        var identities = new Dictionary<uint, Identity>();
        foreach (var row in processes.Take(MaxIdentities))
            if (row.CreationTimeFileTime is { } raw && long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var created))
                identities[row.Pid] = new(row.Identity, created, now);
        lock (_gate)
        {
            if (_stop.IsCancellationRequested || _paused) return;
            _pending = new(identities, processes.Count > MaxIdentities, _epoch);
            _worker ??= Task.Run(RunAsync);
            if (_signal.CurrentCount == 0) _signal.Release();
        }
    }

    private async Task RunAsync()
    {
        long lastFileTime = 0, lastTick = 0, epoch = -1;
        uint lastEventsLost = 0, lastBuffersLost = 0;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await _signal.WaitAsync(_stop.Token);
                RequestState? requested;
                lock (_gate) { requested = _pending; _pending = null; }
                if (requested is null || _settings().Paused) continue;
                var started = Stopwatch.GetTimestamp();
                try
                {
                    _session ??= new EtwNetworkSession();
                    var boundary = DateTime.UtcNow.ToFileTimeUtc();
                    var tick = Stopwatch.GetTimestamp();
                    var flushed = await _session.FlushAsync(_stop.Token);
                    if (flushed.MarkerFileTime > 0) boundary = flushed.MarkerFileTime;
                    var seconds = lastTick == 0 ? 0 : Stopwatch.GetElapsedTime(lastTick, tick).TotalSeconds;
                    var clockStable = lastFileTime == 0 || Math.Abs((boundary - lastFileTime) / 10_000_000d - seconds) < .5;
                    var reset = epoch != requested.Epoch || lastTick == 0 || !clockStable;
                    var reason = requested.Capacity ? new Unavailable("capacity", "进程身份数超过20000项上限，网络窗口不完整。")
                        : flushed.Reason ?? (flushed.EventsLost != lastEventsLost || flushed.BuffersLost != lastBuffersLost
                            ? new("eventsLost", "ETW发生事件或缓冲丢失，此窗口不可用，下一完整窗口重新计算。") : null);
                    var transfers = _session.TakeBefore(boundary, reset ? boundary : lastFileTime, out var queueReason);
                    reason ??= queueReason;
                    var rates = new Dictionary<string, ProcessNetworkReading>();
                    var nextIdentities = new Dictionary<uint, Identity>();
                    foreach (var (pid, current) in requested.Identities)
                    {
                        var continuous = _identities.TryGetValue(pid, out var previous) && previous.Key == current.Key && previous.Observed <= lastFileTime;
                        var identity = continuous ? previous! : current;
                        nextIdentities[pid] = identity;
                        var missing = reason ?? (reset || seconds <= 0 || !continuous || identity.Created > lastFileTime ? NativeSupport.WarmingUp : null);
                        if (missing is not null) rates[current.Key] = new(null, null, missing);
                        else
                        {
                            transfers.TryGetValue(pid, out var total);
                            rates[current.Key] = new(total.Receive / seconds, total.Send / seconds, null);
                        }
                    }
                    _identities = nextIdentities;
                    var statusReason = reason ?? (reset ? NativeSupport.WarmingUp : null);
                    var status = new ProcessNetworkStatusDto(DateTime.FromFileTimeUtc(boundary), statusReason is null ? "available" : statusReason.Code,
                        flushed.EventsLost, flushed.BuffersLost, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        statusReason is null ? new Dictionary<string, Unavailable>() : new Dictionary<string, Unavailable> { ["source"] = statusReason })
                    { WindowDurationMs = seconds > 0 ? seconds * 1000 : null, ConsumedBuffers = flushed.Consumed, FlushedBuffers = flushed.Written, EventLayouts = _session.Layouts };
                    lock (_gate) if (!_paused && requested.Epoch == _epoch) _current = new(status, rates);
                    lastFileTime = boundary; lastTick = tick; epoch = requested.Epoch;
                    lastEventsLost = flushed.EventsLost; lastBuffersLost = flushed.BuffersLost;
                    // Invalid windows establish a fresh baseline. They are never accumulated into a short later window.
                    if (reason is not null) { _identities.Clear(); }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    var reason = exception is EtwNetworkException native ? native.Reason : new Unavailable("error", "Windows ETW网络读取失败：" + exception.Message);
                    lock (_gate) if (!_paused && requested.Epoch == _epoch) _current = new(new(DateTime.UtcNow, reason.Code, 0, 0,
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds, new Dictionary<string, Unavailable> { ["source"] = reason }), new Dictionary<string, ProcessNetworkReading>());
                    if (_session is not null) { await _session.DisposeAsync(); _session = null; }
                    _identities.Clear(); lastFileTime = lastTick = 0;
                    // Request-driven retries have bounded frequency; no native retry spin.
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { if (_session is not null) await _session.DisposeAsync(); _session = null; }
    }

    internal static ProcessNetworkStatusDto Apply(IReadOnlyList<ProcessDto> processes, ProcessNetworkSample sample, int intervalMs)
    {
        var stale = sample.Status.CapturedAtUtc is { } captured && DateTime.UtcNow - captured > TimeSpan.FromMilliseconds(Math.Max(5000, intervalMs * 3));
        var status = stale ? sample.Status with { Status = "stale", Unavailable = new Dictionary<string, Unavailable> { ["source"] = new("stale", "ETW网络最新窗口已过期，等待后台采集恢复。") } } : sample.Status;
        foreach (var process in processes)
        {
            process.NetworkReceiveBytesPerSecond = process.NetworkSendBytesPerSecond = null;
            process.NetworkCapturedAtUtc = status.CapturedAtUtc;
            var reason = status.Unavailable.GetValueOrDefault("source");
            if (process.CreationTimeFileTime is null) reason = new("identityUnavailable", "创建时间不可读，不能将ETW的PID流量安全关联至此进程身份。");
            if (reason is null && sample.Processes.TryGetValue(process.Identity, out var reading))
            {
                process.NetworkReceiveBytesPerSecond = reading.Receive; process.NetworkSendBytesPerSecond = reading.Send;
                reason = reading.Unavailable;
            }
            else reason ??= NativeSupport.WarmingUp;
            foreach (var field in new[] { "networkReceiveBytesPerSecond", "networkSendBytesPerSecond", "networkBytesPerSecond" })
                if (reason is null) process.Unavailable.Remove(field); else process.Unavailable[field] = reason;
        }
        return status;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_worker is not null) await _worker;
        _signal.Dispose(); _stop.Dispose();
    }
}

internal sealed class EtwNetworkException(Unavailable reason) : Exception(reason.Message)
{
    internal Unavailable Reason { get; } = reason;
}

/// <summary>Owns exactly one uniquely named Windows system logger. Never adopts an existing logger.</summary>
internal sealed class EtwNetworkSession : IAsyncDisposable
{
    private const int MaxEvents = 262_144;
    private readonly object _gate = new();
    private readonly List<NetworkTransfer> _events = new();
    private readonly HashSet<string> _layouts = new(StringComparer.Ordinal);
    private Unavailable? _windowReason;
    private Unavailable? _consumerError;
    private readonly Guid _markerProvider = Guid.NewGuid();
    private REGHANDLE _markerHandle;
    private ulong _markerSequence;
    private TaskCompletionSource<long>? _markerCompletion;
    private long _closedBoundary;
    private readonly string _name = $"BlueDomain-Network-{Environment.ProcessId}-{OwnCreationTime()}-{Guid.NewGuid():N}";
    private readonly PEVENT_TRACE_BUFFER_CALLBACKW _bufferCallback;
    private GCHandle _context;
    private unsafe EVENT_TRACE_PROPERTIES* _properties;
    private nint _loggerName;
    private ulong _controller;
    private PROCESSTRACE_HANDLE _consumer = (PROCESSTRACE_HANDLE)ulong.MaxValue;
    private Task? _consume;
    private volatile uint _consumed;
    private volatile bool _stopping;
    internal IReadOnlyList<string> Layouts { get { lock (_gate) return _layouts.Order(StringComparer.Ordinal).ToArray(); } }

    internal unsafe EtwNetworkSession()
    {
        _bufferCallback = Buffer;
        try
        {
            EnableProfilePrivilege();
            var bytes = (uint)(sizeof(EVENT_TRACE_PROPERTIES) + 2048);
            _properties = (EVENT_TRACE_PROPERTIES*)NativeMemory.AllocZeroed(bytes);
            _properties->Wnode.BufferSize = bytes;
            _properties->Wnode.Guid = Guid.NewGuid();
            _properties->Wnode.ClientContext = 1; // QPC clock; ProcessTrace converts event timestamps to FILETIME.
            _properties->Wnode.Flags = PInvoke.WNODE_FLAG_TRACED_GUID;
            _properties->BufferSize = 64;
            _properties->MinimumBuffers = (uint)Math.Clamp(Environment.ProcessorCount * 2, 8, 128);
            _properties->MaximumBuffers = 256;
            _properties->LogFileMode = PInvoke.EVENT_TRACE_REAL_TIME_MODE | PInvoke.EVENT_TRACE_SYSTEM_LOGGER_MODE;
            _properties->EnableFlags = EVENT_TRACE_FLAG.EVENT_TRACE_FLAG_NETWORK_TCPIP;
            _properties->FlushTimer = 1;
            _properties->LoggerNameOffset = (uint)sizeof(EVENT_TRACE_PROPERTIES);
            var error = PInvoke.StartTrace(out var controller, _name, ref *_properties);
            if (error != 0) throw Error(error, "创建独立ETW网络会话失败");
            _controller = controller; // Only a successful StartTrace grants ownership.
            var registration = PInvoke.EventRegister(_markerProvider, null, null, out _markerHandle);
            if (registration != 0) throw Error((WIN32_ERROR)registration, "注册ETW内部采样边界失败");
            error = PInvoke.EnableTraceEx2(_controller, _markerProvider, (uint)ENABLECALLBACK_ENABLED_STATE.EVENT_CONTROL_CODE_ENABLE_PROVIDER, 0, 0, 0, 1000, null);
            if (error != 0) throw Error(error, "启用ETW内部采样边界失败");
            _context = GCHandle.Alloc(this);
            _loggerName = Marshal.StringToHGlobalUni(_name);
            var log = new EVENT_TRACE_LOGFILEW { LoggerName = new PWSTR((char*)_loggerName), BufferCallback = _bufferCallback,
                ProcessTraceMode = PInvoke.PROCESS_TRACE_MODE_REAL_TIME | PInvoke.PROCESS_TRACE_MODE_EVENT_RECORD,
                Context = (void*)GCHandle.ToIntPtr(_context) };
            log.Anonymous2.EventRecordCallback = &Event;
            _consumer = PInvoke.OpenTrace(ref log);
            if (_consumer.Value == ulong.MaxValue) throw Error((WIN32_ERROR)Marshal.GetLastPInvokeError(), "打开ETW网络消费者失败");
            _consume = Task.Factory.StartNew(Consume, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch { try { StopNative(); } finally { ReleaseNative(); } throw; }
    }

    private static string OwnCreationTime()
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)Environment.ProcessId);
        if (process.IsInvalid || !PInvoke.GetProcessTimes(process, out var created, out _, out _, out _)) throw new EtwNetworkException(NativeSupport.LastError("读取本实例ETW所有者身份失败"));
        return NativeSupport.FileTime(created).ToString(CultureInfo.InvariantCulture);
    }

    private static unsafe void EnableProfilePrivilege()
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)Environment.ProcessId);
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token)) throw Error((WIN32_ERROR)Marshal.GetLastPInvokeError(), "打开ETW权限令牌失败");
        using (token)
        {
            if (!PInvoke.LookupPrivilegeValue(null, "SeSystemProfilePrivilege", out var luid)) throw Error((WIN32_ERROR)Marshal.GetLastPInvokeError(), "读取ETW采集权限失败");
            var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1 };
            privileges.Privileges[0] = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED };
            if (!PInvoke.AdjustTokenPrivileges(token, false, &privileges, default)) throw Error((WIN32_ERROR)Marshal.GetLastPInvokeError(), "启用已有ETW采集权限失败");
            var error = Marshal.GetLastPInvokeError();
            if (error != 0) throw Error((WIN32_ERROR)error, "当前令牌未持有ETW采集权限");
        }
    }

    private unsafe void Consume()
    {
        var handle = _consumer;
        var error = PInvoke.ProcessTrace(&handle, 1, null, null);
        if (!_stopping) lock (_gate) _consumerError = error == 0 ? new("error", "ETW网络事件流意外结束。") : Error(error, "ETW网络消费失败").Reason;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe void Event(EVENT_RECORD* record)
    {
        EtwNetworkSession? session = null;
        try
        {
            if (record == null || record->UserContext == null) return;
            session = (EtwNetworkSession)GCHandle.FromIntPtr((nint)record->UserContext).Target!;
            var header = record->EventHeader;
            if (header.ProviderId == session._markerProvider && header.ProcessId == Environment.ProcessId)
            {
                // One standard EventWriteString marker per window, consumed by this same ProcessTrace.
                if (record->UserData == null || record->UserDataLength is < 4 or > 64) return;
                var marker = new string((char*)record->UserData, 0, record->UserDataLength / 2).TrimEnd('\0');
                lock (session._gate)
                    if (ulong.TryParse(marker, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) && sequence == session._markerSequence)
                        session._markerCompletion?.TrySetResult(header.TimeStamp);
                return;
            }
            if (!NetworkEventParser.IsTransfer(header.ProviderId, header.EventDescriptor.Opcode)) return;
            var payload = record->UserData == null ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(record->UserData, record->UserDataLength);
            lock (session._gate)
            {
                if (session._layouts.Count < 16) session._layouts.Add($"{(header.ProviderId == NetworkEventParser.Tcp ? "TCP" : "UDP")}/v{header.EventDescriptor.Version}/op{header.EventDescriptor.Opcode}/{record->UserDataLength}B");
                if (!NetworkEventParser.TryParse(header.ProviderId, header.EventDescriptor.Version, header.EventDescriptor.Opcode, payload, header.TimeStamp, out var transfer))
                    session._windowReason = new("notSupported", "ETW网络事件版本或长度不受支持，未猜测PID或字节数。");
                else if (transfer.FileTime < session._closedBoundary) session._windowReason = new("eventsLost", "ETW出现晚于完整窗口送达的旧事件，当前窗口重新预热。");
                else if (session._events.Count >= MaxEvents) session._windowReason = new("capacity", "ETW网络窗口超过262144个事件上限，当前窗口不可用。");
                else session._events.Add(transfer);
            }
        }
        catch { if (session is not null) lock (session._gate) session._windowReason = new("error", "ETW事件解析失败，此窗口不可用。"); }
    }
    private uint Buffer(ref EVENT_TRACE_LOGFILEW log)
    {
        _consumed = log.BuffersRead;
        return _stopping ? 0u : 1u;
    }

    internal sealed record FlushResult(uint EventsLost, uint BuffersLost, uint Written, uint Consumed, long MarkerFileTime, Unavailable? Reason);
    internal async Task<FlushResult> FlushAsync(CancellationToken cancellationToken)
    {
        lock (_gate) if (_consumerError is { } stopped) throw new EtwNetworkException(stopped);
        TaskCompletionSource<long> completion;
        string marker;
        lock (_gate)
        {
            marker = (++_markerSequence).ToString(CultureInfo.InvariantCulture);
            completion = _markerCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var markerError = PInvoke.EventWriteString(_markerHandle, 0, 0, marker);
        if (markerError != 0) throw Error((WIN32_ERROR)markerError, "写入ETW内部采样边界失败");
        // FLUSH only drains producers. The marker acknowledges chronological delivery by this consumer.
        // BuffersWritten and BuffersRead have different accounting on real Windows and are diagnostics only.
        var error = FlushNative(out var eventsLost, out var buffersLost, out var written);
        if (error != 0) throw Error(error, "刷新ETW网络缓冲失败");
        long fileTime = 0;
        try { fileTime = await completion.Task.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken); }
        catch (TimeoutException) { }
        lock (_gate) if (_consumerError is { } stopped) throw new EtwNetworkException(stopped);
        if (_consume!.IsCompleted) throw new EtwNetworkException(new("error", "ETW消费者已停止，不能将缺失事件当成零流量。"));
        var reason = fileTime == 0 ? new Unavailable("stale", "ETW采样边界尚未消费，当前窗口不可用。") : null;
        return new(eventsLost, buffersLost, written, _consumed, fileTime, reason);
    }
    private unsafe WIN32_ERROR FlushNative(out uint eventsLost, out uint buffersLost, out uint written)
    {
        var error = PInvoke.ControlTrace(_controller, null, _properties, EVENT_TRACE_CONTROL.EVENT_TRACE_CONTROL_FLUSH);
        eventsLost = _properties->EventsLost; buffersLost = _properties->LogBuffersLost + _properties->RealTimeBuffersLost; written = _properties->BuffersWritten;
        return error;
    }

    internal Dictionary<uint, (ulong Receive, ulong Send)> TakeBefore(long end, long start, out Unavailable? reason)
    {
        var result = new Dictionary<uint, (ulong Receive, ulong Send)>();
        lock (_gate)
        {
            reason = _windowReason; _windowReason = null;
            var retained = 0;
            for (var index = 0; index < _events.Count; index++)
            {
                var transfer = _events[index];
                if (transfer.FileTime >= end) { _events[retained++] = transfer; continue; }
                if (transfer.FileTime < start) continue;
                result.TryGetValue(transfer.Pid, out var total);
                try { result[transfer.Pid] = transfer.Receive ? (checked(total.Receive + transfer.Size), total.Send) : (total.Receive, checked(total.Send + transfer.Size)); }
                catch (OverflowException) { reason = new("capacity", "ETW网络字节累计溢出，此窗口不可用。"); }
            }
            _events.RemoveRange(retained, _events.Count - retained);
            _closedBoundary = end;
        }
        return result;
    }

    private static EtwNetworkException Error(WIN32_ERROR error, string action)
    {
        var reason = NativeSupport.Error((int)error, action);
        if ((uint)error == 1450) reason = reason with { Code = "capacity", Message = "Windows系统ETW会话/缓冲资源不足；没有停止其他采集器。" };
        if ((uint)error == 1314 || (uint)error == 1300) reason = reason with { Code = "accessDenied" };
        return new(reason);
    }
    private unsafe Unavailable? StopNative()
    {
        Unavailable? reason = null;
        _stopping = true;
        if (_controller != 0)
        {
            var error = PInvoke.ControlTrace(_controller, null, _properties, EVENT_TRACE_CONTROL.EVENT_TRACE_CONTROL_STOP);
            if (error != 0 && (uint)error is not (4201 or 1168)) error = PInvoke.ControlTrace(_controller, null, _properties, EVENT_TRACE_CONTROL.EVENT_TRACE_CONTROL_STOP);
            if (error == 0 || (uint)error is 4201 or 1168) _controller = 0;
            else reason = Error(error, "停止本实例ETW会话失败").Reason;
        }
        if (_consumer.Value != ulong.MaxValue)
        {
            var error = PInvoke.CloseTrace(_consumer);
            if (error == 0 || (uint)error is 7007 or 6) _consumer = (PROCESSTRACE_HANDLE)ulong.MaxValue;
            else reason ??= Error(error, "关闭本实例ETW消费者失败").Reason;
        }
        if (_markerHandle.Value != 0) { PInvoke.EventUnregister(_markerHandle); _markerHandle = default; }
        return reason;
    }
    private unsafe void ReleaseNative()
    {
        if (_context.IsAllocated) _context.Free();
        if (_loggerName != 0) { Marshal.FreeHGlobal(_loggerName); _loggerName = 0; }
        if (_properties != null) { NativeMemory.Free(_properties); _properties = null; }
        GC.KeepAlive(_bufferCallback);
    }
    public ValueTask DisposeAsync()
    {
        var reason = StopNative();
        return new ValueTask(FinishDisposeAsync(reason));
    }
    private async Task FinishDisposeAsync(Unavailable? reason)
    {
        try { if (_consume is not null) await _consume; }
        finally { ReleaseNative(); }
        if (reason is not null) throw new EtwNetworkException(reason);
    }
}
