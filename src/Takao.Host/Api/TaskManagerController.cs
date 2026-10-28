using Microsoft.AspNetCore.Mvc;
using Takao.Host.Models;
using Takao.Host.Services;
using Takao.Host.Native;

namespace Takao.Host.Api;

[ApiController]
[Route("api")]
public sealed class TaskManagerController(SnapshotService snapshots, ProcessActionService actions, LocalAccessGuard guard, DesktopWindowService desktop, PreferencesService preferences,
    ProcessQueryService queries, ProcessConnections connections, ProcessIconService icons, ProcessManagementService management, ShellActionService shell, ServiceActionService services, TaskManagerIntegrationService integration) : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { platform = "Windows", version = "1.1.1", ready = snapshots.Current is not null, processId = Environment.ProcessId, nativeSource = "Microsoft.Windows.CsWin32", json = "Newtonsoft.Json" });

    [HttpGet("bootstrap")]
    public IActionResult Bootstrap() => Ok(new { token = guard.Token, settings = snapshots.Settings, capabilities = SnapshotService.Capabilities, version = "1.1.1", desktop = desktop.Available, hostPid = Environment.ProcessId, currentUserName = CurrentIdentity.Name, currentSessionId = CurrentIdentity.SessionId });

    [HttpGet("snapshot")]
    public IActionResult Snapshot()
    {
        var representation = snapshots.Representation;
        if (representation is null) return StatusCode(503, new ErrorEnvelope(new("warmingUp", "Windows 首次采样尚未完成，请稍后重试。")));
        Response.Headers.ETag = representation.ETag;
        if (Request.Headers.IfNoneMatch.ToString().Split(',').Any(value => value.Trim() is "*" || value.Trim().Replace("W/", "", StringComparison.Ordinal) == representation.ETag.Replace("W/", "", StringComparison.Ordinal))) return StatusCode(304);
        return File(representation.Utf8, "application/json; charset=utf-8");
    }

    [HttpGet("preferences")]
    public IActionResult Preferences() => Ok(preferences.Current);

    [HttpGet("hwinfo/sensors")]
    public IActionResult HwInfoSensors() => Ok(snapshots.HwInfo);

    [HttpPost("preferences")]
    public IActionResult Preferences(UiPreferences request) => Ok(preferences.Save(request));

    [HttpGet("desktop/task-manager-integration")]
    public IActionResult TaskManagerIntegration() => Ok(integration.Read());

    [HttpPost("desktop/task-manager-integration")]
    public IActionResult TaskManagerIntegration(TaskManagerIntegrationRequest request) => Ok(integration.Apply(request));

    [HttpPost("processes/start")]
    public IActionResult Start(StartRequest request) => Ok(actions.Start(request));

    [HttpPost("processes/{pid:long}/terminate")]
    public IActionResult Terminate(uint pid, TerminateRequest request) => Ok(actions.Terminate(pid, request));

    [HttpGet("processes/{pid:long}/details")]
    public IActionResult Details(uint pid, [FromQuery] string creationTimeFileTime) => Ok(queries.Details(pid, creationTimeFileTime));

    [HttpGet("processes/{pid:long}/connections")]
    public IActionResult Connections(uint pid, [FromQuery] string creationTimeFileTime) => Ok(connections.Read(pid, creationTimeFileTime));

    [HttpGet("processes/{pid:long}/icon")]
    public async Task<IActionResult> Icon(uint pid, [FromQuery] string creationTimeFileTime)
    { var png = await icons.ReadAsync(pid, creationTimeFileTime); return png is null ? NoContent() : File(png, "image/png"); }

    [HttpGet("application-icon")]
    public async Task<IActionResult> ApplicationIcon([FromQuery] string kind, [FromQuery] string id)
    { var png = await icons.ReadApplicationAsync(kind, id, snapshots.Current); return png is null ? NoContent() : File(png, "image/png"); }

    [HttpPost("processes/{pid:long}/priority")]
    public IActionResult Priority(uint pid, PriorityRequest request) => Ok(management.Priority(pid, request));

    [HttpPost("processes/{pid:long}/tree-preview")]
    public IActionResult TreePreview(uint pid, TerminateRequest request) => Ok(management.Preview(pid, request.CreationTimeFileTime));

    [HttpPost("processes/{pid:long}/terminate-tree")]
    public IActionResult TerminateTree(uint pid, TerminateTreeRequest request) => Ok(management.TerminateTree(pid, request));

    [HttpGet("processes/{pid:long}/efficiency")]
    public IActionResult Efficiency(uint pid, [FromQuery] string creationTimeFileTime) => Ok(management.Efficiency(pid, creationTimeFileTime));

    [HttpPost("processes/{pid:long}/efficiency")]
    public IActionResult Efficiency(uint pid, EfficiencyRequest request) => Ok(management.Efficiency(pid, request));

    [HttpPost("processes/{pid:long}/file-action")]
    public async Task<IActionResult> ProcessFile(uint pid, FileActionRequest request) => Ok(new { message = await shell.ProcessFileAsync(pid, request) });

    [HttpPost("desktop/browse-executable")]
    public async Task<IActionResult> BrowseExecutable() => Ok(await desktop.BrowseExecutableAsync());

    [HttpGet("services/{name}/details")]
    public IActionResult ServiceDetails(string name) => Ok(services.Details(name));

    [HttpPost("services/{name}/control")]
    public async Task<IActionResult> ServiceControl(string name, ServiceControlRequest request) => Ok(await services.ControlAsync(name, request));

    [HttpPost("startup/open-location")]
    public async Task<IActionResult> StartupLocation(StartupLocationRequest request) => Ok(new { message = await shell.StartupLocationAsync(request.Id) });

    [HttpPost("desktop/open-system-location")]
    public async Task<IActionResult> SystemLocation(SystemLocationRequest request) => Ok(new { message = await shell.OpenSystemLocationAsync(request.Location) });

    [HttpGet("desktop/window-state")]
    public async Task<IActionResult> WindowState() => Ok(await desktop.StateAsync());

    [HttpPost("settings")]
    public IActionResult Settings(SettingsRequest request) => Ok(snapshots.ApplySettings(request));

    [HttpPost("history/reset")]
    public IActionResult ResetHistory() { snapshots.ResetHistory(); return Ok(new { sessionStartedAtUtc = snapshots.Current?.SessionStartedAtUtc, message = "本次会话历史已清除。" }); }

    [HttpPost("refresh")]
    public IActionResult Refresh() { snapshots.RefreshSlowData(); return Ok(new { message = "已安排在下一次有效采样刷新服务、启动项和会话。" }); }

    [HttpPost("window/{command}")]
    public async Task<IActionResult> Window(string command) { await desktop.ExecuteAsync(command); return Ok(new { message = "窗口操作已发送。" }); }
}

internal static class CurrentIdentity
{
    // Current-user filters refer to the host account, not an arbitrary active WTS session.
    internal static readonly string? Name = ReadName();
    internal static readonly uint? SessionId = ReadSession();
    private static uint? ReadSession() => Windows.Win32.PInvoke.ProcessIdToSessionId((uint)Environment.ProcessId, out var session) ? session : null;
    private static string? ReadName()
    {
        var buffer = new char[32768]; uint size = (uint)buffer.Length;
        return Windows.Win32.PInvoke.GetUserNameEx(Windows.Win32.Security.Authentication.Identity.EXTENDED_NAME_FORMAT.NameSamCompatible, buffer, ref size) && size > 0 ? new string(buffer, 0, (int)size).TrimEnd('\0') : null;
    }
}
