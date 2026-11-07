using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Takao.Host.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Gdi;

namespace Takao.Host.Native;

internal static class SystemInformationCollector
{
    internal static SystemInformationDto Read(CancellationToken cancellationToken)
    {
        var missing = new Dictionary<string, Unavailable>();
        T Group<T>(string field, Func<T> read, T fallback)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return read(); }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or COMException or
                DllNotFoundException or EntryPointNotFoundException or OverflowException or UnauthorizedAccessException)
            { missing[field] = new("error", "读取" + field + "失败：" + exception.Message); return fallback; }
        }
        var firmware = Group<SystemInformationFirmware.Firmware?>("firmware", SystemInformationFirmware.Read, null);
        var absent = missing.GetValueOrDefault("firmware") ?? new Unavailable("notCollected", "SMBIOS没有报告此类硬件。");
        var board = firmware?.Motherboard ?? new(null, null, null,
            new Dictionary<string, Unavailable> { ["manufacturer"] = absent, ["product"] = absent, ["version"] = absent });
        var memory = firmware?.Memory ?? new(null, null, [],
            new Dictionary<string, Unavailable> { ["installedBytes"] = absent, ["moduleCount"] = absent, ["modules"] = absent });
        var processors = firmware?.Processors ?? [];
        if (processors.Count == 0) missing["processors"] = absent;
        var graphics = Group<IReadOnlyList<GraphicsInformationDto>>("graphicsAdapters", Graphics, []);
        var disks = Group<IReadOnlyList<DiskInformationDto>>("disks", () => Disks(cancellationToken), []);
        var displays = Group<IReadOnlyList<DisplayInformationDto>>("displays", Displays, []);
        var os = Group<OperatingSystemInformationDto?>("operatingSystem", OperatingSystem, null) ?? new(null, null, null, null,
            new Dictionary<string, Unavailable> { ["source"] = missing["operatingSystem"] });
        var partial = missing.Count > 0 || board.Unavailable.Count > 0 || memory.Unavailable.Count > 0 || os.Unavailable.Count > 0 ||
            processors.Any(row => row.Unavailable.Count > 0) || graphics.Any(row => row.Unavailable.Count > 0) ||
            memory.Modules.Any(row => row.Unavailable.Count > 0) || disks.Any(row => row.Unavailable.Count > 0) || displays.Any(row => row.Unavailable.Count > 0);
        return new(partial ? "partial" : "ready", DateTime.UtcNow, missing, processors, graphics, board, memory, disks, displays, os);
    }
    private static IReadOnlyList<GraphicsInformationDto> Graphics()
    {
        IDXGIFactory1? factory = null;
        var results = new List<GraphicsInformationDto>();
        try
        {
            PInvoke.CreateDXGIFactory1(out factory).ThrowOnFailure();
            if (factory is null) throw new COMException("DXGI未返回工厂对象。");
            for (uint index = 0; index <= 256; index++)
            {
                IDXGIAdapter1? adapter = null;
                try
                {
                    var hr = factory.EnumAdapters1(index, out adapter);
                    if (hr == HRESULT.DXGI_ERROR_NOT_FOUND) break;
                    hr.ThrowOnFailure();
                    if (index == 256) throw new InvalidDataException("DXGI适配器数量超过256项。");
                    var native = adapter.GetDesc1();
                    var name = SystemInformationFirmware.Clean(native.Description.ToString());
                    var missing = new Dictionary<string, Unavailable>();
                    if (name is null) missing["name"] = SystemInformationFirmware.Missing("DXGI未提供显卡型号。");
                    var id = $"luid_0x{unchecked((uint)native.AdapterLuid.HighPart):x8}_0x{native.AdapterLuid.LowPart:x8}";
                    results.Add(new(id, name, ((ulong)native.DedicatedVideoMemory).ToString(CultureInfo.InvariantCulture),
                        (native.Flags & DXGI_ADAPTER_FLAG.DXGI_ADAPTER_FLAG_SOFTWARE) != 0, missing));
                }
                finally { if (adapter is not null) Marshal.ReleaseComObject(adapter); }
            }
            return results;
        }
        finally { if (factory is not null) Marshal.ReleaseComObject(factory); }
    }
    private static IReadOnlyList<DiskInformationDto> Disks(CancellationToken cancellationToken)
    {
        var buffer = new char[256 * 1024];
        var length = PInvoke.QueryDosDevice(null, buffer);
        if (length == 0) throw new InvalidOperationException(NativeSupport.LastError("枚举物理磁盘设备失败").Message);
        if (length > buffer.Length) throw new InvalidDataException("物理设备名表超过边界。");
        var names = new string(buffer, 0, (int)length).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(name => name.StartsWith("PhysicalDrive", StringComparison.OrdinalIgnoreCase))
            .Select(name => name[13..]).Where(name => name.Length > 0 && name.All(character => character is >= '0' and <= '9') &&
                DiskIdentityCache.TryDeviceNumber(name, out _)).Distinct().ToArray();
        if (names.Length > 256) throw new InvalidDataException("物理磁盘超过256项。");
        var results = new List<DiskInformationDto>();
        foreach (var name in names.OrderBy(name => uint.Parse(name, CultureInfo.InvariantCulture)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = DiskIdentityCache.ReadHardware(name);
            var missing = new Dictionary<string, Unavailable>(identity.Unavailable);
            if (identity.CapacityBytes is null) missing.TryAdd("capacityBytes", new("identityMismatch", "设备编号或容量未验证，未采用磁盘容量。"));
            results.Add(new("physical:" + name, identity.DeviceNumber, identity.Model, identity.CapacityBytes, identity.MediaType, identity.BusType, missing));
        }
        return results;
    }
    private static unsafe IReadOnlyList<DisplayInformationDto> Displays()
    {
        var results = new List<DisplayInformationDto>();
        for (uint index = 0; index <= 256; index++)
        {
            var adapter = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
            if (!PInvoke.EnumDisplayDevices(null, index, ref adapter, 0)) break;
            if (index == 256) throw new InvalidDataException("显示适配器超过256项。");
            if ((adapter.StateFlags & DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
            var deviceName = adapter.DeviceName.ToString();
            var mode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
            var hasMode = PInvoke.EnumDisplaySettings(deviceName, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode);
            var monitorFound = false;
            for (uint monitorIndex = 0; monitorIndex < 64; monitorIndex++)
            {
                var monitor = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
                if (!PInvoke.EnumDisplayDevices(deviceName, monitorIndex, ref monitor, 1)) break;
                if ((monitor.StateFlags & DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_ACTIVE) == 0) continue;
                monitorFound = true;
                var missing = new Dictionary<string, Unavailable>();
                uint? width = hasMode && mode.dmPelsWidth > 0 ? mode.dmPelsWidth : null;
                uint? height = hasMode && mode.dmPelsHeight > 0 ? mode.dmPelsHeight : null;
                double? refresh = hasMode && mode.dmDisplayFrequency > 1 ? mode.dmDisplayFrequency : null;
                if (width is null) missing["widthPixels"] = SystemInformationFirmware.Missing("Windows当前显示宽度不可用。");
                if (height is null) missing["heightPixels"] = SystemInformationFirmware.Missing("Windows当前显示高度不可用。");
                if (refresh is null) missing["refreshRateHz"] = SystemInformationFirmware.Missing("Windows未报告有效刷新率。");
                var (name, widthMm, heightMm) = MonitorEdid(monitor.DeviceID.ToString(), missing);
                results.Add(new(deviceName + ":" + monitorIndex, name, width, height, refresh, widthMm, heightMm, missing));
            }
            if (!monitorFound)
            {
                var absent = SystemInformationFirmware.Missing("Windows未提供活动显示器名称/物理尺寸（可能为远程或虚拟显示）。");
                var missing = new Dictionary<string, Unavailable> { ["name"] = absent, ["widthMillimeters"] = absent, ["heightMillimeters"] = absent };
                if (!hasMode || mode.dmPelsWidth == 0) missing["widthPixels"] = absent;
                if (!hasMode || mode.dmPelsHeight == 0) missing["heightPixels"] = absent;
                if (!hasMode || mode.dmDisplayFrequency <= 1) missing["refreshRateHz"] = absent;
                results.Add(new(deviceName, null, hasMode && mode.dmPelsWidth > 0 ? mode.dmPelsWidth : null,
                    hasMode && mode.dmPelsHeight > 0 ? mode.dmPelsHeight : null, hasMode && mode.dmDisplayFrequency > 1 ? mode.dmDisplayFrequency : null,
                    null, null, missing));
            }
        }
        return results;
    }
    private static (string? Name, uint? WidthMm, uint? HeightMm) MonitorEdid(string interfaceName, Dictionary<string, Unavailable> missing)
    {
        byte[]? edid = null;
        Unavailable? error = null;
        var parts = interfaceName.Split('#');
        if (parts.Length == 4 && parts[0].EndsWith("DISPLAY", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Length is > 0 and < 128 && parts[2].Length is > 0 and < 256 &&
            !parts[1].Contains('\\') && !parts[2].Contains('\\'))
            edid = SystemInformationRegistry.Bytes(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + parts[1] + "\\" + parts[2] + @"\Device Parameters", "EDID", 3, out error);
        if (edid is null || edid.Length < 128 || !edid.AsSpan(0, 8).SequenceEqual(new byte[] { 0, 255, 255, 255, 255, 255, 255, 0 }) ||
            edid.Take(128).Sum(value => value) % 256 != 0)
        {
            error ??= new("notCollected", "显示器未提供有效EDID名称和物理尺寸。");
            foreach (var field in new[] { "name", "widthMillimeters", "heightMillimeters" }) missing[field] = error;
            return (null, null, null);
        }
        string? name = null;
        for (var offset = 54; offset <= 108; offset += 18)
            if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 2] == 0 && edid[offset + 3] == 0xfc)
                name = SystemInformationFirmware.Clean(Encoding.ASCII.GetString(edid, offset + 5, 13).TrimEnd('\n', '\r', '\0', ' '));
        uint? width = edid[21] > 0 && edid[22] > 0 ? (uint)edid[21] * 10 : null;
        uint? height = edid[21] > 0 && edid[22] > 0 ? (uint)edid[22] * 10 : null;
        if (name is null) missing["name"] = SystemInformationFirmware.Missing("EDID没有显示器产品名称。");
        if (width is null) missing["widthMillimeters"] = SystemInformationFirmware.Missing("EDID未报告物理宽度，不由DPI推算。");
        if (height is null) missing["heightMillimeters"] = SystemInformationFirmware.Missing("EDID未报告物理高度，不由DPI推算。");
        return (name, width, height);
    }
    private static OperatingSystemInformationDto OperatingSystem()
    {
        const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var missing = new Dictionary<string, Unavailable>();
        string? Read(string value, string field)
        {
            var result = SystemInformationRegistry.String(key, value, out var error);
            if (result is null) missing[field] = error!;
            return result;
        }
        var product = Read("ProductName", "caption");
        var version = Read("DisplayVersion", "displayVersion");
        var build = Read("CurrentBuildNumber", "build");
        var installationType = SystemInformationRegistry.String(key, "InstallationType", out _);
        if (product is not null && installationType == "Client" && uint.TryParse(build, out var number) && number >= 22000)
            product = product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        var revision = SystemInformationRegistry.Bytes(key, "UBR", 4, out _);
        if (build is not null && revision is { Length: 4 }) build += "." + BinaryPrimitives.ReadUInt32LittleEndian(revision).ToString(CultureInfo.InvariantCulture);
        PInvoke.GetNativeSystemInfo(out var system);
        var architecture = (ushort)system.wProcessorArchitecture switch { 9 => "x64", 12 => "ARM64", 0 => "x86", 6 => "IA64", _ => null };
        if (architecture is null) missing["architecture"] = new("notSupported", "Windows返回了未识别的系统架构。");
        return new(product, version, build, architecture, missing);
    }
}
