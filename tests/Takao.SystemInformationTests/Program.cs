using System.Buffers.Binary;
using System.Text;
using Newtonsoft.Json;
using Takao.Host.Native;

var checks = new List<string>();
void Check(bool valid, string text) { if (!valid) throw new InvalidOperationException(text); checks.Add(text); }
byte[] Table(byte[] record, string strings)
{
    var tail = Encoding.UTF8.GetBytes(strings + "\0\0");
    var raw = new byte[8 + record.Length + tail.Length + 6];
    raw[1] = 3; raw[2] = 7;
    BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), (uint)(raw.Length - 8));
    record.CopyTo(raw, 8); tail.CopyTo(raw, 8 + record.Length);
    raw[^6] = 127; raw[^5] = 4; raw[^4] = 255; raw[^3] = 255;
    return raw;
}
var memory = new byte[0x60]; memory[0] = 17; memory[1] = (byte)memory.Length; memory[2] = 1;
memory[0x10] = 1; memory[0x17] = 2; memory[0x1a] = 3; memory[0x12] = 0x22;
BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(0x0c), 0xffff);
var unknown = SystemInformationFirmware.Parse(Table(memory, "slot\0vendor\0part"));
Check(unknown.Memory.Modules.Count == 1 && unknown.Memory.Modules[0].CapacityBytes is null && unknown.Memory.InstalledBytes is null,
    "Unknown DIMM capacity remains null and never produces a zero total");
Check(unknown.Memory.Modules[0].SpeedMtPerSecond is null && unknown.Memory.Modules[0].Unavailable.ContainsKey("speedMtPerSecond"),
    "Unknown DIMM speed retains a field reason");
BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(0x0c), 0x7fff);
BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(0x1c), 131072);
BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(0x15), 0xffff);
BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(0x54), 8800);
BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(0x20), 0xffff);
BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(0x58), 8400);
var extended = SystemInformationFirmware.Parse(Table(memory, "slot\0vendor\0part"));
Check(extended.Memory.InstalledBytes == "137438953472" && extended.Memory.Modules[0].ConfiguredSpeedMtPerSecond == 8400 && extended.Memory.Modules[0].SpeedMtPerSecond == 8800,
    "Extended SMBIOS capacity and transfer-rate fields preserve their standard units");
BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(0x0c), 0);
Check(SystemInformationFirmware.Parse(Table(memory, "slot\0vendor\0part")).Memory.Modules.Count == 0,
    "Unpopulated slots do not become installed DIMMs");
foreach (var raw in new[] { Array.Empty<byte>(), new byte[7], new byte[] { 0, 3, 0, 0, 255, 255, 255, 127 }, Table([17, 1, 2, 0], "") })
{
    var rejected = false;
    try { SystemInformationFirmware.Parse(raw); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "Reject malformed/truncated firmware without manufacturing hardware facts");
}
Check(SystemInformationFirmware.Clean("To Be Filled By O.E.M.") is null && SystemInformationFirmware.Clean("Default string") is null,
    "Firmware placeholder strings remain unknown");
var actual = SystemInformationFirmware.Read();
Check(actual.Processors.Count > 0 && actual.Memory.Modules.Count > 0 && actual.Motherboard.Product is not null,
    "Production CsWin32 firmware reader returns actual processors, DIMMs and motherboard");
var result = JsonConvert.SerializeObject(new { result = "PASS", checks, actual }, Formatting.Indented);
if (args.FirstOrDefault() is { } path) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, result); }
Console.WriteLine(result);
