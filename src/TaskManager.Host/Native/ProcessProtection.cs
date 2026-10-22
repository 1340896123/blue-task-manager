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
        // Bound this registry to current identities; process creation time protects PID reuse.
        var live = processes.Select(p => p.Pid).ToHashSet();
        foreach (var pid in Launched.Keys) if (!live.Contains(pid)) Launched.TryRemove(pid, out _);
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
