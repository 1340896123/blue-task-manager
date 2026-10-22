using Microsoft.AspNetCore.Mvc;
using TaskManager.Host.Models;
using TaskManager.Host.Services;

namespace TaskManager.Host.Api;

[ApiController]
[Route("api")]
public sealed class TaskManagerController(SnapshotService snapshots, ProcessActionService actions, LocalAccessGuard guard, DesktopWindowService desktop, PreferencesService preferences) : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { platform = "Windows", version = "1.0.0", ready = snapshots.Current is not null, processId = Environment.ProcessId, nativeSource = "Microsoft.Windows.CsWin32", json = "Newtonsoft.Json" });

    [HttpGet("bootstrap")]
    public IActionResult Bootstrap() => Ok(new { token = guard.Token, settings = snapshots.Settings, capabilities = SnapshotService.Capabilities, version = "1.0.0", desktop = desktop.Available });

    [HttpGet("snapshot")]
    public IActionResult Snapshot()
    {
        var representation = snapshots.Representation;
        if (representation is null) return StatusCode(503, new ErrorEnvelope(new("warmingUp", "Windows 首次采样尚未完成，请稍后重试。")));
        Response.Headers.ETag = representation.ETag;
        if (Request.Headers.IfNoneMatch.ToString().Split(',').Any(value => value.Trim() is "*" || value.Trim().Replace("W/", "", StringComparison.Ordinal) == representation.ETag)) return StatusCode(304);
        return File(representation.Utf8, "application/json; charset=utf-8");
    }

    [HttpGet("preferences")]
    public IActionResult Preferences() => Ok(preferences.Current);

    [HttpPost("preferences")]
    public IActionResult Preferences(UiPreferences request) => Ok(preferences.Save(request));

    [HttpPost("processes/start")]
    public IActionResult Start(StartRequest request) => Ok(actions.Start(request));

    [HttpPost("processes/{pid:long}/terminate")]
    public IActionResult Terminate(uint pid, TerminateRequest request) => Ok(actions.Terminate(pid, request));

    [HttpPost("settings")]
    public IActionResult Settings(SettingsRequest request) => Ok(snapshots.ApplySettings(request));

    [HttpPost("history/reset")]
    public IActionResult ResetHistory() { snapshots.ResetHistory(); return Ok(new { sessionStartedAtUtc = snapshots.Current?.SessionStartedAtUtc, message = "本次会话历史已清除。" }); }

    [HttpPost("refresh")]
    public IActionResult Refresh() { snapshots.RefreshSlowData(); return Ok(new { message = "已安排在下一次有效采样刷新服务、启动项和会话。" }); }

    [HttpPost("window/{command}")]
    public async Task<IActionResult> Window(string command) { await desktop.ExecuteAsync(command); return Ok(new { message = "窗口操作已发送。" }); }
}
