using System.Diagnostics;
using System.Runtime.InteropServices;
using Takao.Host.Models;
using Takao.Host.Native;
using Windows.Win32;
using Windows.Win32.System.Services;

namespace Takao.Host.Services;

public sealed class ServiceActionService(SnapshotService snapshots)
{
    private readonly SemaphoreSlim mutation = new(1, 1);
    public unsafe ServiceDetails Details(string name)
    {
        ValidateName(name);
        using var manager = PInvoke.OpenSCManager(null, null, PInvoke.SC_MANAGER_CONNECT);
        if (manager.IsInvalid) NativeError("无法打开服务管理器");
        using var service = PInvoke.OpenService(manager, name, PInvoke.SERVICE_QUERY_STATUS);
        if (service.IsInvalid) NativeError("无法查询服务");
        var status = Status(service);
        var errors = new Dictionary<string, Unavailable>();
        string? binary = null, account = null, startType = null;
        var display = snapshots.Current?.Services.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? name;
        var dependencies = new List<string>();
        using var config = PInvoke.OpenService(manager, name, PInvoke.SERVICE_QUERY_CONFIG);
        if (config.IsInvalid) errors["configuration"] = NativeSupport.LastError("服务配置不可读");
        else
        {
            PInvoke.QueryServiceConfig(config, null, 0, out var size);
            if (size == 0) errors["configuration"] = NativeSupport.LastError("服务配置长度不可读");
            else if (size > 65536) errors["configuration"] = new("error", "服务配置超过64KiB上限。");
            else
            {
                var bytes = new byte[size];
                fixed (byte* buffer = bytes)
                {
                    var info = (QUERY_SERVICE_CONFIGW*)buffer;
                    if (!PInvoke.QueryServiceConfig(config, info, size, out _)) errors["configuration"] = NativeSupport.LastError("服务配置不可读");
                    else
                    {
                        binary = info->lpBinaryPathName.ToString(); account = info->lpServiceStartName.ToString(); startType = info->dwStartType.ToString(); display = info->lpDisplayName.ToString();
                        var text = info->lpDependencies.Value; var end = buffer + bytes.Length;
                        while (text is not null && (byte*)text < end && *text != '\0' && dependencies.Count < 256)
                        {
                            var begin = text; while ((byte*)(text + 1) <= end && *text != '\0') text++;
                            if ((byte*)(text + 1) > end) { errors["dependencies"] = new("partial", "服务依赖表未能完整读取。"); break; }
                            dependencies.Add(new string(begin, 0, (int)(text - begin))); text++;
                        }
                    }
                }
            }
        }
        bool Has(uint access, string field, string action)
        {
            using var probe = PInvoke.OpenService(manager, name, access);
            if (!probe.IsInvalid) return true;
            errors[field] = NativeSupport.LastError(action); return false;
        }
        var mayStart = Has(PInvoke.SERVICE_START | PInvoke.SERVICE_QUERY_STATUS, "start", "当前用户无法打开服务启动权限");
        var mayStop = Has(PInvoke.SERVICE_STOP | PInvoke.SERVICE_QUERY_STATUS, "stop", "当前用户无法打开服务停止权限");
        if (startType == "SERVICE_DISABLED") { mayStart = false; errors["start"] = new("disabled", "Windows已禁用该服务；本应用不会自动改变启动方式。"); }
        if (errors.TryGetValue("configuration", out var configurationError)) errors["restart"] = new(configurationError.Code, "无法读取启动配置，不能保证停止后可以重启；" + configurationError.Message, configurationError.NativeErrorCode);
        var stopped = status.dwCurrentState == SERVICE_STATUS_CURRENT_STATE.SERVICE_STOPPED;
        var active = status.dwCurrentState is SERVICE_STATUS_CURRENT_STATE.SERVICE_RUNNING or SERVICE_STATUS_CURRENT_STATE.SERVICE_PAUSED;
        var acceptsStop = (status.dwControlsAccepted & 1) != 0;
        return new(name, display, status.dwCurrentState.ToString(), status.dwProcessId, binary, startType, account, dependencies, stopped && mayStart, active && acceptsStop && mayStop, active && acceptsStop && mayStop && mayStart && !errors.ContainsKey("configuration"), DateTime.UtcNow, errors);
    }

