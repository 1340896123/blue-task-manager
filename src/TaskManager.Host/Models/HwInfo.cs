namespace TaskManager.Host.Models;

public sealed record HwInfoReadingDto(string Id, uint ReadingId, uint Type, string Label, string OriginalLabel, string Unit,
    double? Value, double? Minimum, double? Maximum, double? Average, IReadOnlyDictionary<string, Unavailable> Unavailable);
public sealed record HwInfoSensorDto(string Id, uint SensorId, uint SensorInstance, string Name, string OriginalName, IReadOnlyList<HwInfoReadingDto> Readings);
public sealed record HwInfoSummaryDto(string Status, DateTime? CapturedAtUtc, DateTime? PollTimeUtc, int SensorCount, int ReadingCount, IReadOnlyDictionary<string, Unavailable> Unavailable);
public sealed record HwInfoSensorsDto(string Status, DateTime? CapturedAtUtc, DateTime? PollTimeUtc, int SensorCount, int ReadingCount,
    IReadOnlyDictionary<string, Unavailable> Unavailable, IReadOnlyList<HwInfoSensorDto> Sensors)
{
    [Newtonsoft.Json.JsonIgnore]
    public HwInfoSummaryDto Summary => new(Status, CapturedAtUtc, PollTimeUtc, SensorCount, ReadingCount, Unavailable);
    public static HwInfoSensorsDto WarmingUp { get; } = new("unavailable", null, null, 0, 0,
        new Dictionary<string, Unavailable> { ["source"] = new("warmingUp", "HWiNFO共享内存正在后台首次读取。") }, []);
}
