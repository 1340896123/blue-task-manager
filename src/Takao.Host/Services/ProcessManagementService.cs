using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Takao.Host.Models;
using Takao.Host.Native;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Takao.Host.Services;

public sealed class ProcessManagementService
{
    private readonly object actionGate = new();
    private readonly Dictionary<string, (uint Priority, PROCESS_POWER_THROTTLING_STATE Power)> efficiency = new();
    public PriorityResult Priority(uint pid, PriorityRequest request)
    {
        uint value = request.Priority switch { "idle" => 0x40, "belowNormal" => 0x4000, "normal" => 0x20, "aboveNormal" => 0x8000, "high" => 0x80, _ => throw new ApiException(400, "invalidPriority", "只允许空闲、低于正常、正常、高于正常和高五档；实时优先级不可用。") };
        lock (actionGate)
        {
            if (efficiency.ContainsKey($"{pid}:{request.CreationTimeFileTime}")) throw new ApiException(409, "efficiencyPriorityConflict", "请先关闭本应用开启的效率模式并恢复原状态，再单独调整优先级。");
            using var handle = ProcessAccess.Open(pid, request.CreationTimeFileTime, PROCESS_ACCESS_RIGHTS.PROCESS_SET_INFORMATION, true);
            if (!PInvoke.SetPriorityClass(handle, (PROCESS_CREATION_FLAGS)value)) ProcessAccess.Throw("设置进程优先级失败");
            var actual = PInvoke.GetPriorityClass(handle);
            if (actual == 0) ProcessAccess.Throw("优先级已提交，但读回失败，请刷新确认");
            if (actual != value) throw new ApiException(409, "priorityNotApplied", $"Windows读回的实际优先级为{ProcessAccess.PriorityLabel(actual)}，与请求不一致。");
            return new(pid, request.CreationTimeFileTime, $"{pid}:{request.CreationTimeFileTime}", ProcessAccess.PriorityName(actual), "已读取并确认Windows实际优先级。");
        }
    }

