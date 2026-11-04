using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json;
using Takao.Host.Models;
using Takao.Host.Native;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
if (args.FirstOrDefault() == "--fixture") { await Fixture(args[1]); return; }
var output = args.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "taskmanager-network-results.json");
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
var temporary = Path.Combine(Path.GetTempPath(), "taskmanager-network-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
var children = new List<Process>();
var facts = new List<object>();
ProcessNetworkCollector? collector = null;
ProcessNetworkCollector? peerCollector = null;
try
{
    var payload = new byte[44]; BinaryPrimitives.WriteUInt32LittleEndian(payload, 123); BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 9876);
    foreach (var provider in new[] { NetworkEventParser.Tcp, NetworkEventParser.Udp })
        foreach (var opcode in new byte[] { 10, 11, 26, 27 })
        {
            Check(NetworkEventParser.TryParse(provider, 2, opcode, payload, 9000, out var transfer) && transfer.Pid == 123 && transfer.Size == 9876 && transfer.Receive == (opcode is 11 or 27), $"v2 {provider} opcode{opcode} reads payload PID and size");
            Check(!NetworkEventParser.TryParse(provider, 1, opcode, payload, 9000, out _), "old event layout is rejected");
            Check(!NetworkEventParser.TryParse(provider, 2, opcode, payload.AsSpan(0, opcode >= 26 ? 43 : 19), 9000, out _), "truncated transfer is rejected");
        }
    Check(!NetworkEventParser.TryParse(Guid.NewGuid(), 2, 10, payload, 9000, out _) && !NetworkEventParser.TryParse(NetworkEventParser.Tcp, 2, 14, payload, 9000, out _), "unrelated providers and retransmits are excluded");
    foreach (var (eventId, task, opcode) in new (ushort, ushort, byte)[]
        { (10, 10, 10), (11, 10, 11), (26, 10, 10), (27, 10, 11), (42, 11, 42), (43, 11, 43), (58, 11, 42), (59, 11, 43) })
    {
        var ipv6 = eventId is 26 or 27 or 58 or 59;
        Check(NetworkEventParser.TryParse(NetworkEventParser.KernelNetwork, 0, opcode, payload, 9000, out var transfer, eventId, task)
            && transfer.Pid == 123 && transfer.Size == 9876 && transfer.Receive == (eventId is 11 or 27 or 43 or 59),
            $"manifest v0 ID{eventId}/task{task}/op{opcode} reads payload identity, bytes and direction");
        Check(!NetworkEventParser.TryParse(NetworkEventParser.KernelNetwork, 2, opcode, payload, 9000, out _, eventId, task)
            && !NetworkEventParser.TryParse(NetworkEventParser.KernelNetwork, 0, opcode, payload, 9000, out _, eventId, 99)
            && !NetworkEventParser.TryParse(NetworkEventParser.KernelNetwork, 0, 14, payload, 9000, out _, eventId, task)
            && !NetworkEventParser.TryParse(NetworkEventParser.KernelNetwork, 0, opcode, payload.AsSpan(0, ipv6 ? 43 : 19), 9000, out _, eventId, task),
            "manifest mismatched version/task/opcode and truncated IP payloads are rejected");
    }
    var rows = new List<ProcessDto>();
    for (var index = 0; index < 2; index++)
    {
        var directory = Path.Combine(temporary, index.ToString()); Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--fixture"); start.ArgumentList.Add(directory);
        var child = Process.Start(start)!; children.Add(child);
        rows.Add(new() { Identity = $"{child.Id}:{child.StartTime.ToUniversalTime().ToFileTimeUtc()}", CreationTimeFileTime = child.StartTime.ToUniversalTime().ToFileTimeUtc().ToString(), Pid = (uint)child.Id, Name = "Dedicated network test fixture" });
    }
    var settings = new SamplingSettings();
    collector = new(() => settings);
    var target = rows[0];
    var endpointsFile = Path.Combine(temporary, "0", "endpoints.txt");
    await Wait(() => File.Exists(endpointsFile), "fixture endpoints");
    var endpoints = File.ReadAllLines(endpointsFile).Select(line => line.Split('=')).ToDictionary(parts => parts[0], parts => int.Parse(parts[1]));
    for (var i = 0; i < 12; i++) { collector.Request(rows); await Task.Delay(1200); if (collector.Current.Status.Status == "available") break; }
    var sample = collector.Current;
    Check(sample.Status.Status == "available", "real ETW session becomes available: " + JsonConvert.SerializeObject(sample.Status));
    Check(sample.Status.Source is "classicSystemLogger" or "kernelNetworkManifest", "actual ETW provider mode is exposed, including system-logger capacity fallback");
    facts.Add(new { source = sample.Status.Source });
    Check(sample.Processes[target.Identity].Receive == 0 && sample.Processes[target.Identity].Send == 0, "complete quiet windows report genuine zero");
    foreach (var protocol in new[] { "TCP4", "UDP4", "TCP6", "UDP6" })
    {
        if (endpoints[protocol] == 0) { facts.Add(new { protocol, result = "NOT_EXECUTED", reason = "IPv6 unavailable" }); continue; }
        using var stopTraffic = new CancellationTokenSource();
        long sent = 0, received = 0;
        var traffic = Transfer(protocol, endpoints[protocol], stopTraffic.Token, count => Interlocked.Add(ref sent, count), count => Interlocked.Add(ref received, count));
        ProcessNetworkSample? observed = null;
        for (var i = 0; i < 12; i++)
        {
            collector.Request(rows); await Task.Delay(1100); observed = collector.Current;
            if (observed.Status.Status == "available" && observed.Processes.TryGetValue(target.Identity, out var rate) && rate.Receive > 1000 && rate.Send > 1000) break;
        }
        stopTraffic.Cancel(); await traffic;
        var stoppedAt = DateTime.UtcNow;
        var reading = observed!.Processes.GetValueOrDefault(target.Identity);
        Check(reading?.Receive > 1000 && reading.Send > 1000, protocol + " real fixture receives and sends through production ETW: " + JsonConvert.SerializeObject(observed.Status));
        Check(sent > 0 && received > 0, protocol + " independent socket transfer counters are positive");
        Check(observed.Processes[rows[1].Identity].Receive == 0 && observed.Processes[rows[1].Identity].Send == 0, protocol + " idle identity does not inherit traffic");
        facts.Add(new { protocol, result = "PASS", sent, received, receiveRate = reading!.Receive, sendRate = reading.Send, observed.Status.WindowDurationMs });
        for (var i = 0; i < 20; i++)
        {
            collector.Request(rows); await Task.Delay(1100);
            var quiet = collector.Current;
            if (quiet.Status.Status == "available" && quiet.Status.CapturedAtUtc?.AddMilliseconds(-(quiet.Status.WindowDurationMs ?? 0)) > stoppedAt && quiet.Processes[target.Identity].Receive == 0 && quiet.Processes[target.Identity].Send == 0) break;
        }
        Check(collector.Current.Status.Status == "available" && collector.Current.Processes[target.Identity].Receive == 0 && collector.Current.Processes[target.Identity].Send == 0, protocol + " stopped transfer decays to complete quiet zero: " + JsonConvert.SerializeObject(collector.Current.Status));
    }
    sample = collector.Current;
    facts.Add(new { layouts = sample.Status.EventLayouts, sample.Status.ConsumedBuffers, sample.Status.FlushedBuffers });
    Check(sample.Status.FlushedBuffers > 0 && sample.Status.ConsumedBuffers > 0, "real buffers flow and the same consumer acknowledges each unique ETW marker");
    // A second live subscriber must neither starve this session nor disable it
    // when the peer stops. Both consumers observe the same real dedicated TCP flow.
    peerCollector = new(() => settings);
    using (var concurrentStop = new CancellationTokenSource())
    {
        var concurrentTraffic = Transfer("TCP4", endpoints["TCP4"], concurrentStop.Token, _ => { }, _ => { });
        try
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                collector.Request(rows); peerCollector.Request(rows); await Task.Delay(1100);
                if (collector.Current.Status.Status == "available" && peerCollector.Current.Status.Status == "available" &&
                    collector.Current.Processes.GetValueOrDefault(target.Identity)?.Receive > 1000 &&
                    peerCollector.Current.Processes.GetValueOrDefault(target.Identity)?.Receive > 1000) break;
            }
            var primaryWindow = collector.Current; var peerWindow = peerCollector.Current;
            facts.Add(new { concurrentPrimary = primaryWindow.Status, concurrentPeer = peerWindow.Status,
                primaryReading = primaryWindow.Processes.GetValueOrDefault(target.Identity),
                peerReading = peerWindow.Processes.GetValueOrDefault(target.Identity) });
            Check(primaryWindow.Status.Status == "available" && peerWindow.Status.Status == "available" &&
                primaryWindow.Processes.GetValueOrDefault(target.Identity) is { Receive: > 1000, Send: > 1000 } &&
                peerWindow.Processes.GetValueOrDefault(target.Identity) is { Receive: > 1000, Send: > 1000 },
                "two independently owned live sessions both receive the dedicated socket traffic");
            Check(primaryWindow.Status.EventsLost == 0 && primaryWindow.Status.BuffersLost == 0 &&
                peerWindow.Status.EventsLost == 0 && peerWindow.Status.BuffersLost == 0,
                "concurrent provider subscribers retain complete windows without event or buffer loss");
            var peerStoppedAt = DateTime.UtcNow;
            await peerCollector.DisposeAsync(); peerCollector = null;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                collector.Request(rows); await Task.Delay(1100);
                if (collector.Current.Status.CapturedAtUtc > peerStoppedAt && collector.Current.Status.Status == "available" &&
                    collector.Current.Processes.GetValueOrDefault(target.Identity)?.Receive > 1000) break;
            }
            Check(collector.Current.Status.CapturedAtUtc > peerStoppedAt && collector.Current.Status.Status == "available" &&
                collector.Current.Processes.GetValueOrDefault(target.Identity) is { Receive: > 1000, Send: > 1000 } &&
                collector.Current.Status.EventsLost == 0 && collector.Current.Status.BuffersLost == 0,
                "stopping the exact peer session preserves positive traffic in the surviving independent session");
            facts.Add(new { survivingSession = collector.Current.Status });
        }
        finally { concurrentStop.Cancel(); await concurrentTraffic; }
    }
    collector.SetPaused(true); settings = settings with { Paused = true };
    var paused = collector.Current; await Task.Delay(1200);
    Check(collector.Current == paused && paused.Status.Status == "paused", "pause publishes no new windows");
    collector.SetPaused(false); settings = settings with { Paused = false };
    for (var i = 0; i < 8; i++) { collector.Request(rows); await Task.Delay(1100); if (collector.Current.Status.CapturedAtUtc is not null) break; }
    Check(collector.Current.Status.Status == "warmingUp" && collector.Current.Processes.Values.All(rate => rate.Receive is null && rate.Send is null), "resume discards paused bytes and warms a new complete window");
    for (var i = 0; i < 8; i++) { collector.Request(rows); await Task.Delay(1100); if (collector.Current.Status.Status == "available") break; }
    Check(collector.Current.Status.Status == "available", "fresh window recovers after pause");
    var changed = new ProcessDto { Identity = target.Pid + ":1", Pid = target.Pid, CreationTimeFileTime = "1", Name = "isolated identity guard input" };
    for (var i = 0; i < 8; i++) { collector.Request([changed, rows[1]]); await Task.Delay(1100); if (collector.Current.Processes.ContainsKey(changed.Identity)) break; }
    Check(collector.Current.Processes[changed.Identity].Receive is null, "a changed creation identity never inherits prior PID totals");
    var missing = new ProcessDto { Identity = "1:unknown", Pid = 1, Name = "isolated missing identity input" };
    ProcessNetworkCollector.Apply([missing], collector.Current, 1000);
    Check(missing.NetworkBytesPerSecond is null && missing.Unavailable["networkBytesPerSecond"].Code == "identityUnavailable", "unverified creation identity remains unknown");
    var stale = new ProcessDto { Identity = target.Identity, Pid = target.Pid, CreationTimeFileTime = target.CreationTimeFileTime, Name = target.Name, NetworkReceiveBytesPerSecond = 100, NetworkSendBytesPerSecond = 100 };
    ProcessNetworkCollector.Apply([stale], collector.Current with { Status = collector.Current.Status with { CapturedAtUtc = DateTime.UtcNow.AddMinutes(-1) } }, 1000);
    Check(stale.NetworkBytesPerSecond is null && stale.Unavailable["networkBytesPerSecond"].Code == "stale", "expired source clears old nonzero values instead of relabeling them as current");
    await collector.DisposeAsync(); collector = null;
    // A second independent collector proves releasing our session permits a new one.
    collector = new(() => settings);
    for (var i = 0; i < 10; i++) { collector.Request(rows); await Task.Delay(1100); if (collector.Current.Status.Status == "available") break; }
    Check(collector.Current.Status.Status == "available", "an independently created ETW session works after disposal/restart");
    await collector.DisposeAsync(); collector = null;
    var report = new { result = "PASS", assertions = checks.Count, checks, facts, checkedAtUtc = DateTime.UtcNow };
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, JsonConvert.SerializeObject(report, Formatting.Indented));
    Console.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
}
catch (Exception error)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, JsonConvert.SerializeObject(new { result = "FAIL", checks, facts, message = error.ToString() }, Formatting.Indented));
    throw;
}
finally
{
    if (peerCollector is not null) await peerCollector.DisposeAsync();
    if (collector is not null) await collector.DisposeAsync();
    foreach (var child in children) { if (!child.HasExited) child.Kill(); child.Dispose(); }
    Directory.Delete(temporary, true);
}

