using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.Loader;

// Isolated malformed byte buffers exercise the production parser. This never
// creates a shared mapping, supplies telemetry, or represents a successful HWiNFO run.
var assemblyPath = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var candidate = Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll");
    return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
var parser = assembly.GetType("TaskManager.Host.Native.HwInfoSharedMemory", true)!
    .GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static)!;
byte[] Header(int length = 44)
{
    var bytes = new byte[length];
    Write(bytes, 0, 0x53695748); Write(bytes, 4, 2);
    BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(12, 8), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    Write(bytes, 20, 44); Write(bytes, 24, 264); Write(bytes, 32, 44); Write(bytes, 36, 316);
    return bytes;
}
void Write(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
void Reject(string name, byte[] bytes, string expected)
{
    try { parser.Invoke(null, [bytes]); }
    catch (TargetInvocationException exception)
    {
        var cause = exception.InnerException!;
        var reason = cause.GetType().GetProperty("Reason", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(cause);
        var code = reason?.GetType().GetProperty("Code")?.GetValue(reason) as string;
        if (code != expected) throw new InvalidOperationException(name + ": expected " + expected + ", got " + cause.GetType().Name + "/" + code, cause);
        Console.WriteLine("PASS\t" + name); return;
    }
    throw new InvalidOperationException(name + ": malformed input was accepted");
}
Reject("truncated header", new byte[43], "invalidData");
var buffer = Header(); Write(buffer, 0, 0); Reject("invalid signature", buffer, "invalidData");
buffer = Header(); Write(buffer, 4, 3); Reject("unsupported layout version", buffer, "unsupportedVersion");
buffer = Header(); BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(12, 8), 0); Reject("invalid publication time", buffer, "invalidData");
buffer = Header(); BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(12, 8), DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()); Reject("future publication time", buffer, "invalidData");
buffer = Header(); Write(buffer, 24, 263); Reject("short sensor stride", buffer, "unsupportedVersion");
buffer = Header(); Write(buffer, 36, 315); Reject("short reading stride", buffer, "unsupportedVersion");
buffer = Header(); Write(buffer, 28, 1025); Reject("sensor capacity", buffer, "capacity");
buffer = Header(); Write(buffer, 40, 8193); Reject("reading capacity", buffer, "capacity");
buffer = Header(); Write(buffer, 20, uint.MaxValue); Write(buffer, 24, uint.MaxValue); Write(buffer, 28, 1024); Reject("wide offset arithmetic", buffer, "invalidData");
buffer = Header(416); Write(buffer, 28, 1); Write(buffer, 32, 100); Write(buffer, 40, 1); Reject("overlapping sections", buffer, "invalidData");
buffer = Header(308); Write(buffer, 28, 1); buffer.AsSpan(52, 128).Fill(65); Reject("unterminated bounded text", buffer, "invalidData");
buffer = Header(360); Write(buffer, 40, 1); Reject("invalid sensor reference", buffer, "invalidData");
