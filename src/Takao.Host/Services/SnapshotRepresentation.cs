using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Takao.Host.Models;

namespace Takao.Host.Services;

public sealed record SnapshotRepresentation(string ETag, byte[] Utf8);
public enum SnapshotView { Full, Live, History, Performance }
internal static class SnapshotJson
{
    internal static readonly JsonSerializerSettings Settings = new() { ContractResolver = new CamelCasePropertyNamesContractResolver(), DateTimeZoneHandling = DateTimeZoneHandling.Utc, NullValueHandling = NullValueHandling.Include };
    private static readonly JsonSerializerSettings[] Views = Enum.GetValues<SnapshotView>().Select(view => new JsonSerializerSettings
    {
        ContractResolver = new ViewResolver(view), DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        NullValueHandling = NullValueHandling.Include
    }).ToArray();
    internal static JsonSerializerSettings For(SnapshotView view) => Views[(int)view];

    private sealed class ViewResolver : DefaultContractResolver
    {
        private readonly SnapshotView view;
        internal ViewResolver(SnapshotView view)
        {
            this.view = view;
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = true, OverrideSpecifiedNames = true };
        }
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization serialization)
        {
            var properties = base.CreateProperties(type, serialization);
            if (type != typeof(SnapshotDto)) return properties;
            return properties.Where(property => property.UnderlyingName switch
            {
                nameof(SnapshotDto.History) => view is SnapshotView.Full or SnapshotView.History,
                nameof(SnapshotDto.PerformanceHistory) => view is SnapshotView.Full or SnapshotView.Performance,
                _ => true
            }).ToList();
        }
    }
}
