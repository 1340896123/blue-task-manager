using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace Takao.Host.Services;

// Public GitHub REST/HTTPS only. No token, URL or repository is accepted from the renderer.
internal sealed class GitHubUpdateSource : IDisposable
{
    internal const string Repository = "1340896123/blue-task-manager";
    private const string ApiRoot = "https://api.github.com/repos/" + Repository;
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private string? etag;
    private List<Release>? cached;
    internal DateTimeOffset? RetryAtUtc { get; private set; }
    internal GitHubUpdateSource()
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Takao/1.1.1");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }
    internal async Task<UpdateCandidate?> CheckAsync(UpdateReleaseIdentity identity, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoot + "/releases?per_page=100");
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        List<Release> releases;
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null) releases = cached;
        else
        {
            EnsureSuccess(response);
            releases = JsonConvert.DeserializeObject<List<Release>>(await ReadTextAsync(response, 2 * 1024 * 1024, timeout.Token), JsonSettings) ?? throw Failure("GitHub没有返回有效发布列表。");
            if (releases.Count > 100) throw Failure("GitHub发布列表超过读取上限。");
            cached = releases; etag = response.Headers.ETag?.ToString();
        }
        var eligible = releases.Where(row => row is not null && row.Id > 0 && !row.Draft && row.PublishedAt is not null && UpdateReleaseIdentity.TryTag(row.TagName, out _))
            .OrderByDescending(row => NuGetVersion.Parse(row.TagName![1..]), VersionComparer.VersionRelease).ToArray();
        foreach (var release in eligible)
        {
            var version = NuGetVersion.Parse(release.TagName![1..]);
            var core = new NuGetVersion(version.Major, version.Minor, version.Patch);
            var coreComparison = VersionComparer.VersionRelease.Compare(core, identity.Version);
            if (coreComparison < 0) continue;
            if (identity.ReleaseTag is not null && UpdateReleaseIdentity.TryTag(identity.ReleaseTag, out var current)
                && VersionComparer.VersionRelease.Compare(version, current) <= 0) continue;
            var installers = release.Assets?.Where(asset => asset is not null && asset.Id > 0 && IsInstallerAssetName(asset.Name, core) && asset.State == "uploaded").ToArray() ?? [];
            var sums = release.Assets?.Where(asset => asset is not null && asset.Id > 0 && asset.Name == "SHA256SUMS" && asset.State == "uploaded").ToArray() ?? [];
            if (installers.Length != 1 || sums.Length != 1 || installers[0].Size is <= 0 or > 1024L * 1024 * 1024 || sums[0].Size is <= 0 or > 65536) continue;
            var asset = installers[0];
            var name = asset.Name!;
            if (asset.Digest is null || !Regex.IsMatch(asset.Digest, "\\Asha256:[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)
                || !OfficialAssetUrl(asset.Url, release.TagName, name) || !OfficialAssetUrl(sums[0].Url, release.TagName, "SHA256SUMS")) continue;
            var commit = await ApiAsync<Commit>(ApiRoot + "/commits/" + Uri.EscapeDataString(release.TagName), timeout.Token);
            if (commit.Sha is null || !UpdateReleaseIdentity.CommitPattern.IsMatch(commit.Sha)) throw Failure("无法确认发布标签对应的源码提交。");
            if (coreComparison == 0 && identity.ReleaseTag is null)
            {
                if (identity.SourceCommit is null) throw Failure("当前安装缺少可比较的发布标签和源码身份，无法确认同版本更新。");
                if (identity.SourceCommit == commit.Sha) return null;
                var comparison = await ApiAsync<Comparison>(ApiRoot + "/compare/" + identity.SourceCommit + "..." + commit.Sha, timeout.Token);
                if (comparison.Status is "identical" or "behind") return null;
                if (comparison.Status != "ahead" || comparison.AheadBy <= 0 || comparison.BehindBy != 0) throw Failure("当前源码与发布版本分叉，无法确认更新方向。");
            }
            var expected = asset.Digest[7..];
            using var sumResponse = await DownloadResponseAsync(new Uri(sums[0].Url!), timeout.Token);
            var checksumText = await ReadTextAsync(sumResponse, 65536, timeout.Token);
            var entries = checksumText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).Where(line => line.EndsWith("  " + name, StringComparison.Ordinal)).ToArray();
            if (entries.Length != 1 || entries[0] != expected + "  " + name) throw Failure("Release的SHA256SUMS与安装包摘要不一致。");
            return new(release.Id, release.TagName, commit.Sha, "https://github.com/" + Repository + "/releases/tag/" + Uri.EscapeDataString(release.TagName), name,
                asset.Id, asset.Url!, asset.Size, expected, sums[0].Url!);
        }
        return null;
    }
    internal static bool IsInstallerAssetName(string? name, NuGetVersion version) =>
        name == $"Takao-v{version.Major}.{version.Minor}.{version.Patch}-win-x64-setup.exe"
        || name == $"BlueDomainTaskManager-v{version.Major}.{version.Minor}.{version.Patch}-win-x64-setup.exe";
    internal async Task<HttpResponseMessage> DownloadResponseAsync(Uri initial, CancellationToken cancellationToken)
    {
        var address = initial;
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (address.Scheme != Uri.UriSchemeHttps || !AllowedAssetHost(address.Host) || !string.IsNullOrEmpty(address.UserInfo) || !address.IsDefaultPort)
                throw Failure("安装包下载地址不属于GitHub官方HTTPS附件服务。");
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.Accept.Clear(); request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location is { } next)
            { response.Dispose(); address = next.IsAbsoluteUri ? next : new Uri(address, next); continue; }
            try { EnsureSuccess(response); return response; } catch { response.Dispose(); throw; }
        }
        throw Failure("GitHub附件重定向次数超过上限。");
    }
    private async Task<T> ApiAsync<T>(string url, CancellationToken token)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token); EnsureSuccess(response);
        return JsonConvert.DeserializeObject<T>(await ReadTextAsync(response, 2 * 1024 * 1024, token), JsonSettings) ?? throw Failure("GitHub返回空元数据。");
    }
    private void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.Headers.RetryAfter?.Date is { } retry) RetryAtUtc = retry;
        else if (response.Headers.RetryAfter?.Delta is { } delta) RetryAtUtc = DateTimeOffset.UtcNow + delta;
        else if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resets) && long.TryParse(resets.FirstOrDefault(), out var reset)) RetryAtUtc = DateTimeOffset.FromUnixTimeSeconds(reset);
        throw Failure($"GitHub更新请求失败（HTTP {(int)response.StatusCode}），请稍后重试。");
    }
    private static async Task<string> ReadTextAsync(HttpResponseMessage response, int maximum, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > maximum) throw Failure("GitHub响应超过读取上限。");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream(); var buffer = new byte[16384];
        for (var length = await input.ReadAsync(buffer, token); length != 0; length = await input.ReadAsync(buffer, token))
        { if (output.Length + length > maximum) throw Failure("GitHub响应超过读取上限。"); await output.WriteAsync(buffer.AsMemory(0, length), token); }
        return new UTF8Encoding(false, true).GetString(output.ToArray());
    }
    internal static bool OfficialAssetUrl(string? url, string tag, string name) => Uri.TryCreate(url, UriKind.Absolute, out var value)
        && value.Scheme == "https" && value.Host == "github.com" && value.IsDefaultPort && value.UserInfo.Length == 0 && value.Query.Length == 0
        && Uri.UnescapeDataString(value.AbsolutePath) == "/" + Repository + "/releases/download/" + tag + "/" + name;
    private static bool AllowedAssetHost(string host) => host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
    private static ApiException Failure(string message) => new(503, "updateSourceUnavailable", message);
    private static readonly JsonSerializerSettings JsonSettings = new() { MaxDepth = 24, TypeNameHandling = TypeNameHandling.None };
    public void Dispose() => client.Dispose();
    private sealed class Release { public long Id { get; set; } [JsonProperty("tag_name")] public string? TagName { get; set; } public bool Draft { get; set; } [JsonProperty("published_at")] public DateTime? PublishedAt { get; set; } public List<Asset>? Assets { get; set; } }
    private sealed class Asset { public long Id { get; set; } public string? Name { get; set; } public string? State { get; set; } public long Size { get; set; } public string? Digest { get; set; } [JsonProperty("browser_download_url")] public string? Url { get; set; } }
    private sealed class Commit { public string? Sha { get; set; } }
    private sealed class Comparison { public string? Status { get; set; } [JsonProperty("ahead_by")] public int AheadBy { get; set; } [JsonProperty("behind_by")] public int BehindBy { get; set; } }
}
