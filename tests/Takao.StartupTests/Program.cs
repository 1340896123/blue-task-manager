using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using Takao.Host.Models;
using Takao.Host.Native;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

Console.OutputEncoding = new UTF8Encoding(false);
const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
var checks = new List<string>();
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
var beforeWarnings = new List<Unavailable>();
var before = SlowDataReader.ReadStartup(beforeWarnings);
var originalFingerprint = Fingerprint(before);
var prefix = "TakaoStartupTest-" + Guid.NewGuid().ToString("N") + "-";
var command = "\"" + Environment.ProcessPath + "\" --startup-fixture-never-executed";
var values = new List<OwnedValue>();
string? startupFile = null;
byte[]? fileContents = null;
var scopeProbes = 0;
try
{
    // Use real, existing records as read-only probes. A missing value in one hive
    // defaults independently even when the other hive contains a disabled record.
    foreach (var group in new[] { "Run", "Run32", "StartupFolder" })
    foreach (var machine in new[] { false, true })
    {
        using var other = Open(!machine, ApprovalPath(group));
        if (other.IsInvalid) continue;
        foreach (var name in Names(other))
        {
            var raw = Query(other, name);
            if (raw is not { Type: REG_VALUE_TYPE.REG_BINARY } value || value.Data.Length != 12 || BinaryPrimitives.ReadUInt32LittleEndian(value.Data) != 3) continue;
            using var own = Open(machine, ApprovalPath(group));
            if (!own.IsInvalid && Query(own, name) is not null) continue;
            using var approvals = new StartupApprovalReader();
            var state = approvals.Read(machine, group, name);
            Check(state.Enabled == true && state.Reason.Contains("按默认启用") && state.Reason.StartsWith("未设置"), "absent approval ignores existing disabled record in the other hive: " + (machine ? "HKLM" : "HKCU") + "/" + group);
            scopeProbes++;
            break;
        }
    }
    if (scopeProbes > 0) Check(true, "real machine supplies at least one cross-hive independence regression probe");

    foreach (var state in new uint[] { 2, 3, 1, 6, 7, uint.MaxValue })
    {
        Add(false, RunPath, prefix + state, REG_VALUE_TYPE.REG_SZ, Encoding.Unicode.GetBytes(command + '\0'));
        Add(false, ApprovalPath("Run"), prefix + state, REG_VALUE_TYPE.REG_BINARY, State(state));
    }
    Add(false, RunPath, prefix + "missing", REG_VALUE_TYPE.REG_SZ, Encoding.Unicode.GetBytes(command + '\0'));
    Add(false, RunPath, prefix + "bad-type", REG_VALUE_TYPE.REG_SZ, Encoding.Unicode.GetBytes(command + '\0'));
    Add(false, ApprovalPath("Run"), prefix + "bad-type", REG_VALUE_TYPE.REG_SZ, Encoding.Unicode.GetBytes("bad\0"));
    foreach (var length in new[] { 0, 4, 11, 13, 256 })
    {
        Add(false, RunPath, prefix + "length-" + length, REG_VALUE_TYPE.REG_SZ, Encoding.Unicode.GetBytes(command + '\0'));
        Add(false, ApprovalPath("Run"), prefix + "length-" + length, REG_VALUE_TYPE.REG_BINARY, State(2, length));
    }
    Add(false, RunPath + "Once", prefix + "once", REG_VALUE_TYPE.REG_SZ, Encoding.Unicode.GetBytes(command + '\0'));
    Add(false, ApprovalPath("Run"), prefix + "once", REG_VALUE_TYPE.REG_BINARY, State(3));
    var folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    Check(Directory.Exists(folder), "Windows current-user Startup folder exists");
    startupFile = Path.Combine(folder, prefix + "folder.txt");
    fileContents = Encoding.UTF8.GetBytes("Takao C# startup read test; never executed.");
    using (var file = new FileStream(startupFile, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(fileContents);
    Add(false, ApprovalPath("StartupFolder"), Path.GetFileName(startupFile), REG_VALUE_TYPE.REG_BINARY, State(3));

    var warnings = new List<Unavailable>();
    var rows = SlowDataReader.ReadStartup(warnings);
    StartupDto Row(string suffix) => rows.Single(row => row.Name == prefix + suffix);
    foreach (var state in new uint[] { 2, 3, 1, 6, 7, uint.MaxValue })
    {
        var row = Row(state.ToString());
        bool? expected = state == 2 ? true : state == 3 ? false : null;
        Check(row.Enabled == expected && row.EnabledReason.Contains(state.ToString()), "production Run approval preserves known/unknown state " + state);
        Check(row.Source.StartsWith("HKCU") && row.EnabledReason.Contains("HKCU") && !row.EnabledReason.Contains("HKLM"), "approval reason belongs only to original user source " + state);
        var roundTrip = JsonConvert.DeserializeObject<StartupDto>(JsonConvert.SerializeObject(row))!;
        Check(roundTrip.Enabled == expected && roundTrip.EnabledReason == row.EnabledReason, "JSON.NET preserves nullable Boolean and actual reason " + state);
    }
    Check(Row("missing").Enabled == true && Row("missing").EnabledReason.Contains("按默认启用"), "missing Run approval is explicitly default enabled");
    Check(Row("bad-type").Enabled is null && Row("bad-type").EnabledReason.Contains("类型或长度"), "malformed registry type remains unknown");
    foreach (var length in new[] { 0, 4, 11, 13, 256 })
        Check(Row("length-" + length).Enabled is null, "malformed approval length remains unknown " + length);
    Check(Row("once").Enabled is null && Row("once").EnabledReason.Contains("RunOnce"), "RunOnce never borrows same-name Run approval");
    var fileRow = rows.Single(row => row.Id == startupFile);
    Check(fileRow.Enabled == false && fileRow.EnabledReason.Contains(Path.GetFileName(startupFile)), "Startup folder matches full filename including extension");
    var mutableApproval = values.Single(value => value.Name == prefix + "2" && value.Path == ApprovalPath("Run"));
    mutableApproval.Replace(State(3));
    var refreshedRows = SlowDataReader.ReadStartup(new List<Unavailable>());
    Check(refreshedRows.Single(row => row.Name == prefix + "2").Enabled == false, "subsequent slow read observes real native enabled-to-disabled change");
    Check(Fingerprint(rows.Where(row => !row.Name.StartsWith(prefix))) == originalFingerprint, "all pre-existing startup rows and reasons remain unchanged during fixtures");
    Check(warnings.SequenceEqual(beforeWarnings), "fixtures do not mask source warnings");
}
finally
{
    var cleanupErrors = new List<Exception>();
    foreach (var value in values.AsEnumerable().Reverse())
        try { value.Dispose(); } catch (Exception error) { cleanupErrors.Add(error); }
    try
    {
        if (startupFile is not null && File.Exists(startupFile))
        {
            if (!File.ReadAllBytes(startupFile).SequenceEqual(fileContents!)) throw new InvalidOperationException("Refusing to remove modified startup fixture file.");
            File.Delete(startupFile);
        }
    }
    catch (Exception error) { cleanupErrors.Add(error); }
    if (cleanupErrors.Count > 0) throw new AggregateException("Startup fixture cleanup requires attention.", cleanupErrors);
}
var after = SlowDataReader.ReadStartup(new List<Unavailable>());
Check(!after.Any(row => row.Name.StartsWith(prefix)), "all GUID startup files and registry values are removed");
Check(Fingerprint(after) == originalFingerprint, "cleanup preserves every pre-existing startup row and status");
var report = new
{
    result = "PASS", assertions = checks.Count, checks,
    baseline = new { total = before.Count, enabled = before.Count(row => row.Enabled == true), disabled = before.Count(row => row.Enabled == false), unknown = before.Count(row => row.Enabled is null), warnings = beforeWarnings.Count },
    scopeProbes, scopeProbeStatus = scopeProbes > 0 ? "passed" : "skipped: no existing disabled approval with an absent same-name value in the other hive",
    cleanup = "only identity-matched GUID values and file removed; original startup fingerprint unchanged",
    notExecuted = new[] { "UI/browser/Electron tests", "Windows Settings changes", "HKLM fixture writes (current token is not elevated)", "access-denied approval fixture", "unverified approval states 1/6/7 remain unknown" }
};
var json = JsonConvert.SerializeObject(report, Formatting.Indented);
if (args.FirstOrDefault() is { } output) File.WriteAllText(output, json + Environment.NewLine);
Console.WriteLine(json);

void Add(bool machine, string path, string name, REG_VALUE_TYPE type, byte[] data)
{
    using var key = Open(machine, path, true);
    Check(!key.IsInvalid, "fixture source already exists and is writable: " + path.Split('\\').Last());
    if (Query(key, name) is not null) throw new InvalidOperationException("Refusing to overwrite existing startup registry value.");
    var value = new OwnedValue(machine, path, name, type, data);
    var status = PInvoke.RegSetValueEx(key, name, type, data);
    if (status != WIN32_ERROR.ERROR_SUCCESS) throw new InvalidOperationException("Fixture creation failed: " + status);
    values.Add(value);
}

static string Fingerprint(IEnumerable<StartupDto> rows) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(rows.OrderBy(row => row.Id)))));
static byte[] State(uint state, int length = 12) { var bytes = new byte[length]; if (length >= 4) BinaryPrimitives.WriteUInt32LittleEndian(bytes, state); return bytes; }
static string ApprovalPath(string group) => @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + group;
static SafeRegistryHandle Open(bool machine, string path, bool write = false)
{
    using var root = new SafeRegistryHandle((nint)(machine ? HKEY.HKEY_LOCAL_MACHINE : HKEY.HKEY_CURRENT_USER), false);
    var status = PInvoke.RegOpenKeyEx(root, path, 0, REG_SAM_FLAGS.KEY_QUERY_VALUE | REG_SAM_FLAGS.KEY_WOW64_64KEY | (write ? REG_SAM_FLAGS.KEY_SET_VALUE : 0), out var key);
    if (status is not (WIN32_ERROR.ERROR_SUCCESS or WIN32_ERROR.ERROR_FILE_NOT_FOUND or WIN32_ERROR.ERROR_PATH_NOT_FOUND)) throw new InvalidOperationException("Open fixture key failed: " + status);
    return key;
}
static (REG_VALUE_TYPE Type, byte[] Data)? Query(SafeRegistryHandle key, string name)
{
    var bytes = new byte[65536]; uint length = (uint)bytes.Length;
    var status = PInvoke.RegQueryValueEx(key, name, out var type, bytes, ref length);
    if (status is WIN32_ERROR.ERROR_FILE_NOT_FOUND or WIN32_ERROR.ERROR_PATH_NOT_FOUND) return null;
    if (status != WIN32_ERROR.ERROR_SUCCESS) throw new InvalidOperationException("Query fixture failed: " + status);
    return (type, bytes[..(int)length]);
}
static IEnumerable<string> Names(SafeRegistryHandle key)
{
    var result = new List<string>();
    for (uint index = 0; index < 4096; index++)
    {
        var name = new char[16384]; uint length = (uint)name.Length; uint bytes = 65536;
        var status = PInvoke.RegEnumValue(key, index, name, ref length, out _, new byte[bytes], ref bytes);
        if (status == WIN32_ERROR.ERROR_NO_MORE_ITEMS) break;
        if (status != WIN32_ERROR.ERROR_SUCCESS) throw new InvalidOperationException("Enumerate real approval probe failed: " + status);
        result.Add(new string(name, 0, (int)length));
    }
    return result;
}

