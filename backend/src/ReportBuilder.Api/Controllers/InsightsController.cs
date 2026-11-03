using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/insights")]
public sealed class InsightsController : ControllerBase
{
    private readonly IReportInsightsService _insights;

    public InsightsController(IReportInsightsService insights)
    {
        _insights = insights;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.InsightsView)]
    public async Task<ActionResult<ReportInsightsResult>> Get(
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var result = await _insights.GetAsync(new ReportInsightsQuery { Days = days }, cancellationToken);
        return Ok(result);
    }
}
