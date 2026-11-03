using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReportBuilder.Api.Contracts;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/logs")]
public sealed class LogsController : ControllerBase
{
    private readonly IActivityLog _logs;

    public LogsController(IActivityLog logs)
    {
        _logs = logs;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.LogsView)]
    public async Task<ActionResult<ActivityLogPageDto>> List(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromQuery] string? q = null,
        [FromQuery] string? level = null,
        [FromQuery] string? category = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? actor = null,
        CancellationToken cancellationToken = default)
    {
        var page = await _logs.ListAsync(
            new ActivityLogQuery
            {
                Skip = skip,
                Take = take,
                Search = q,
                Level = level,
                Category = category,
                From = from,
                To = to,
                Actor = actor
            },
            cancellationToken);
        return Ok(ActivityLogPageDto.From(page));
    }

    [HttpGet("settings")]
    [Authorize(Policy = PermissionCodes.LogsView)]
    public async Task<ActionResult<ActivityLogSettingsDto>> Settings(CancellationToken cancellationToken)
        => Ok(ActivityLogSettingsDto.From(await _logs.GetSettingsAsync(cancellationToken)));

    [HttpPut("settings")]
    [HttpPost("settings")]
    [Authorize(Policy = PermissionCodes.LogsManage)]
    public async Task<ActionResult<ActivityLogSettingsDto>> UpdateSettings(
        [FromBody] ActivityLogSettingsWriteDto request,
        CancellationToken cancellationToken)
        => Ok(ActivityLogSettingsDto.From(
            await _logs.UpdateSettingsAsync(request.RetentionDays, cancellationToken)));
}
