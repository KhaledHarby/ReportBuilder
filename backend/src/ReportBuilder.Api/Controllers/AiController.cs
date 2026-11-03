using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReportBuilder.Api.Contracts;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ai")]
public sealed class AiController : ControllerBase
{
    private readonly IReportAiService _ai;

    public AiController(IReportAiService ai)
    {
        _ai = ai;
    }

    [HttpGet("status")]
    public async Task<ActionResult<AiStatusDto>> Status(CancellationToken cancellationToken)
    {
        var status = await _ai.GetStatusAsync(cancellationToken);
        return Ok(new AiStatusDto
        {
            Configured = status.Configured
        });
    }

    [HttpPost("assist")]
    [Authorize(Policy = PermissionCodes.AiUse)]
    public async Task<ActionResult<AiAssistDto>> Assist([FromBody] AiAssistRequest request, CancellationToken cancellationToken)
    {
        var result = await _ai.AssistAsync(new ReportAiAssistRequest
        {
            Prompt = request.Prompt,
            DatasetId = request.DatasetId,
            Name = request.Name,
            NameArabic = request.NameArabic,
            Description = request.Description,
            DescriptionArabic = request.DescriptionArabic,
            Layout = request.Layout
        }, cancellationToken);

        return Ok(new AiAssistDto
        {
            Name = result.Name,
            NameArabic = result.NameArabic,
            Description = result.Description,
            DescriptionArabic = result.DescriptionArabic,
            DatasetId = result.DatasetId,
            DatasetName = result.DatasetName,
            Layout = result.Layout,
            Explanation = result.Explanation
        });
    }
}
