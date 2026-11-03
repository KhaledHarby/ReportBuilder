using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReportBuilder.Api.Contracts;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/lookups")]
public sealed class LookupsController : ControllerBase
{
    private readonly ILookupService _lookups;

    public LookupsController(ILookupService lookups)
    {
        _lookups = lookups;
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<LookupDto>>> List(CancellationToken cancellationToken)
    {
        if (!CanListLookups())
        {
            return Forbid();
        }

        var lookups = await _lookups.ListAsync(cancellationToken);
        return Ok(lookups.Select(LookupDto.From).ToList());
    }

    [HttpGet("objects")]
    [Authorize(Policy = PermissionCodes.LookupsView)]
    public async Task<ActionResult<IReadOnlyList<LookupObjectDto>>> Objects(
        [FromQuery] string schema,
        CancellationToken cancellationToken)
    {
        var objects = await _lookups.ListObjectsAsync(schema ?? string.Empty, cancellationToken);
        return Ok(objects.Select(item => new LookupObjectDto
        {
            SchemaName = item.SchemaName,
            ObjectName = item.ObjectName,
            ObjectType = item.ObjectType
        }).ToList());
    }

    [HttpGet("columns")]
    [Authorize(Policy = PermissionCodes.LookupsView)]
    public async Task<ActionResult<IReadOnlyList<LookupColumnDto>>> Columns(
        [FromQuery] string schema,
        [FromQuery] string objectName,
        CancellationToken cancellationToken)
    {
        var columns = await _lookups.PreviewColumnsAsync(schema, objectName, cancellationToken);
        return Ok(columns.Select(column => new LookupColumnDto
        {
            Name = column.Name,
            SqlType = column.SqlType,
            DisplayName = column.DisplayName,
            DisplayNameArabic = column.DisplayNameArabic,
            Locale = column.Locale,
            Included = column.Included,
            ShowInDesigner = column.ShowInDesigner
        }).ToList());
    }

    [HttpPost("columns")]
    [Authorize(Policy = PermissionCodes.LookupsView)]
    public async Task<ActionResult<IReadOnlyList<LookupColumnDto>>> PreviewQuery(
        [FromBody] LookupQueryPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var columns = await _lookups.PreviewQueryColumnsAsync(request.SqlText ?? string.Empty, cancellationToken);
        return Ok(columns.Select(column => new LookupColumnDto
        {
            Name = column.Name,
            SqlType = column.SqlType,
            DisplayName = column.DisplayName,
            DisplayNameArabic = column.DisplayNameArabic,
            Locale = column.Locale,
            Included = column.Included,
            ShowInDesigner = column.ShowInDesigner
        }).ToList());
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.LookupsUpdate)]
    public async Task<ActionResult<LookupDto>> Save(
        [FromBody] LookupWriteRequest request,
        CancellationToken cancellationToken)
    {
        var saved = await _lookups.SaveAsync(request, cancellationToken);
        return Ok(LookupDto.From(saved));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionCodes.LookupsUpdate)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _lookups.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private bool CanListLookups()
        => User.HasClaim("perm", PermissionCodes.LookupsView)
            || User.HasClaim("perm", PermissionCodes.LookupsUpdate)
            || User.HasClaim("perm", PermissionCodes.DatasetsView);
}
