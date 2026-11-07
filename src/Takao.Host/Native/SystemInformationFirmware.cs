using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Takao.Host.Models;
using Windows.Win32;
using Windows.Win32.System.SystemInformation;

namespace Takao.Host.Native;

// DMTF SMBIOS type 2/4/17, accessed through Windows' public RSMB provider.
// Strings, formatted sections and extended fields are all checked against the
// actual returned table. Firmware unknown sentinels never become real zeroes.
internal static class SystemInformationFirmware
{
    internal sealed record Firmware(IReadOnlyList<ProcessorInformationDto> Processors,
        MotherboardInformationDto Motherboard, MemoryInformationDto Memory);
    internal static Firmware Read()
    {
        var length = PInvoke.GetSystemFirmwareTable(FIRMWARE_TABLE_PROVIDER.RSMB, 0, []);
        if (length == 0) throw new InvalidOperationException(NativeSupport.LastError("读取SMBIOS固件表长度失败").Message);
        if (length is < 8 or > 4 * 1024 * 1024) throw new InvalidDataException("SMBIOS固件表超过4MiB边界或缺少头。");
        var bytes = new byte[length];
        var returned = PInvoke.GetSystemFirmwareTable(FIRMWARE_TABLE_PROVIDER.RSMB, 0, bytes);
        if (returned < 8 || returned > length) throw new InvalidDataException("SMBIOS固件表在读取期间变化，请刷新重试。");
        return Parse(bytes.AsSpan(0, (int)returned));
    }
    internal static Firmware Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 8) throw new InvalidDataException("SMBIOS头不足。");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]);
        if (length > raw.Length - 8) throw new InvalidDataException("SMBIOS声明长度超出实际固件表。");
        var table = raw.Slice(8, (int)length);
        var processors = new List<ProcessorInformationDto>();
        var modules = new List<MemoryModuleInformationDto>();
        MotherboardInformationDto? board = null;
        var seen = new HashSet<ushort>();
        var offset = 0;
        for (var count = 0; offset < table.Length; count++)
        {
            if (count >= 8192 || table.Length - offset < 4) throw new InvalidDataException("SMBIOS记录数量或头边界无效。");
            var type = table[offset]; var size = table[offset + 1];
            if (size < 4 || size > table.Length - offset) throw new InvalidDataException("SMBIOS格式段超出表边界。");
            var handle = BinaryPrimitives.ReadUInt16LittleEndian(table[(offset + 2)..]);
            if (!seen.Add(handle)) throw new InvalidDataException("SMBIOS记录句柄重复。");
            var end = offset + size;
            while (end + 1 < table.Length && !(table[end] == 0 && table[end + 1] == 0)) end++;
            if (end + 1 >= table.Length) throw new InvalidDataException("SMBIOS字符串段缺少双NUL终止符。");
            var record = table.Slice(offset, size).ToArray();
            var strings = DecodeStrings(table.Slice(offset + size, end - offset - size));
            var missing = new Dictionary<string, Unavailable>();
            string? Text(int index, string field)
            {
                var number = index < record.Length ? record[index] : 0;
                var result = number > 0 && number <= strings.Count ? Clean(strings[number - 1]) : null;
                if (result is null) missing[field] = Missing("固件未提供可用的" + field + "。");
                return result;
            }
            ushort? Word(int index) => index + 2 <= record.Length ? BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(index)) : null;
            uint? Dword(int index) => index + 4 <= record.Length ? BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(index)) : null;
            uint? ProcessorCount(int index, int extended, string field)
            {
                uint? value = index < record.Length ? record[index] : null;
                if (value == 255) value = Word(extended);
                if (value is null or 0 or 65535) { missing[field] = Missing("固件未提供此CPU的" + field + "。"); return null; }
                return value;
            }
            uint? Speed(int index, int extended, string field)
            {
                uint? value = Word(index);
                if (value == 65535) value = Dword(extended);
                if (value is null or 0 or uint.MaxValue) { missing[field] = Missing("固件未报告内存传输速率；不是实时测量值。"); return null; }
                return value;
            }
            if (type == 2 && board is null)
                board = new(Text(4, "manufacturer"), Text(5, "product"), Text(6, "version"), missing);
            // Status bit 6 means CPU socket populated. Never show empty sockets.
            else if (type == 4 && record.Length > 0x18 && (record[0x18] & 0x40) != 0)
            {
                processors.Add(new($"smbios:{handle}", Text(0x10, "name"), Text(7, "manufacturer"),
                    ProcessorCount(0x23, 0x2a, "coreCount"), ProcessorCount(0x25, 0x2e, "logicalProcessorCount"), missing));
            }
            else if (type == 17)
            {
                var sizeValue = Word(0x0c);
                if (sizeValue != 0) // 0 is explicitly an unpopulated slot; unknown stays represented.
                {
                    ulong? capacity = sizeValue switch
                    {
                        null or 0xffff => null,
                        0x7fff => Dword(0x1c) is { } large && large is > 0 and < 0x80000000 ? (ulong)large * 1024 * 1024 : null,
                        { } normal => (ulong)(normal & 0x7fff) * ((normal & 0x8000) != 0 ? 1024UL : 1024UL * 1024)
                    };
                    if (capacity is null) missing["capacityBytes"] = Missing("固件未报告此内存设备的容量。");
                    var memoryType = record.Length > 0x12 ? record[0x12] switch
                    {
                        0x12 => "DDR", 0x13 => "DDR2", 0x18 => "DDR3", 0x1a => "DDR4", 0x1b => "LPDDR",
                        0x1c => "LPDDR2", 0x1d => "LPDDR3", 0x1e => "LPDDR4", 0x22 => "DDR5", 0x23 => "LPDDR5",
                        0x0f => "SDRAM", _ => null
                    } : null;
                    if (memoryType is null) missing["memoryType"] = Missing("固件未报告可识别的内存类型。");
                    modules.Add(new($"smbios:{handle}", Text(0x10, "deviceLocator"), Text(0x17, "manufacturer"),
                        Text(0x1a, "partNumber"), capacity?.ToString(CultureInfo.InvariantCulture), memoryType,
                        Speed(0x15, 0x54, "speedMtPerSecond"), Speed(0x20, 0x58, "configuredSpeedMtPerSecond"), missing));
                }
            }
            offset = end + 2;
            if (type == 127) break;
        }
        var absent = Missing("SMBIOS未提供此类信息。");
        board ??= new(null, null, null, new Dictionary<string, Unavailable> { ["manufacturer"] = absent, ["product"] = absent, ["version"] = absent });
        var memoryMissing = new Dictionary<string, Unavailable>();
        string? installed = null;
        if (modules.Count > 0 && modules.All(module => module.CapacityBytes is not null))
            installed = modules.Aggregate(0UL, (sum, module) => checked(sum + ulong.Parse(module.CapacityBytes!, CultureInfo.InvariantCulture))).ToString(CultureInfo.InvariantCulture);
        else memoryMissing["installedBytes"] = Missing("内存条容量缺失，不能把部分已知容量当作安装总量。");
        if (modules.Count == 0) memoryMissing["moduleCount"] = absent;
        return new(processors, board, new(installed, modules.Count > 0 ? modules.Count : null, modules, memoryMissing));
    }
    internal static string? Clean(string? text)
    {
        var value = text?.Trim();
        return string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(c => c < 32 || c == 127) ||
            value.Equals("To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Default string", StringComparison.OrdinalIgnoreCase) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Not Specified", StringComparison.OrdinalIgnoreCase) || value.Equals("None", StringComparison.OrdinalIgnoreCase) ? null : value;
    }
    private static List<string?> DecodeStrings(ReadOnlySpan<byte> bytes)
    {
        var result = new List<string?>();
        var decoder = new UTF8Encoding(false, true);
        while (!bytes.IsEmpty)
        {
            if (result.Count >= 255) throw new InvalidDataException("SMBIOS字符串数量超过索引范围。");
            var end = bytes.IndexOf((byte)0);
            if (end < 0) end = bytes.Length;
            try { result.Add(end <= 512 ? decoder.GetString(bytes[..end]) : null); }
            catch (DecoderFallbackException) { result.Add(null); }
            bytes = end == bytes.Length ? [] : bytes[(end + 1)..];
        }
        return result;
    }
    internal static Unavailable Missing(string text) => new("notCollected", text);
}