sealed class OwnedValue(bool machine, string path, string name, REG_VALUE_TYPE type, byte[] data) : IDisposable
{
    public string Path => path;
    public string Name => name;
    private byte[] _data = data;
    public void Replace(byte[] replacement)
    {
        using var key = OpenOwned(); Verify(key);
        var status = PInvoke.RegSetValueEx(key, name, type, replacement);
        if (status != WIN32_ERROR.ERROR_SUCCESS) throw new InvalidOperationException("Fixture update failed: " + status);
        _data = replacement;
    }
    private SafeRegistryHandle OpenOwned()
    {
        using var root = new SafeRegistryHandle((nint)(machine ? HKEY.HKEY_LOCAL_MACHINE : HKEY.HKEY_CURRENT_USER), false);
        var status = PInvoke.RegOpenKeyEx(root, path, 0, REG_SAM_FLAGS.KEY_QUERY_VALUE | REG_SAM_FLAGS.KEY_SET_VALUE | REG_SAM_FLAGS.KEY_WOW64_64KEY, out var key);
        if (status != WIN32_ERROR.ERROR_SUCCESS) throw new InvalidOperationException("Owned fixture key inaccessible: " + status);
        return key;
    }
    private void Verify(SafeRegistryHandle key)
    {
        var buffer = new byte[65536]; uint length = (uint)buffer.Length;
        var status = PInvoke.RegQueryValueEx(key, name, out var actualType, buffer, ref length);
        if (status != WIN32_ERROR.ERROR_SUCCESS || actualType != type || !buffer.AsSpan(0, (int)length).SequenceEqual(_data)) throw new InvalidOperationException("Refusing to mutate a changed or unowned fixture registry value.");
    }
    public void Dispose()
    {
        using var key = OpenOwned(); Verify(key);
        var status = PInvoke.RegDeleteValue(key, name);
        if (status != WIN32_ERROR.ERROR_SUCCESS) throw new InvalidOperationException("Fixture cleanup failed: " + status);
    }
}
