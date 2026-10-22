using System.Collections.Concurrent;
using TaskManager.Host.Models;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace TaskManager.Host.Native;

internal static class ProcessProtection
{
    // Only identities explicitly created by the Run task action are excluded from the host's child family.
    private static readonly ConcurrentDictionary<uint, string> Launched = new();
    internal static void Register(uint pid, string creationTime) => Launched[pid] = creationTime;
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    { "System", "Registry", "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "lsass.exe", "services.exe", "secure system", "memory compression" };

    internal static HashSet<uint> Family(IReadOnlyList<ProcessDto> processes)
    {
        var protectedIds = new HashSet<uint> { 0, 4, (uint)Environment.ProcessId };
        var byPid = processes.ToDictionary(p => p.Pid);
        var next = (uint)Environment.ProcessId;
        while (byPid.TryGetValue(next, out var parent) && parent.ParentPid != next && protectedIds.Add(parent.ParentPid)) next = parent.ParentPid;
        var descendants = new HashSet<uint> { (uint)Environment.ProcessId };
        bool changed;
        do
        {
            changed = false;
            foreach (var process in processes)
                if (descendants.Contains(process.ParentPid) && !IsExplicitlyLaunched(process) && descendants.Add(process.Pid)) changed = true;
        } while (changed);
        protectedIds.UnionWith(descendants);
        foreach (var process in processes)
            if (ProtectedNames.Contains(process.Name)) protectedIds.Add(process.Pid);
        // Enumeration and CreateProcess/Register can overlap. A PID absent from
        // this earlier list is not proof that a just-registered task has exited.
        // Remove only a natively confirmed missing/reused identity; conditional
        // removal also preserves a newer registration under the same PID.
        var live = processes.Select(p => p.Pid).ToHashSet();
        foreach (var pair in Launched)
        {
            if (live.Contains(pair.Key)) continue;
            using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pair.Key);
            var remove = handle.IsInvalid
                ? System.Runtime.InteropServices.Marshal.GetLastPInvokeError() is 87 or 1168
                : PInvoke.GetProcessTimes(handle, out var created, out _, out _, out _) && NativeSupport.FileTime(created).ToString(System.Globalization.CultureInfo.InvariantCulture) != pair.Value;
            if (remove) ((ICollection<KeyValuePair<uint, string>>)Launched).Remove(pair);
        }
        return protectedIds;
    }

    private static bool IsExplicitlyLaunched(ProcessDto process)
    {
        if (!Launched.TryGetValue(process.Pid, out var expected)) return false;
        if (process.CreationTimeFileTime is not null) return process.CreationTimeFileTime == expected;
        using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, process.Pid);
        return !handle.IsInvalid && PInvoke.GetProcessTimes(handle, out var creation, out _, out _, out _) && NativeSupport.FileTime(creation).ToString(System.Globalization.CultureInfo.InvariantCulture) == expected;
    }
}
