using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Takao.Host.Services;

public sealed record SnapshotRepresentation(string ETag, byte[] Utf8);
internal static class SnapshotJson
{
    internal static readonly JsonSerializerSettings Settings = new() { ContractResolver = new CamelCasePropertyNamesContractResolver(), DateTimeZoneHandling = DateTimeZoneHandling.Utc, NullValueHandling = NullValueHandling.Include };
}
