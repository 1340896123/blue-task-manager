using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using Takao.Host.Models;
using Takao.Host.Native;

Console.OutputEncoding = new UTF8Encoding(false);
var checks = new List<string>();
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
foreach (var input in new[] { "0", "4 C:", "65535 " }) Check(DiskIdentityCache.TryDeviceNumber(input, out _), "accept bounded physical instance " + input);
foreach (var input in new[] { "", "_Total", "-1", "+1", "1#1 C:", "1C:", "4294967295", "65536 C:", "４ C:", "1\\C:", new string('1', 1025) })
    Check(!DiskIdentityCache.TryDeviceNumber(input, out _), "reject ambiguous/path/overflow physical instance " + input[..Math.Min(24, input.Length)]);
var descriptor = new byte[80];
Encoding.ASCII.GetBytes("  Test model  ").CopyTo(descriptor, 40);
Check(DiskIdentityCache.DescriptorString(descriptor, 40, out var error) == "Test model" && error is null, "bounded descriptor string preserves model and trims padding");
Check(DiskIdentityCache.DescriptorString(descriptor, 0, out error) is null && error is null, "absent product offset remains unknown");
foreach (var offset in new uint[] { 1, 35, 80, uint.MaxValue }) Check(DiskIdentityCache.DescriptorString(descriptor, offset, out error) is null && error?.Code == "invalidData", "reject invalid descriptor offset " + offset);
Array.Fill(descriptor, (byte)'x');
Check(DiskIdentityCache.DescriptorString(descriptor, 40, out error) is null && error?.Code == "invalidData", "reject unterminated product identifier");
descriptor[45] = 0; descriptor[42] = 255;
Check(DiskIdentityCache.DescriptorString(descriptor, 40, out error) is null && error?.Code == "invalidData", "reject malformed non-ASCII product identifier");
var knownFailure = new Unavailable("accessDenied", "fixture access denied");
var freshDisk = new PhysicalDiskDto("4 C:", "4 C:", false, 0, 0, 0, new Dictionary<string, Unavailable> { ["mediaType"] = knownFailure })
    { DeviceNumber = 4, Model = "fixture model", BusType = "SATA", IdentityCapturedAtUtc = DateTime.UtcNow };
Check(ReferenceEquals(freshDisk, DiskIdentityCache.FreshIdentity(freshDisk)), "fresh metadata survives independent PDH freshness projection");
var oldDisk = freshDisk with { IdentityCapturedAtUtc = DateTime.UtcNow.AddSeconds(-31) };
var expiredDisk = DiskIdentityCache.FreshIdentity(oldDisk);
Check(expiredDisk.DeviceNumber is null && expiredDisk.Model is null && expiredDisk.BusType is null && expiredDisk.Unavailable["model"].Code == "stale" && ReferenceEquals(expiredDisk.Unavailable["mediaType"], knownFailure), "expired identity clears successful fields while preserving actual unavailable reasons");

var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command");
start.ArgumentList.Add("$ErrorActionPreference='Stop'; $physical=@(Get-PhysicalDisk|Select-Object DeviceId,FriendlyName,@{n='MediaType';e={$_.MediaType.ToString()}},@{n='BusType';e={$_.BusType.ToString()}}); $instances=@((Get-Counter '\\PhysicalDisk(*)\\Disk Read Bytes/sec').CounterSamples.InstanceName|Where-Object {$_ -ne '_total'}); [pscustomobject]@{physical=$physical;instances=$instances}|ConvertTo-Json -Compress -Depth 4");
using var command = Process.Start(start)!;
var text = await command.StandardOutput.ReadToEndAsync(); var stderr = await command.StandardError.ReadToEndAsync(); await command.WaitForExitAsync();
Check(command.ExitCode == 0, "independent Windows storage and counter query succeeded: " + stderr);
var independent = JsonConvert.DeserializeObject<Independent>(text)!;
Check(independent.Instances.Length > 0, "Windows provides real PhysicalDisk instances");
var cache = new DiskIdentityCache();
var watch = Stopwatch.StartNew(); var identities = cache.Resolve(independent.Instances); var firstReadMs = watch.Elapsed.TotalMilliseconds;
foreach (var (instance, identity) in identities)
{
    Check(DiskIdentityCache.TryDeviceNumber(instance, out var number) && identity.DeviceNumber == number, "native device number verifies PDH instance " + instance);
    var source = independent.Physical.Single(row => row.DeviceId == number.ToString());
    Check(identity.Model is not null && source.FriendlyName.Contains(identity.Model, StringComparison.OrdinalIgnoreCase), "native product identifier matches independent device " + number);
    Check(identity.MediaType == source.MediaType, "native seek penalty matches independent media " + number);
    Check(identity.BusType == source.BusType, "native descriptor matches independent bus " + number);
    Check(identity.CapturedAtUtc is not null && identity.Unavailable.Count == 0, "successful fields have source time and no unavailable state " + number);
}
watch.Restart(); var repeated = cache.Resolve(independent.Instances); var cachedReadMs = watch.Elapsed.TotalMilliseconds;
Check(identities.All(item => ReferenceEquals(item.Value, repeated[item.Key])), "stable reads reuse immutable cached identities");
await Task.Delay(TimeSpan.FromSeconds(31));
var timedRefresh = cache.Resolve(independent.Instances);
Check(identities.All(item => !ReferenceEquals(item.Value, timedRefresh[item.Key]) && timedRefresh[item.Key].CapturedAtUtc > item.Value.CapturedAtUtc), "real elapsed cache expiry revalidates all native identities");
cache.Resolve([]); var refreshed = cache.Resolve(independent.Instances);
Check(timedRefresh.All(item => !ReferenceEquals(item.Value, refreshed[item.Key])), "removed instances evict cached identity before device-number reuse");
using var self = Process.GetCurrentProcess();
self.Refresh(); var handlesBefore = self.HandleCount;
for (var attempt = 0; attempt < 64; attempt++) { cache.Resolve([]); cache.Resolve(independent.Instances); }
self.Refresh(); var handlesAfter = self.HandleCount;
Check(handlesAfter <= handlesBefore + 4, "repeated real device refreshes release every storage handle");
var invalid = cache.Resolve(["not-a-physical-device", "65536 C:"]);
Check(invalid.Values.All(row => row.DeviceNumber is null && row.Model is null && row.MediaType is null && row.BusType is null && row.Unavailable.Count == 4 && row.Unavailable.Values.All(reason => reason.Code == "identityMismatch")), "unmappable instances publish no borrowed identity and retain all field reasons");
var report = new { result = "PASS", assertions = checks.Count, checks, source = "production CsWin32 DeviceIoControl; independent Get-PhysicalDisk/Get-Counter; boundary fixtures are separate from real device evidence", firstReadMs, cachedReadMs, handlesBefore, handlesAfter, disks = identities };
var output = JsonConvert.SerializeObject(report, Formatting.Indented);
if (args.FirstOrDefault() is { } destination) await File.WriteAllTextAsync(destination, output + Environment.NewLine);
Console.WriteLine(output);

internal sealed record Independent(Physical[] Physical, string[] Instances);
internal sealed record Physical(string DeviceId, string FriendlyName, string MediaType, string BusType);
