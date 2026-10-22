using System.Diagnostics;

// This is a real, bounded integration-test target. Production never invokes it or uses its output as telemetry.
var directory = args.Length > 0 ? Path.GetFullPath(args[0]) : throw new ArgumentException("A test-owned temporary directory is required.");
var seconds = args.Length > 1 && int.TryParse(args[1], out var duration) ? Math.Clamp(duration, 5, 180) : 90;
Directory.CreateDirectory(directory);
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
