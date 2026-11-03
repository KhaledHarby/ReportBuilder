using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReportBuilder.Api.Contracts;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/report-categories")]
public sealed class ReportCategoriesController : ControllerBase
{
    private readonly IReportCategoryService _categories;

    public ReportCategoriesController(IReportCategoryService categories)
    {
        _categories = categories;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.ReportsView)]
    public async Task<ActionResult<IReadOnlyList<ReportCategoryDto>>> List(CancellationToken cancellationToken)
    {
        var categories = await _categories.ListAsync(cancellationToken);
        return Ok(categories.Select(ReportCategoryDto.From).ToList());
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<ActionResult<ReportCategoryDto>> Create(
        [FromBody] ReportCategoryWriteDto request,
        CancellationToken cancellationToken)
    {
        var created = await _categories.CreateAsync(ToWrite(request), cancellationToken);
        return CreatedAtAction(nameof(List), ReportCategoryDto.From(created));
    }

    [HttpPut("{id:guid}")]
    [HttpPost("{id:guid}")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<ActionResult<ReportCategoryDto>> Update(
        Guid id,
        [FromBody] ReportCategoryWriteDto request,
        CancellationToken cancellationToken)
    {
        var updated = await _categories.UpdateAsync(id, ToWrite(request), cancellationToken);
        return updated is null ? NotFound() : Ok(ReportCategoryDto.From(updated));
    }

    [HttpDelete("{id:guid}")]
    [HttpPost("{id:guid}/delete")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _categories.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private static ReportCategoryWriteRequest ToWrite(ReportCategoryWriteDto request) => new()
    {
        Name = request.Name,
        NameArabic = request.NameArabic,
        SortOrder = request.SortOrder
    };
}