    public async Task<ServiceControlResult> ControlAsync(string name, ServiceControlRequest request)
    {
        ValidateName(name);
        if (request.Command is not ("start" or "stop" or "restart") || !Enum.TryParse<SERVICE_STATUS_CURRENT_STATE>(request.ExpectedState, out var expected) || !Enum.IsDefined(expected)) throw new ApiException(400, "invalidServiceCommand", "请输入有效的服务动作与预期状态。");
        if (!await mutation.WaitAsync(0)) throw new ApiException(409, "serviceBusy", "已有服务动作执行中，请等实际状态确认后再操作。");
        var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var wrote = false;
        var observed = new ServiceControlResult(name, "unknown", null, false, false, "尚未完成实际状态读取，当前PID未知。");
        var task = Task.Run(async () =>
        {
            try
            {
                using var manager = PInvoke.OpenSCManager(null, null, PInvoke.SC_MANAGER_CONNECT);
                if (manager.IsInvalid) NativeError("无法打开服务管理器");
                uint access = PInvoke.SERVICE_QUERY_STATUS | (request.Command is "start" or "restart" ? PInvoke.SERVICE_START : 0) | (request.Command is "stop" or "restart" ? PInvoke.SERVICE_STOP : 0) | (request.Command == "restart" ? PInvoke.SERVICE_QUERY_CONFIG : 0);
                using var service = PInvoke.OpenService(manager, name, access);
                if (service.IsInvalid) NativeError("Windows拒绝打开服务动作句柄");
                var status = Status(service);
                observed = Observe(name, status, false, false, "已读取服务实际状态。");
                if (status.dwCurrentState != expected || status.dwProcessId != request.ExpectedPid) throw new ApiException(409, "serviceChanged", "服务状态或PID已变化，未执行动作；请刷新后重试。");
                if (status.dwCurrentState is not (SERVICE_STATUS_CURRENT_STATE.SERVICE_RUNNING or SERVICE_STATUS_CURRENT_STATE.SERVICE_STOPPED or SERVICE_STATUS_CURRENT_STATE.SERVICE_PAUSED)) throw new ApiException(409, "servicePending", "服务正在转换状态，请等待完成后重试。");
                if (request.Command == "start" && status.dwCurrentState != SERVICE_STATUS_CURRENT_STATE.SERVICE_STOPPED) throw new ApiException(409, "serviceAlreadyRunning", "服务并非已停止状态，未重复启动。");
                if (request.Command == "restart" && IsDisabled(service)) throw new ApiException(409, "serviceDisabled", "该服务启动配置已禁用；为避免停止后无法启动，未执行重启。");
                if (request.Command is "stop" or "restart")
                {
                    if (status.dwCurrentState == SERVICE_STATUS_CURRENT_STATE.SERVICE_STOPPED) throw new ApiException(409, "serviceAlreadyStopped", "服务已经停止，未执行停止或重启。");
                    timeout.Token.ThrowIfCancellationRequested();
                    if (!PInvoke.ControlService(service, 1 /* SERVICE_CONTROL_STOP */, out _)) NativeError("停止服务失败；不会自动停止依赖服务");
                    wrote = true;
                    if (!await WaitForAsync(SERVICE_STATUS_CURRENT_STATE.SERVICE_STOPPED)) return TimedOut();
                }
                if (request.Command is "start" or "restart")
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    if (!PInvoke.StartService(service, default)) NativeError("启动服务失败");
                    wrote = true;
                    if (!await WaitForAsync(SERVICE_STATUS_CURRENT_STATE.SERVICE_RUNNING)) return TimedOut();
                }
                observed = Observe(name, Status(service), true, false, "已确认服务到达请求的实际状态。");
                snapshots.RefreshSlowData();
                return observed;

                async Task<bool> WaitForAsync(SERVICE_STATUS_CURRENT_STATE target)
                {
                    while (!timeout.IsCancellationRequested)
                    {
                        var actual = Status(service); observed = Observe(name, actual, actual.dwCurrentState == target, false, "正在等待服务实际状态。");
                        if (actual.dwCurrentState == target) return true;
                        if (actual.dwCurrentState == SERVICE_STATUS_CURRENT_STATE.SERVICE_STOPPED && target == SERVICE_STATUS_CURRENT_STATE.SERVICE_RUNNING) throw new ApiException(400, "serviceStartFailed", "服务在启动后已停止。", (int)actual.dwWin32ExitCode);
                        await Task.Delay(200, timeout.Token);
                    }
                    return false;
                }
            }
            catch (ApiException error) when (wrote)
            {
                throw new ApiException(error.Status, error.Code, $"{error.Message} 最后观察状态：{observed.State}，PID {observed.Pid?.ToString() ?? "未知"}；部分动作可能已经生效，请刷新确认。", error.NativeErrorCode);
            }
            finally { if (wrote) snapshots.RefreshSlowData(); mutation.Release(); timeout.Dispose(); }
        });
        try { return await task.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException)
        {
            // A synchronous SCM call can outlive the deadline. Its owned handles remain alive;
            // cancellation prevents a later restart/start step and the busy gate remains held.
            try { timeout.Cancel(); } catch (ObjectDisposedException) { }
            snapshots.RefreshSlowData();
            _ = task.ContinueWith(completed => _ = completed.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return TimedOut();
        }
        ServiceControlResult TimedOut() => observed with { Completed = false, TimedOut = true, Message = "15秒内未确认完成；显示最后观察状态。Windows可能仍在处理，请刷新确认，勿盲目重试。" };
    }
    private static ServiceControlResult Observe(string name, SERVICE_STATUS_PROCESS status, bool completed, bool timedOut, string message) => new(name, status.dwCurrentState.ToString(), status.dwProcessId, completed, timedOut, message);
    private static SERVICE_STATUS_PROCESS Status(System.Runtime.InteropServices.SafeHandle handle)
    {
        var status = new SERVICE_STATUS_PROCESS();
        if (!PInvoke.QueryServiceStatusEx(handle, SC_STATUS_TYPE.SC_STATUS_PROCESS_INFO, MemoryMarshal.AsBytes(new Span<SERVICE_STATUS_PROCESS>(ref status)), out _)) NativeError("读取服务实际状态失败");
        return status;
    }
    private static unsafe bool IsDisabled(System.Runtime.InteropServices.SafeHandle handle)
    {
        PInvoke.QueryServiceConfig(handle, null, 0, out var size);
        if (size is 0 or > 65536) throw new ApiException(400, "serviceConfigUnavailable", "无法读取有效的服务启动配置，未开始停止操作。");
        var bytes = new byte[size];
        fixed (byte* buffer = bytes)
        {
            if (!PInvoke.QueryServiceConfig(handle, (QUERY_SERVICE_CONFIGW*)buffer, size, out _)) NativeError("无法核验服务启动配置，未开始停止操作");
            return ((QUERY_SERVICE_CONFIGW*)buffer)->dwStartType == SERVICE_START_TYPE.SERVICE_DISABLED;
        }
    }
    private static void ValidateName(string name) { if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || name.IndexOfAny(['\0', '/', '\\']) >= 0) throw new ApiException(400, "invalidServiceName", "服务名无效。"); }
    private static void NativeError(string action)
    {
        var code = Marshal.GetLastPInvokeError(); var error = NativeSupport.Error(code, action);
        throw new ApiException(code == 5 ? 403 : code == 1060 ? 404 : 400, code == 1060 ? "serviceNotFound" : error.Code, error.Message, code);
    }
}
