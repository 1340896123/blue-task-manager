using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Takao.Host.Models;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Ioctl;

namespace Takao.Host.Native;

internal sealed record DiskIdentity(uint? DeviceNumber, string? Model, string? MediaType, string? BusType,
    DateTime? CapturedAtUtc, IReadOnlyDictionary<string, Unavailable> Unavailable);

/// <summary>Owned by the deferred PDH worker. No device handle survives a read or enters the snapshot lock.</summary>
internal sealed class DiskIdentityCache
{
    private const int MaximumDescriptorBytes = 64 * 1024;
    private const uint MaximumDeviceNumber = 65_535;
    private static readonly string[] Fields = ["deviceNumber", "model", "mediaType", "busType"];
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    internal static PhysicalDiskDto FreshIdentity(PhysicalDiskDto disk)
    {
        if (disk.IdentityCapturedAtUtc is not { } captured || DateTime.UtcNow - captured <= TimeSpan.FromSeconds(30)) return disk;
        var missing = new Dictionary<string, Unavailable>(disk.Unavailable);
        foreach (var field in Fields) missing.TryAdd(field, new("stale", "磁盘身份最近查询已过期，等待后台重新验证设备。"));
        return disk with { DeviceNumber = null, Model = null, MediaType = null, BusType = null, Unavailable = missing };
    }

    internal IReadOnlyDictionary<string, DiskIdentity> Resolve(IEnumerable<string> source)
    {
        var names = source.Distinct(StringComparer.Ordinal).Take(256).ToArray();
        var present = names.ToHashSet(StringComparer.Ordinal);
        foreach (var removed in _entries.Keys.Where(name => !present.Contains(name)).ToArray()) _entries.Remove(removed);
        foreach (var name in names)
        {
            if (_entries.TryGetValue(name, out var entry) && Stopwatch.GetElapsedTime(entry.AttemptedAt) < entry.RetryAfter) continue;
            var identity = Read(name);
            _entries[name] = new(identity, Stopwatch.GetTimestamp(), TimeSpan.FromSeconds(identity.Unavailable.Count == 0 ? 30 : 5));
        }
        return names.ToDictionary(name => name, name => _entries[name].Identity, StringComparer.Ordinal);
    }

