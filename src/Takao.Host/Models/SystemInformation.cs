namespace Takao.Host.Models;

public sealed record SystemInformationDto(string Status, DateTime? CapturedAtUtc,
    IReadOnlyDictionary<string, Unavailable> Unavailable,
    IReadOnlyList<ProcessorInformationDto> Processors, IReadOnlyList<GraphicsInformationDto> GraphicsAdapters,
    MotherboardInformationDto Motherboard, MemoryInformationDto Memory, IReadOnlyList<DiskInformationDto> Disks,
    IReadOnlyList<DisplayInformationDto> Displays, OperatingSystemInformationDto OperatingSystem);
public sealed record ProcessorInformationDto(string Id, string? Name, string? Manufacturer, uint? CoreCount,
    uint? LogicalProcessorCount, IReadOnlyDictionary<string, Unavailable> Unavailable);
public sealed record GraphicsInformationDto(string Id, string? Name, string? DedicatedMemoryBytes, bool IsSoftware,
    IReadOnlyDictionary<string, Unavailable> Unavailable)
{
    public string? PhysicalDeviceId { get; init; }
    public string AdapterKind { get; init; } = "logical";
    public IReadOnlyList<string> LogicalAdapterIds { get; init; } = [];
}
public sealed record MotherboardInformationDto(string? Manufacturer, string? Product, string? Version,
    IReadOnlyDictionary<string, Unavailable> Unavailable);
public sealed record MemoryInformationDto(string? InstalledBytes, int? ModuleCount, IReadOnlyList<MemoryModuleInformationDto> Modules,
    IReadOnlyDictionary<string, Unavailable> Unavailable);
public sealed record MemoryModuleInformationDto(string Id, string? DeviceLocator, string? Manufacturer, string? PartNumber,
    string? CapacityBytes, string? MemoryType, uint? SpeedMtPerSecond, uint? ConfiguredSpeedMtPerSecond,
    IReadOnlyDictionary<string, Unavailable> Unavailable);
public sealed record DiskInformationDto(string Id, uint? DeviceNumber, string? Model, string? CapacityBytes,
    string? MediaType, string? BusType, IReadOnlyDictionary<string, Unavailable> Unavailable);
public sealed record DisplayInformationDto(string Id, string? Name, uint? WidthPixels, uint? HeightPixels,
    double? RefreshRateHz, uint? WidthMillimeters, uint? HeightMillimeters, IReadOnlyDictionary<string, Unavailable> Unavailable);
public sealed record OperatingSystemInformationDto(string? Caption, string? DisplayVersion, string? Build, string? Architecture,
    IReadOnlyDictionary<string, Unavailable> Unavailable);
