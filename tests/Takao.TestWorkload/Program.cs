using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

// This is a real, bounded integration-test target. Production never invokes it or uses its output as telemetry.
var directory = args.Length > 0 ? Path.GetFullPath(args[0]) : throw new ArgumentException("A test-owned temporary directory is required.");
var seconds = args.Length > 1 && int.TryParse(args[1], out var duration) ? Math.Clamp(duration, 5, 180) : 90;
Directory.CreateDirectory(directory);
using var tcp4 = new TcpListener(IPAddress.Loopback, 0);
tcp4.Start();
using var udp4 = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
using var tcp6 = Socket.OSSupportsIPv6 ? new TcpListener(IPAddress.IPv6Loopback, 0) : null;
tcp6?.Start();
using var udp6 = Socket.OSSupportsIPv6 ? new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, 0)) : null;
File.WriteAllLines(Path.Combine(directory, "endpoints.txt"), new[] { $"TCP4={((IPEndPoint)tcp4.LocalEndpoint).Port}", $"UDP4={((IPEndPoint)udp4.Client.LocalEndPoint!).Port}", $"TCP6={(tcp6 is null ? 0 : ((IPEndPoint)tcp6.LocalEndpoint).Port)}", $"UDP6={(udp6 is null ? 0 : ((IPEndPoint)udp6.Client.LocalEndPoint!).Port)}" });
var children = new List<Process>();
if (args.Length > 2 && args[2] == "tree")
{
    for (var i = 0; i < 2; i++)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(Path.Combine(directory, "child" + i)); info.ArgumentList.Add(seconds.ToString());
        children.Add(Process.Start(info)!);
    }
    File.WriteAllLines(Path.Combine(directory, "children.txt"), children.Select(p => $"{p.Id}:{p.StartTime.ToUniversalTime().ToFileTimeUtc()}"));
}
var memory = new byte[48 * 1024 * 1024];
for (var index = 0; index < memory.Length; index += 4096) memory[index] = 1;
using var file = new FileStream(Path.Combine(directory, "workload.bin"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1024 * 1024, FileOptions.WriteThrough);
var clock = Stopwatch.StartNew();
double checksum = 0;
while (clock.Elapsed.TotalSeconds < seconds)
{
    var start = clock.ElapsedMilliseconds;
    while (clock.ElapsedMilliseconds - start < 60)
        for (var index = 1; index < 10000; index++) checksum += Math.Sqrt(index);
    file.Position = 0;
    file.Write(memory, 0, 1024 * 1024);
    file.Flush(true);
    file.Position = 0;
    file.ReadExactly(memory.AsSpan(0, 1024 * 1024));
    Thread.Sleep(40);
}
GC.KeepAlive(memory);
GC.KeepAlive(checksum);
foreach (var child in children) { if (!child.HasExited) child.Kill(); child.Dispose(); }