    // Never accept signs, Unicode digits, suffixes such as '#1', or overflowing/path-like input.
    internal static bool TryDeviceNumber(string name, out uint number)
    {
        number = 0;
        if (name.Length is 0 or > 1024) return false;
        var end = 0;
        while (end < name.Length && name[end] is >= '0' and <= '9') end++;
        return end > 0 && (end == name.Length || name[end] == ' ')
            && uint.TryParse(name.AsSpan(0, end), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number <= MaximumDeviceNumber;
    }

    private static unsafe DiskIdentity Read(string instance)
    {
        if (!TryDeviceNumber(instance, out var expected)) return Failed(new("identityMismatch", "PhysicalDisk实例没有可验证的物理磁盘编号；保留原始实例。"));
        try
        {
            using var device = PInvoke.CreateFile($@"\\.\PhysicalDrive{expected}", 0, FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
                null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, 0, null);
            if (device.IsInvalid) return Failed(Error(Marshal.GetLastPInvokeError(), "打开物理磁盘身份失败"));
            Span<byte> numberBytes = stackalloc byte[sizeof(STORAGE_DEVICE_NUMBER)];
            if (!PInvoke.DeviceIoControl(device, PInvoke.IOCTL_STORAGE_GET_DEVICE_NUMBER, [], numberBytes, out var returned, null))
                return Failed(Error(Marshal.GetLastPInvokeError(), "读取物理磁盘编号失败"));
            if (returned < numberBytes.Length) return Failed(Invalid("物理磁盘编号返回长度不足"));
            var nativeNumber = MemoryMarshal.Read<STORAGE_DEVICE_NUMBER>(numberBytes);
            if (nativeNumber.DeviceType != (uint)FILE_DEVICE_TYPE.FILE_DEVICE_DISK || nativeNumber.DeviceNumber == uint.MaxValue || nativeNumber.DeviceNumber != expected)
                return Failed(new("identityMismatch", "原生设备编号与PhysicalDisk实例不匹配，未发布该设备身份。"));

            var missing = new Dictionary<string, Unavailable>();
            string? model = null, bus = null, media = null;
            STORAGE_BUS_TYPE? nativeBus = null;
            var descriptorBytes = ReadDescriptor(device, out var descriptorError);
            if (descriptorBytes is not null)
            {
                var descriptor = MemoryMarshal.Read<STORAGE_DEVICE_DESCRIPTOR>(descriptorBytes);
                nativeBus = descriptor.BusType;
                bus = BusName(descriptor.BusType);
                if (bus is null) missing["busType"] = new("notSupported", $"Windows报告了未知存储总线值 {(int)descriptor.BusType}。");
                model = DescriptorString(descriptorBytes, descriptor.ProductIdOffset, out var modelError);
                // The standard product identifier is the model; vendor and serial identifiers are not substituted for a missing model.
                if (model is null) missing["model"] = modelError ?? new("notCollected", "存储设备未报告产品型号。");
            }
            else
            {
                missing["model"] = descriptorError!;
                missing["busType"] = descriptorError!;
            }

            if (!nativeBus.HasValue || !CanClassifyMedia(nativeBus.Value))
                missing["mediaType"] = new("notSupported", "当前总线未能确认独立物理介质；不从复合、虚拟或未知设备推测HDD/SSD。");
            else
            {
                var query = new STORAGE_PROPERTY_QUERY { PropertyId = STORAGE_PROPERTY_ID.StorageDeviceSeekPenaltyProperty, QueryType = STORAGE_QUERY_TYPE.PropertyStandardQuery };
                var input = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref query, 1));
                Span<byte> seekBytes = stackalloc byte[sizeof(DEVICE_SEEK_PENALTY_DESCRIPTOR)];
                if (!PInvoke.DeviceIoControl(device, PInvoke.IOCTL_STORAGE_QUERY_PROPERTY, input, seekBytes, out returned, null))
                    missing["mediaType"] = Error(Marshal.GetLastPInvokeError(), "查询磁盘寻道惩罚失败");
                else if (returned < seekBytes.Length) missing["mediaType"] = Invalid("磁盘寻道惩罚描述符长度不足");
                else
                {
                    var seek = MemoryMarshal.Read<DEVICE_SEEK_PENALTY_DESCRIPTOR>(seekBytes);
                    if (seek.Version < seekBytes.Length || seek.Size < seekBytes.Length || seek.Size > returned || seek.IncursSeekPenalty.Value > 1)
                        missing["mediaType"] = Invalid("磁盘寻道惩罚描述符无效");
                    else media = seek.IncursSeekPenalty ? "HDD" : "SSD";
                }
            }
            return new(expected, model, media, bus, DateTime.UtcNow, missing);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        { return Failed(new("notSupported", "Windows存储身份查询接口不可用。", exception.HResult)); }
    }

