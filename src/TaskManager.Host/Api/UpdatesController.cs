using Microsoft.AspNetCore.Mvc;
using TaskManager.Host.Models;
using TaskManager.Host.Services;

namespace TaskManager.Host.Api;

[ApiController]
[Route("api/updates")]
public sealed class UpdatesController(ApplicationUpdateService updates) : ControllerBase
{
    [HttpGet] public IActionResult State() => Ok(updates.Read());
    [HttpPost("check")] public IActionResult Check() => Accepted(updates.BeginCheck());
    [HttpPost("download")] public IActionResult Download(UpdateRevisionRequest request) => Accepted(updates.BeginDownload(request));
    [HttpPost("cancel")] public IActionResult Cancel(UpdateRevisionRequest request) => Ok(updates.Cancel(request));
    [HttpPost("install")]
    public async Task<IActionResult> Install(UpdateInstallRequest request)
    {
        var state = await updates.PrepareInstallAsync(request);
        if (request.Mode == "now") Response.OnCompleted(updates.ApplyAfterResponseAsync);
        return Accepted(state);
    }
}