    public TreePreview Preview(uint pid, string creation)
    {
        using var root = ProcessAccess.Open(pid, creation);
        var all = Win32Collector.EnumerateBaseProcesses();
        var protectedIds = ProcessProtection.Family(all);
        var byPid = all.ToDictionary(p => p.Pid);
        var children = all.Where(p => p.Pid != p.ParentPid).ToLookup(p => p.ParentPid);
        if (!byPid.ContainsKey(pid)) throw new ApiException(404, "processExited", "目标进程已退出。");
        var members = new Dictionary<uint, TreeMember>();
        var queue = new Queue<(uint Pid, string? Parent)>(); queue.Enqueue((pid, null));
        while (queue.TryDequeue(out var next))
        {
            if (members.ContainsKey(next.Pid)) continue;
            if (members.Count >= 4096) throw new ApiException(400, "treeTooLarge", "进程树超过4096成员上限，未执行树操作。");
            var basic = byPid[next.Pid];
            string? time = null; bool can = false; string? reason = null;
            using (var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, next.Pid))
            {
                if (handle.IsInvalid) reason = NativeSupport.LastError("无法读取成员身份").Message;
                else if (!PInvoke.GetProcessTimes(handle, out var started, out var exit, out _, out _)) reason = NativeSupport.LastError("无法读取成员创建时间").Message;
                else
                {
                    time = NativeSupport.FileTime(started).ToString(CultureInfo.InvariantCulture);
                    if (next.Parent is not null && members.TryGetValue(basic.ParentPid, out var parent) && parent.CreationTimeFileTime is { } parentTime && ulong.Parse(time, CultureInfo.InvariantCulture) < ulong.Parse(parentTime, CultureInfo.InvariantCulture)) continue;
                    if (NativeSupport.FileTime(exit) != 0) reason = "成员已退出，请重新预览。";
                    else if (protectedIds.Contains(next.Pid)) reason = "系统关键进程或本应用进程族受保护。";
                    else if (!PInvoke.IsProcessCritical(handle, out var critical)) reason = NativeSupport.LastError("无法确认成员关键状态").Message;
                    else if (critical) reason = "Windows关键进程，禁止结束。";
                    else can = true;
                }
            }
            var member = new TreeMember(next.Pid, time, $"{next.Pid}:{time ?? "unknown"}", basic.Name, next.Parent, can, reason);
            members[next.Pid] = member;
            foreach (var child in children[next.Pid]) queue.Enqueue((child.Pid, member.Identity));
        }
        ProcessAccess.Validate(root, creation);
        var ready = members.Count > 0 && members.Values.All(p => p.CanTerminate && p.CreationTimeFileTime is not null);
        return new($"{pid}:{creation}", DateTime.UtcNow, members.Values.ToArray(), ready, ready ? "确认将结束以下成员。执行前会重新核验完整集合，按叶到根执行。" : "部分成员身份或权限无法安全确认；禁止结束整棵树。");
    }

    public TreeActionResult TerminateTree(uint pid, TerminateTreeRequest request)
    {
        if (request.Members is null || request.Members.Count is 0 or > 4096 || request.Members.Any(p => p is null) || request.Members.Select(p => p.Pid).Distinct().Count() != request.Members.Count) throw new ApiException(400, "invalidTreeMembers", "请提交一次有效预览中的完整且不重复成员集合。");
        foreach (var member in request.Members) ProcessAccess.Parse(member.CreationTimeFileTime);
        lock (actionGate)
        {
            var preview = Preview(pid, request.CreationTimeFileTime);
            CheckSet(preview, request);
            if (!preview.CanTerminate) throw new ApiException(403, "protectedTree", "树中含受保护、已退出或身份不可读的成员，未开始结束。");
            var handles = new Dictionary<string, SafeFileHandle>();
            try
            {
                var protectedIds = ProcessProtection.Family(Win32Collector.EnumerateBaseProcesses());
                foreach (var member in preview.Members) handles[member.Identity] = ProcessAccess.Open(member.Pid, member.CreationTimeFileTime!, PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE | PROCESS_ACCESS_RIGHTS.PROCESS_SYNCHRONIZE, true, protectedIds);
                // Recheck membership after opening every handle. A failure here performs no termination.
                var finalPreview = Preview(pid, request.CreationTimeFileTime);
                CheckSet(finalPreview, request);
                if (!finalPreview.CanTerminate) throw new ApiException(403, "protectedTree", "树成员的保护状态已变化，未开始结束。");
                var byIdentity = preview.Members.ToDictionary(p => p.Identity);
                int Depth(TreeMember member) { var seen = new HashSet<string>(); var depth = 0; while (member.ParentIdentity is { } parent && seen.Add(parent) && byIdentity.TryGetValue(parent, out member!)) depth++; return depth; }
                var results = new List<TreeActionItem>();
                var execution = Stopwatch.StartNew();
                foreach (var member in preview.Members.OrderByDescending(Depth))
                {
                    try
                    {
                        var handle = handles[member.Identity]; ProcessAccess.Validate(handle, member.CreationTimeFileTime!);
                        if (!PInvoke.TerminateProcess(handle, 1)) ProcessAccess.Throw("结束树成员失败");
                        var wait = (uint)Math.Clamp(8000 - execution.ElapsedMilliseconds, 0, 1000);
                        var finished = PInvoke.WaitForSingleObject(handle, wait) == WAIT_EVENT.WAIT_OBJECT_0;
                        results.Add(new(member.Identity, member.Pid, finished ? "terminated" : "pending", finished ? "已确认进程退出。" : "Windows已接受结束请求，但等待退出超时，请刷新确认。"));
                    }
                    catch (ApiException error) { results.Add(new(member.Identity, member.Pid, "failed", error.Message, error.NativeErrorCode)); }
                }
                return new(results.All(p => p.Status == "terminated"), results, results.All(p => p.Status == "terminated") ? "所有预览成员均已确认退出。" : "部分成员未确认退出，请检查逐项结果；未扩展或重试目标集合。");
            }
            finally { foreach (var handle in handles.Values) handle.Dispose(); }
        }
    }
    private static void CheckSet(TreePreview actual, TerminateTreeRequest expected)
    {
        var identities = expected.Members.Select(p => $"{p.Pid}:{p.CreationTimeFileTime}").ToHashSet();
        if (!identities.SetEquals(actual.Members.Select(p => p.Identity))) throw new ApiException(409, "treeChanged", "树成员或创建身份已变化，未开始结束；请重新预览后确认。");
    }

    public unsafe EfficiencyState Efficiency(uint pid, string creation)
    {
        using var handle = ProcessAccess.Open(pid, creation);
        var state = new PROCESS_POWER_THROTTLING_STATE { Version = 1 };
        if (!PInvoke.GetProcessInformation(new HANDLE(handle.DangerousGetHandle()), PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, &state, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE)))
        { var error = NativeSupport.LastError("Windows不支持或不允许读取电源节流状态"); return new($"{pid}:{creation}", false, false, false, null, null, null, error.Message); }
        var priority = PInvoke.GetPriorityClass(handle); GC.KeepAlive(handle);
        lock (actionGate)
        {
            var owned = efficiency.ContainsKey($"{pid}:{creation}");
            var enabled = (state.ControlMask & state.StateMask & 1) != 0 && priority == 0x40;
            var reason = priority == 0x100 ? "实时优先级进程不支持在本应用中切换效率模式。" : enabled && !owned ? "当前节流与优先级由外部设置，本应用没有原状态基线；请使用原工具恢复。" : "效率模式同时使用Windows执行速度节流与空闲优先级；仅能恢复本应用保存的原状态。";
            return new($"{pid}:{creation}", priority != 0x100, enabled, owned, ProcessAccess.PriorityName(priority), state.ControlMask, state.StateMask, reason);
        }
    }

    public unsafe EfficiencyState Efficiency(uint pid, EfficiencyRequest request)
    {
        lock (actionGate)
        {
            using var handle = ProcessAccess.Open(pid, request.CreationTimeFileTime, PROCESS_ACCESS_RIGHTS.PROCESS_SET_INFORMATION, true);
            var identity = $"{pid}:{request.CreationTimeFileTime}";
            var state = new PROCESS_POWER_THROTTLING_STATE { Version = 1 };
            if (!PInvoke.GetProcessInformation(new HANDLE(handle.DangerousGetHandle()), PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, &state, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE))) ProcessAccess.Throw("无法读取Windows电源节流状态");
            var priority = PInvoke.GetPriorityClass(handle); if (priority == 0) ProcessAccess.Throw("无法读取原优先级");
            if (request.Enabled && priority == 0x100) throw new ApiException(403, "efficiencyRealtimeProtected", "实时优先级进程不支持在本应用中切换效率模式。");
            if (request.Enabled && efficiency.ContainsKey(identity)) return Efficiency(pid, request.CreationTimeFileTime);
            if (request.Enabled && (state.ControlMask & state.StateMask & 1) != 0 && priority == 0x40) throw new ApiException(409, "efficiencyExternallyManaged", "该进程已经由外部工具开启效率模式；本应用没有原状态基线，不重复覆盖。");
            if (!request.Enabled && !efficiency.ContainsKey(identity)) throw new ApiException(409, "efficiencyRestoreUnavailable", "没有本应用保存的原状态，无法安全恢复外部设置的效率模式。");
            if (request.Enabled && efficiency.Count >= 512)
            {
                var live = Win32Collector.EnumerateBaseProcesses().Select(p => p.Pid).ToHashSet();
                foreach (var key in efficiency.Keys.Where(key => !live.Contains(uint.Parse(key.Split(':')[0], CultureInfo.InvariantCulture))).ToArray()) efficiency.Remove(key);
                if (efficiency.Count >= 512) throw new ApiException(409, "efficiencyStateLimit", "效率模式恢复记录已达上限，请先恢复已有目标。");
            }
            var target = request.Enabled ? (Priority: 0x40u, Power: new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = state.ControlMask | 1, StateMask = state.StateMask | 1 }) : efficiency[identity];
            if (request.Enabled) efficiency[identity] = (priority, state);
            if (!SetPower(handle, target.Power)) { if (request.Enabled) efficiency.Remove(identity); ProcessAccess.Throw("Windows拒绝设置执行速度节流"); }
            if (!PInvoke.SetPriorityClass(handle, (PROCESS_CREATION_FLAGS)target.Priority))
            {
                var error = Marshal.GetLastPInvokeError();
                var rolledBack = SetPower(handle, state);
                if (request.Enabled && rolledBack) efficiency.Remove(identity);
                throw new ApiException(error == 5 ? 403 : 400, "efficiencyPriorityFailed", $"节流操作后的优先级设置失败；{(rolledBack ? "已恢复本次节流变更" : "节流回滚失败，请刷新状态并使用恢复动作")}。", error);
            }
            var readBack = new PROCESS_POWER_THROTTLING_STATE { Version = 1 };
            if (!PInvoke.GetProcessInformation(new HANDLE(handle.DangerousGetHandle()), PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, &readBack, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE))) ProcessAccess.Throw("效率模式已提交，但原生读回失败，请刷新确认");
            var actualPriority = PInvoke.GetPriorityClass(handle); GC.KeepAlive(handle);
            if (readBack.ControlMask != target.Power.ControlMask || readBack.StateMask != target.Power.StateMask || actualPriority != target.Priority) throw new ApiException(409, "efficiencyNotApplied", "Windows读回状态与请求不一致，请刷新并检查恢复状态。");
            if (!request.Enabled) efficiency.Remove(identity);
            return new(identity, true, (readBack.ControlMask & readBack.StateMask & 1) != 0 && actualPriority == 0x40, efficiency.ContainsKey(identity), ProcessAccess.PriorityName(actualPriority), readBack.ControlMask, readBack.StateMask, request.Enabled ? "已确认Windows执行速度节流与空闲优先级生效。" : "已读回并确认原节流/优先级状态恢复。");
        }
    }
    private static unsafe bool SetPower(SafeFileHandle handle, PROCESS_POWER_THROTTLING_STATE state)
    { var result = PInvoke.SetProcessInformation(new HANDLE(handle.DangerousGetHandle()), PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, &state, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE)); GC.KeepAlive(handle); return result; }
}
