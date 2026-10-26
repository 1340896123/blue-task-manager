using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// A test-only caller of the shipped production storage/downloader. It never
// changes update version identity, installation eligibility or pending state.
if (args.Length is < 3 or > 4 || args[0] is not ("download" or "deny")) throw new ArgumentException("Expected download|deny release-directory result-json [finish-file].");
var release = Path.GetFullPath(args[1]); var resultFile = Path.GetFullPath(args[2]);
var finishFile = args.Length == 4 ? Path.GetFullPath(args[3]) : null;
Directory.CreateDirectory(Path.GetDirectoryName(resultFile)!);
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(release, name.Name + ".dll")) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(release, name.Name + ".dll")) : null;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(release, "TaskManager.Host.dll"));
var storageType = assembly.GetType("TaskManager.Host.Native.ProtectedUpdateStorage", true)!;
var privilegeType = assembly.GetType("TaskManager.Host.Native.AdministratorPrivilege", true)!;
var sourceType = assembly.GetType("TaskManager.Host.Services.GitHubUpdateSource", true)!;
var storage = Activator.CreateInstance(storageType, true)!;
var directory = (string)storageType.GetProperty("DirectoryPath", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(storage)!;
var expectedDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlueDomainTaskManager", "Updates");
if (!Path.GetFullPath(directory).Equals(Path.GetFullPath(expectedDirectory), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Production storage path differs from fixed CommonApplicationData target.");
var installer = Path.Combine(directory, "installer.exe"); var parent = Path.GetDirectoryName(directory)!;
var checks = new List<string>(); var progress = new List<object>();
object? failure = null, metadata = null, security = null; bool skipped = false, created = false, parentCreated = false, ready = false, cleaned = false;
DateTime createdAt = default, parentCreatedAt = default; IDisposable? lease = null; string? expectedHash = null; long expectedSize = 0;
void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
void Save(string state) => File.WriteAllText(resultFile, JsonConvert.SerializeObject(new { state, checkedAtUtc = DateTime.UtcNow, checks, progress, metadata, security, directory, ready, skipped, cleaned, failure }, Formatting.Indented));
try
{
    var elevated = true;
    try { Call(privilegeType, null, "EnsureElevated"); } catch (Exception error) when (error.GetType().Name == "StartupPrivilegeException") { elevated = false; }
    if (args[0] == "deny")
    {
        Check(!elevated, "independent denial probe has a genuine non-elevated token");
        try { using var stream = new FileStream(installer, FileMode.Open, FileAccess.Write, FileShare.None); throw new InvalidOperationException("Medium token unexpectedly opened the installer for writing."); }
        catch (UnauthorizedAccessException) { checks.Add("medium token cannot open the protected official installer for writing"); }
        var testFile = Path.Combine(directory, "qa-denied-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { using (File.Create(testFile)) { } File.Delete(testFile); throw new InvalidOperationException("Medium token unexpectedly created a protected-directory file."); }
        catch (UnauthorizedAccessException) { checks.Add("medium token cannot create or replace protected update state"); }
        Save("PASS"); return;
    }
    Check(elevated, "download probe passes the production CsWin32 elevation check");
    Check(finishFile is not null && !File.Exists(finishFile), "a fresh bounded test completion signal is used");
    Call(storageType, null, "RejectLinks", directory);
    if (Directory.Exists(directory)) { skipped = true; checks.Add("existing ProgramData update directory preserved without mutation"); Save("SKIP_EXISTING_STATE"); return; }
    var parentExisted = Directory.Exists(parent);
    using var api = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    api.DefaultRequestHeaders.UserAgent.ParseAdd("BlueDomainTaskManager-QA/1.1.1");
    api.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json"); api.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    var releases = JArray.Parse(await api.GetStringAsync("https://api.github.com/repos/1340896123/blue-task-manager/releases?per_page=100"));
    var releaseRow = releases.OfType<JObject>().Where(row => row.Value<bool?>("draft") == false && row["published_at"]?.Type == JTokenType.Date)
        .OrderByDescending(row => row.Value<DateTime>("published_at")).First(row => row["assets"]!.Any(asset => asset.Value<string>("name") == "BlueDomainTaskManager-v1.1.1-win-x64-setup.exe"));
    var tag = releaseRow.Value<string>("tag_name")!; var assets = (JArray)releaseRow["assets"]!;
    var asset = assets.Single(row => row.Value<string>("name") == "BlueDomainTaskManager-v1.1.1-win-x64-setup.exe");
    var sums = assets.Single(row => row.Value<string>("name") == "SHA256SUMS");
    var name = asset.Value<string>("name")!; var url = asset.Value<string>("browser_download_url")!; var sumsUrl = sums.Value<string>("browser_download_url")!;
    var digest = asset.Value<string>("digest")!; expectedSize = asset.Value<long>("size");
    Check(asset.Value<string>("state") == "uploaded" && expectedSize is > 0 and <= 1024L * 1024 * 1024 && Regex.IsMatch(digest, "\\Asha256:[0-9a-f]{64}\\z"), "real uploaded GitHub attachment has bounded length and SHA256 digest");
    Check((bool)Call(sourceType, null, "OfficialAssetUrl", url, tag, name)! && (bool)Call(sourceType, null, "OfficialAssetUrl", sumsUrl, tag, "SHA256SUMS")!, "production URL validator accepts only the actual fixed-repository attachment URLs");
    expectedHash = digest[7..]; metadata = new { tag, releaseId = releaseRow.Value<long>("id"), assetId = asset.Value<long>("id"), name, size = expectedSize, sha256 = expectedHash, source = "actual official published attachment, not an application update candidate" };
    using var source = (IDisposable)Activator.CreateInstance(sourceType, true)!;
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    using (var sumsResponse = await (Task<HttpResponseMessage>)Call(sourceType, source, "DownloadResponseAsync", new Uri(sumsUrl), timeout.Token)!)
    {
        var text = await sumsResponse.Content.ReadAsStringAsync(timeout.Token);
        Check(text.Length <= 65536 && text.Split('\n').Select(line => line.TrimEnd('\r')).Count(line => line == expectedHash + "  " + name) == 1, "same-release SHA256SUMS agrees with the actual GitHub attachment digest");
    }
    Check(!Directory.Exists(directory), "update staging remains absent immediately before production Ensure");
    Call(storageType, storage, "Ensure"); created = true; createdAt = Directory.GetCreationTimeUtc(directory);
    parentCreated = !parentExisted; parentCreatedAt = Directory.GetCreationTimeUtc(parent);
    Check(!Directory.EnumerateFileSystemEntries(directory).Any(), "new protected storage starts empty");
    Call(storageType, storage, "Validate");
    var acl = new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
    var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
    var owner = acl.GetOwner(typeof(SecurityIdentifier))?.Value ?? throw new InvalidOperationException("Protected directory owner is unavailable.");
    security = new { owner, protectedDacl = acl.AreAccessRulesProtected, rules = rules.Select(row => new { sid = row.IdentityReference.Value, type = row.AccessControlType.ToString(), rights = row.FileSystemRights.ToString(), inherited = row.IsInherited }) };
    Check(acl.AreAccessRulesProtected && owner == "S-1-5-32-544" && rules.Length == 2 && rules.All(row => row.AccessControlType == AccessControlType.Allow && row.FileSystemRights == FileSystemRights.FullControl && row.IdentityReference.Value is "S-1-5-18" or "S-1-5-32-544"), "independent ACL read confirms protected Administrators/SYSTEM full-control-only storage");
    lease = (IDisposable)Call(storageType, storage, "AcquireLease")!;
    using (var response = await (Task<HttpResponseMessage>)Call(sourceType, source, "DownloadResponseAsync", new Uri(url), timeout.Token)!)
    {
        Check(response.Content.Headers.ContentLength is null || response.Content.Headers.ContentLength == expectedSize, "official HTTPS response length matches published attachment metadata");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var output = (FileStream)Call(storageType, storage, "CreatePart")!;
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; long bytes = 0, observedAt = Environment.TickCount64;
        for (var count = await input.ReadAsync(buffer, timeout.Token); count != 0; count = await input.ReadAsync(buffer, timeout.Token))
        {
            bytes += count; if (bytes > expectedSize) throw new InvalidOperationException("Official attachment exceeded its verified metadata length.");
            incremental.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
            if (Environment.TickCount64 - observedAt >= 1000 && progress.Count < 256) { progress.Add(new { bytes, total = expectedSize, checkedAtUtc = DateTime.UtcNow }); observedAt = Environment.TickCount64; }
        }
        output.Flush(true); progress.Add(new { bytes, total = expectedSize, checkedAtUtc = DateTime.UtcNow });
        Check(bytes == expectedSize && Convert.ToHexString(incremental.GetHashAndReset()).Equals(expectedHash, StringComparison.OrdinalIgnoreCase), "all official installer bytes match metadata length and streaming SHA256");
    }
    Call(storageType, storage, "CommitPart"); Call(storageType, storage, "ValidateInstaller");
    await using (var input = File.OpenRead(installer)) Check(Convert.ToHexString(await SHA256.HashDataAsync(input)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase), "independent second file hash agrees after production atomic part commit");
    Check(!File.Exists(Path.Combine(directory, "installer.part")) && !File.Exists(Path.Combine(directory, "pending.json")), "complete download leaves no part or install scheduling record");
    ready = true; Save("READY_FOR_MEDIUM");
    var deadline = DateTime.UtcNow.AddMinutes(3);
    while (!File.Exists(finishFile!) && DateTime.UtcNow < deadline) await Task.Delay(100);
    Check(File.Exists(finishFile!), "independent medium-token check finished within the bounded hold");
}
catch (Exception error) { failure = new { type = error.GetType().Name, message = error.Message }; }
finally
{
    if (args[0] == "download" && created)
    {
        try
        {
            Check(Path.GetFullPath(directory).Equals(Path.GetFullPath(expectedDirectory), StringComparison.OrdinalIgnoreCase) && Directory.GetCreationTimeUtc(directory) == createdAt, "cleanup still targets the exact directory created by this probe");
            Call(storageType, storage, "Validate");
            var remaining = Directory.EnumerateFileSystemEntries(directory).ToArray();
            Check(remaining.All(file => (Path.GetFileName(file) is "installer.exe" or "installer.part" or "operation.lock") && (File.GetAttributes(file) & FileAttributes.Directory) == 0), "cleanup sees no external files, pending state or subdirectories");
            foreach (var file in remaining) Call(storageType, null, "RejectLinks", file);
            if (File.Exists(installer)) { using var input = File.OpenRead(installer); Check(expectedHash is not null && new FileInfo(installer).Length == expectedSize && Convert.ToHexString(SHA256.HashData(input)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase), "cleanup installer is exactly the verified official file owned by this probe"); }
            Call(storageType, storage, "DeletePart"); Call(storageType, storage, "ClearPending", true);
            lease?.Dispose(); lease = null;
            var lockFile = Path.Combine(directory, "operation.lock"); if (File.Exists(lockFile)) File.Delete(lockFile);
            Check(!Directory.EnumerateFileSystemEntries(directory).Any(), "only exact probe-owned staging files were removed");
            Directory.Delete(directory, false);
            if (parentCreated && Directory.GetCreationTimeUtc(parent) == parentCreatedAt && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent, false);
            cleaned = !Directory.Exists(directory); Check(cleaned, "new empty update directory was removed without recursive cleanup");
        }
        catch (Exception error) { failure ??= new { type = error.GetType().Name, message = "Cleanup: " + error.Message }; }
    }
    lease?.Dispose(); Save(failure is null ? skipped ? "SKIP_EXISTING_STATE" : "PASS" : "FAIL");
}
if (failure is not null) Environment.ExitCode = 1;

static object? Call(Type type, object? target, string name, params object?[] values)
{
    try { return type.GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, values); }
    catch (TargetInvocationException error) when (error.InnerException is not null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
}
