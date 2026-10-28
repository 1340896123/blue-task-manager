using System.Reflection;
using System.Text.RegularExpressions;
using NuGet.Versioning;
using Newtonsoft.Json;

namespace Takao.Host.Services;

internal sealed record UpdateReleaseIdentity(NuGetVersion Version, string? ReleaseTag, string? SourceCommit)
{
    internal static readonly Regex CommitPattern = new("\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant);
    internal static UpdateReleaseIdentity Current()
    {
        var assembly = typeof(UpdateReleaseIdentity).Assembly;
        var version = assembly.GetName().Version ?? new Version(0, 0, 0);
        var core = new NuGetVersion(version.Major, version.Minor, Math.Max(0, version.Build));
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().GroupBy(row => row.Key, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);
        metadata.TryGetValue("ReleaseTag", out var tag);
        metadata.TryGetValue("SourceCommit", out var commit);
        if (!TryTag(tag, out var tagged) || !SameCore(tagged!, core)) tag = null;
        if (commit is null || !CommitPattern.IsMatch(commit))
        {
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var candidate = info?.Split('+', 2).ElementAtOrDefault(1)?.Split('.')[0];
            commit = candidate is not null && CommitPattern.IsMatch(candidate) ? candidate : null;
        }
        return new(core, tag, commit);
    }
    internal static bool TryTag(string? tag, out NuGetVersion? version)
    {
        version = null;
        return tag is { Length: > 1 and <= 128 } && tag[0] == 'v' && !tag.Any(char.IsWhiteSpace)
            && NuGetVersion.TryParseStrict(tag[1..], out version);
    }
    internal static bool SameCore(NuGetVersion a, NuGetVersion b) => a.Major == b.Major && a.Minor == b.Minor && a.Patch == b.Patch;
    internal bool TargetInstalled(UpdateCandidate target) => VersionComparer.VersionRelease.Compare(Version, target.Core) > 0
        || (ReleaseTag is not null && TryTag(ReleaseTag, out var tag) && VersionComparer.VersionRelease.Compare(tag, target.Version) >= 0)
        || (SourceCommit is not null && SourceCommit == target.SourceCommit);
}

internal sealed record UpdateCandidate(long ReleaseId, string Tag, string SourceCommit, string ReleaseUrl, string AssetName, long AssetId,
    string DownloadUrl, long Size, string Sha256, string ChecksumsUrl)
{
    [JsonIgnore] internal NuGetVersion Version => NuGetVersion.Parse(Tag[1..]);
    [JsonIgnore] internal NuGetVersion Core => new(Version.Major, Version.Minor, Version.Patch);
}