    private static unsafe byte[]? ReadDescriptor(SafeHandle device, out Unavailable? error)
    {
        error = null;
        var query = new STORAGE_PROPERTY_QUERY { PropertyId = STORAGE_PROPERTY_ID.StorageDeviceProperty, QueryType = STORAGE_QUERY_TYPE.PropertyStandardQuery };
        var input = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref query, 1));
        Span<byte> headerBytes = stackalloc byte[sizeof(STORAGE_DESCRIPTOR_HEADER)];
        if (!PInvoke.DeviceIoControl(device, PInvoke.IOCTL_STORAGE_QUERY_PROPERTY, input, headerBytes, out var returned, null))
        { error = Error(Marshal.GetLastPInvokeError(), "查询磁盘存储描述符长度失败"); return null; }
        if (returned < headerBytes.Length) { error = Invalid("磁盘存储描述符头长度不足"); return null; }
        var header = MemoryMarshal.Read<STORAGE_DESCRIPTOR_HEADER>(headerBytes);
        if (header.Size < sizeof(STORAGE_DEVICE_DESCRIPTOR) || header.Size > MaximumDescriptorBytes)
        { error = Invalid("磁盘存储描述符大小不在有效范围内"); return null; }
        var bytes = new byte[header.Size];
        if (!PInvoke.DeviceIoControl(device, PInvoke.IOCTL_STORAGE_QUERY_PROPERTY, input, bytes, out returned, null))
        { error = Error(Marshal.GetLastPInvokeError(), "读取磁盘存储描述符失败"); return null; }
        if (returned < sizeof(STORAGE_DEVICE_DESCRIPTOR) || returned > bytes.Length) { error = Invalid("磁盘存储描述符实际长度无效"); return null; }
        var descriptor = MemoryMarshal.Read<STORAGE_DEVICE_DESCRIPTOR>(bytes);
        if (descriptor.Size < sizeof(STORAGE_DEVICE_DESCRIPTOR) || descriptor.Size > returned || descriptor.Version < sizeof(STORAGE_DEVICE_DESCRIPTOR) || descriptor.Version > descriptor.Size)
        { error = Invalid("磁盘存储描述符版本或声明长度无效"); return null; }
        return bytes.AsSpan(0, (int)descriptor.Size).ToArray();
    }

    internal static unsafe string? DescriptorString(ReadOnlySpan<byte> bytes, uint offset, out Unavailable? error)
    {
        error = null;
        if (offset == 0) return null;
        if (offset < (uint)Marshal.OffsetOf<STORAGE_DEVICE_DESCRIPTOR>(nameof(STORAGE_DEVICE_DESCRIPTOR.RawDeviceProperties)).ToInt32() || offset >= bytes.Length)
        { error = Invalid("磁盘型号字符串偏移超出描述符"); return null; }
        var tail = bytes[(int)offset..];
        var end = tail.IndexOf((byte)0);
        if (end < 0 || end > 1024) { error = Invalid("磁盘型号字符串缺少有效终止符或过长"); return null; }
        var value = tail[..end];
        foreach (var character in value) if (character < 32 || character > 126) { error = Invalid("磁盘型号包含无效ASCII字符"); return null; }
        var text = Encoding.ASCII.GetString(value).Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool CanClassifyMedia(STORAGE_BUS_TYPE type) => type is STORAGE_BUS_TYPE.BusTypeScsi or STORAGE_BUS_TYPE.BusTypeAtapi
        or STORAGE_BUS_TYPE.BusTypeAta or STORAGE_BUS_TYPE.BusType1394 or STORAGE_BUS_TYPE.BusTypeSsa or STORAGE_BUS_TYPE.BusTypeFibre
        or STORAGE_BUS_TYPE.BusTypeUsb or STORAGE_BUS_TYPE.BusTypeiScsi or STORAGE_BUS_TYPE.BusTypeSas or STORAGE_BUS_TYPE.BusTypeSata or STORAGE_BUS_TYPE.BusTypeNvme;
    private static string? BusName(STORAGE_BUS_TYPE type) => type switch
    {
        STORAGE_BUS_TYPE.BusTypeScsi => "SCSI", STORAGE_BUS_TYPE.BusTypeAtapi => "ATAPI", STORAGE_BUS_TYPE.BusTypeAta => "ATA",
        STORAGE_BUS_TYPE.BusType1394 => "IEEE 1394", STORAGE_BUS_TYPE.BusTypeSsa => "SSA", STORAGE_BUS_TYPE.BusTypeFibre => "Fibre Channel",
        STORAGE_BUS_TYPE.BusTypeUsb => "USB", STORAGE_BUS_TYPE.BusTypeRAID => "RAID", STORAGE_BUS_TYPE.BusTypeiScsi => "iSCSI",
        STORAGE_BUS_TYPE.BusTypeSas => "SAS", STORAGE_BUS_TYPE.BusTypeSata => "SATA", STORAGE_BUS_TYPE.BusTypeSd => "SD",
        STORAGE_BUS_TYPE.BusTypeMmc => "MMC", STORAGE_BUS_TYPE.BusTypeVirtual => "Virtual", STORAGE_BUS_TYPE.BusTypeFileBackedVirtual => "FileBackedVirtual",
        STORAGE_BUS_TYPE.BusTypeSpaces => "Storage Spaces", STORAGE_BUS_TYPE.BusTypeNvme => "NVMe", STORAGE_BUS_TYPE.BusTypeSCM => "SCM",
        STORAGE_BUS_TYPE.BusTypeUfs => "UFS", STORAGE_BUS_TYPE.BusTypeNvmeof => "NVMe-oF", _ => null
    };
    private static DiskIdentity Failed(Unavailable error) => new(null, null, null, null, DateTime.UtcNow, Fields.ToDictionary(field => field, _ => error));
    private static Unavailable Invalid(string action) => new("invalidData", action + "，未采用此身份字段。");
    private static Unavailable Error(int code, string action) => new(code switch
    { 5 => "accessDenied", 1 or 50 or 87 => "notSupported", 2 or 3 or 21 or 55 or 1167 => "deviceRemoved", _ => "error" }, $"{action}：{new Win32Exception(code).Message}", code);
    private sealed record Entry(DiskIdentity Identity, long AttemptedAt, TimeSpan RetryAfter);
}