static async Task Wait(Func<bool> condition, string name)
{
    var started = Stopwatch.StartNew();
    while (!condition()) { if (started.Elapsed.TotalSeconds > 15) throw new InvalidOperationException("Timeout: " + name); await Task.Delay(50); }
}
static async Task Transfer(string protocol, int port, CancellationToken cancellationToken, Action<int> sent, Action<int> received)
{
    var address = protocol.EndsWith('6') ? IPAddress.IPv6Loopback : IPAddress.Loopback;
    var bytes = new byte[protocol.StartsWith("TCP") ? 16384 : 1024];
    try
    {
        if (protocol.StartsWith("TCP"))
        {
            using var client = new TcpClient(address.AddressFamily); await client.ConnectAsync(address, port, cancellationToken);
            var stream = client.GetStream();
            while (!cancellationToken.IsCancellationRequested)
            {
                await stream.WriteAsync(bytes, cancellationToken); sent(bytes.Length);
                var count = 0;
                while (count < bytes.Length) { var n = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken); if (n == 0) return; count += n; received(n); }
                await Task.Delay(10, cancellationToken);
            }
        }
        else
        {
            using var client = new UdpClient(address.AddressFamily); client.Connect(address, port);
            while (!cancellationToken.IsCancellationRequested)
            {
                await client.SendAsync(bytes, cancellationToken); sent(bytes.Length);
                var reply = await client.ReceiveAsync(cancellationToken); received(reply.Buffer.Length);
                await Task.Delay(10, cancellationToken);
            }
        }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
}
static async Task Fixture(string directory)
{
    Directory.CreateDirectory(directory);
    using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    var endpoints = new List<string>(); var tasks = new List<Task>(); var owned = new List<IDisposable>();
    foreach (var pair in new[] { (IPAddress.Loopback, "4"), (IPAddress.IPv6Loopback, "6") })
    {
        if (pair.Item2 == "6" && !Socket.OSSupportsIPv6) { endpoints.Add("TCP6=0"); endpoints.Add("UDP6=0"); continue; }
        var listener = new TcpListener(pair.Item1, 0); listener.Start(); owned.Add(listener);
        endpoints.Add($"TCP{pair.Item2}={((IPEndPoint)listener.LocalEndpoint).Port}");
        tasks.Add(Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token); var stream = client.GetStream(); var bytes = new byte[16384];
                while (!stop.IsCancellationRequested) { var count = await stream.ReadAsync(bytes, stop.Token); if (count == 0) break; await stream.WriteAsync(bytes.AsMemory(0, count), stop.Token); }
            }
        }));
        var udp = new UdpClient(new IPEndPoint(pair.Item1, 0)); owned.Add(udp);
        endpoints.Add($"UDP{pair.Item2}={((IPEndPoint)udp.Client.LocalEndPoint!).Port}");
        tasks.Add(Task.Run(async () => { while (!stop.IsCancellationRequested) { var packet = await udp.ReceiveAsync(stop.Token); await udp.SendAsync(packet.Buffer, packet.RemoteEndPoint, stop.Token); } }));
    }
    var endpointFile = Path.Combine(directory, "endpoints.txt");
    var endpointTemporary = endpointFile + ".tmp";
    await File.WriteAllLinesAsync(endpointTemporary, endpoints);
    File.Move(endpointTemporary, endpointFile);
    try { await Task.WhenAll(tasks); } catch (OperationCanceledException) { }
    finally { foreach (var item in owned) item.Dispose(); }
}
