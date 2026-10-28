using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Takao.Host.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Threading;

namespace Takao.Host.Native;

/// <summary>Reads the public HWiNFO SM2 binary layout. No publisher, driver, or writable mapping is created.</summary>
internal static class HwInfoSharedMemory
{
    private const int HeaderSize = 44;
    private const int MaximumBytes = 64 * 1024 * 1024;

    internal static unsafe HwInfoSensorsDto Read()
    {
        using var mapping = PInvoke.OpenFileMapping((uint)FILE_MAP.FILE_MAP_READ, false, @"Global\HWiNFO_SENS_SM2");
        if (mapping.IsInvalid)
        {
            var code = Marshal.GetLastPInvokeError();
            if (code is 2 or 3) throw Failure("notAvailable", "HWiNFO共享内存不可用：可能未运行、未启用共享内存，或当前版本/许可未提供。", code);
            throw new HwInfoException(NativeSupport.Error(code, "打开HWiNFO只读共享内存失败"));
        }
        var view = PInvoke.MapViewOfFile(mapping, FILE_MAP.FILE_MAP_READ, 0, 0, 0);
        if (view.Value is null) throw new HwInfoException(NativeSupport.LastError("映射HWiNFO只读内存失败"));
        try
        {
            if (PInvoke.VirtualQuery(view.Value, out var region) == 0)
                throw new HwInfoException(NativeSupport.LastError("查询HWiNFO映射边界失败"));
            if (region.BaseAddress != view.Value || region.RegionSize < HeaderSize) throw Failure("invalidData", "HWiNFO映射缺少完整头。");
            if (region.RegionSize > MaximumBytes) throw Failure("capacity", "HWiNFO共享映射超过64MiB边界。");
            var length = checked((int)region.RegionSize);
            // Synchronize and release require these two standard mutex rights; no mutex is created.
            using var mutex = PInvoke.OpenMutex(SYNCHRONIZATION_ACCESS_RIGHTS.SYNCHRONIZATION_SYNCHRONIZE | SYNCHRONIZATION_ACCESS_RIGHTS.MUTEX_MODIFY_STATE, false, @"Global\HWiNFO_SM2_MUTEX");
            if (mutex.IsInvalid)
            {
                var mutexError = Marshal.GetLastPInvokeError();
                if (mutexError != 2) throw new HwInfoException(NativeSupport.Error(mutexError, "打开HWiNFO同步互斥体失败"));
            }
            var owned = false;
            try
            {
                if (!mutex.IsInvalid)
                {
                    var wait = PInvoke.WaitForSingleObject(mutex, 100);
                    if (wait == WAIT_EVENT.WAIT_TIMEOUT) throw Failure("synchronizationTimeout", "等待HWiNFO同步超过100毫秒，稍后重试。");
                    if (wait is not (WAIT_EVENT.WAIT_OBJECT_0 or WAIT_EVENT.WAIT_ABANDONED)) throw new HwInfoException(NativeSupport.LastError("等待HWiNFO同步失败"));
                    owned = true;
                }
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var before = new byte[HeaderSize];
                    Marshal.Copy((nint)view.Value, before, 0, HeaderSize);
                    var header = ValidateHeader(before, length);
                    var copy = new byte[header.RequiredBytes];
                    Marshal.Copy((nint)view.Value, copy, 0, copy.Length);
                    Thread.MemoryBarrier();
                    var after = new byte[HeaderSize];
                    Marshal.Copy((nint)view.Value, after, 0, HeaderSize);
                    if (!before.AsSpan().SequenceEqual(after) || !before.AsSpan().SequenceEqual(copy.AsSpan(0, HeaderSize))) continue;
                    return Parse(copy);
                }
                throw Failure("sourceChanging", "HWiNFO发布头在读取期间持续变化，未发布不完整采样。");
            }
            finally { if (owned) PInvoke.ReleaseMutex(mutex); }
        }
        finally { PInvoke.UnmapViewOfFile(view); }
    }

    // Internal byte parser also permits isolated malformed-layout verification without creating a production mapping.
    internal static HwInfoSensorsDto Parse(byte[] bytes)
    {
        var h = ValidateHeader(bytes, bytes.Length);
        var pollTime = DateTimeOffset.FromUnixTimeSeconds(h.PollTime).UtcDateTime;
        if (pollTime > DateTime.UtcNow.AddMinutes(5)) throw Failure("invalidData", "HWiNFO pollTime 超出有效时间范围。");
        var groups = new List<(string Id, uint SensorId, uint Instance, string Name, string Original, List<HwInfoReadingDto> Readings)>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (uint i = 0; i < h.SensorCount; i++)
        {
            var offset = checked((int)((ulong)h.SensorOffset + (ulong)i * h.SensorStride));
            var id = UInt(bytes, offset); var instance = UInt(bytes, offset + 4);
            var original = Text(bytes, offset + 8, 128); var name = Text(bytes, offset + 136, 128);
            var key = $"{id}:{instance}";
            if (!ids.Add(key)) throw Failure("invalidData", "HWiNFO传感器身份重复。");
            groups.Add((key, id, instance, string.IsNullOrWhiteSpace(name) ? original : name, original, []));
        }
        var readingIds = new HashSet<string>(StringComparer.Ordinal);
        for (uint i = 0; i < h.ReadingCount; i++)
        {
            var offset = checked((int)((ulong)h.ReadingOffset + (ulong)i * h.ReadingStride));
            var type = UInt(bytes, offset); var sensorIndex = UInt(bytes, offset + 4); var id = UInt(bytes, offset + 8);
            if (sensorIndex >= groups.Count) throw Failure("invalidData", "HWiNFO读取项引用了不存在的传感器索引。");
            var group = groups[(int)sensorIndex];
            var key = $"{group.Id}:{id}";
            if (!readingIds.Add(key)) throw Failure("invalidData", "HWiNFO读取项身份重复。");
            var original = Text(bytes, offset + 12, 128); var label = Text(bytes, offset + 140, 128); var unit = Text(bytes, offset + 268, 16);
            var missing = new Dictionary<string, Unavailable>();
            double? Number(int at, string field)
            {
                var value = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(offset + at, sizeof(double)));
                if (double.IsFinite(value)) return value;
                missing[field] = new("invalidData", "HWiNFO发布的此值不是有限数值。");
                return null;
            }
            group.Readings.Add(new(key, id, type, string.IsNullOrWhiteSpace(label) ? original : label, original, unit,
                Number(284, "value"), Number(292, "minimum"), Number(300, "maximum"), Number(308, "average"), missing));
        }
        var sensors = groups.Select(group => new HwInfoSensorDto(group.Id, group.SensorId, group.Instance, group.Name, group.Original, group.Readings.ToArray())).ToArray();
        return new("available", DateTime.UtcNow, pollTime, groups.Count, (int)h.ReadingCount, new Dictionary<string, Unavailable>(), sensors);
    }

    private static Header ValidateHeader(ReadOnlySpan<byte> bytes, int mappedLength)
    {
        if (bytes.Length < HeaderSize || mappedLength < HeaderSize) throw Failure("invalidData", "HWiNFO发布头不完整。");
        if (mappedLength > MaximumBytes) throw Failure("capacity", "HWiNFO共享映射超过64MiB边界。");
        if (UInt(bytes, 0) != 0x53695748) throw Failure("invalidData", "HWiNFO共享内存签名无效。");
        if (UInt(bytes, 4) != 2) throw Failure("unsupportedVersion", "当前HWiNFO共享内存版本不支持；需要SM2版本2布局。");
        var pollTime = BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(12, 8));
        if (pollTime <= 0 || pollTime > 253402300799) throw Failure("invalidData", "HWiNFO pollTime 不是有效Unix秒。");
        var sensorOffset = UInt(bytes, 20); var sensorStride = UInt(bytes, 24); var sensorCount = UInt(bytes, 28);
        var readingOffset = UInt(bytes, 32); var readingStride = UInt(bytes, 36); var readingCount = UInt(bytes, 40);
        if (sensorCount > 1024 || readingCount > 8192) throw Failure("capacity", "HWiNFO发布数量超过1024组/8192读取项边界。");
        if (sensorStride < 264 || readingStride < 316) throw Failure("unsupportedVersion", "HWiNFO元素布局短于支持的SM2字段前缀。");
        var sensorEnd = checked((ulong)sensorOffset + (ulong)sensorStride * sensorCount);
        var readingEnd = checked((ulong)readingOffset + (ulong)readingStride * readingCount);
        if (sensorOffset < HeaderSize || readingOffset < HeaderSize || sensorEnd > (ulong)mappedLength || readingEnd > (ulong)mappedLength)
            throw Failure("invalidData", "HWiNFO分区偏移/数量/步长超出已映射内存。");
        if (sensorCount > 0 && readingCount > 0 && (ulong)sensorOffset < readingEnd && (ulong)readingOffset < sensorEnd)
            throw Failure("invalidData", "HWiNFO传感器与读取项分区重叠。");
        return new(pollTime, sensorOffset, sensorStride, sensorCount, readingOffset, readingStride, readingCount, checked((int)Math.Max(HeaderSize, Math.Max(sensorEnd, readingEnd))));
    }
    private static uint UInt(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    private static unsafe string Text(byte[] bytes, int offset, int length)
    {
        var field = bytes.AsSpan(offset, length);
        var terminator = field.IndexOf((byte)0);
        if (terminator < 0) throw Failure("invalidData", "HWiNFO文本字段缺少边界内NUL终止符。");
        // The SM2 legacy fields use Windows ANSI, including localized names and the degree symbol.
        fixed (byte* start = bytes) return Marshal.PtrToStringAnsi((nint)(start + offset), terminator) ?? string.Empty;
    }
    private sealed record Header(long PollTime, uint SensorOffset, uint SensorStride, uint SensorCount, uint ReadingOffset, uint ReadingStride, uint ReadingCount, int RequiredBytes);
    private static HwInfoException Failure(string code, string message, int? nativeCode = null) => new(new(code, message, nativeCode));
}

internal sealed class HwInfoException(Unavailable reason) : Exception(reason.Message)
{
    internal Unavailable Reason { get; } = reason;
}
