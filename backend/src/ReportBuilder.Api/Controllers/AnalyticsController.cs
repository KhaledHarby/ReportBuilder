using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReportBuilder.Api.Contracts;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/analytics")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsDashboardService _dashboards;
    private readonly IAnalyticsQueryService _queries;
    private readonly IActivityLog _log;
    private readonly ICurrentUser _currentUser;

    public AnalyticsController(
        IAnalyticsDashboardService dashboards,
        IAnalyticsQueryService queries,
        IActivityLog log,
        ICurrentUser currentUser)
    {
        _dashboards = dashboards;
        _queries = queries;
        _log = log;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.AnalyticsView)]
    public async Task<ActionResult<IReadOnlyList<AnalyticsDashboardSummaryDto>>> List(CancellationToken cancellationToken)
    {
        var items = await _dashboards.ListAsync(cancellationToken);
        return Ok(items.Select(item => AnalyticsDashboardSummaryDto.From(item, _currentUser)).ToList());
    }

    [HttpGet("people")]
    [Authorize(Policy = PermissionCodes.AnalyticsUpdate)]
    public async Task<ActionResult<IReadOnlyList<ReportPerson>>> People(CancellationToken cancellationToken)
        => Ok(await _dashboards.ListPeopleAsync(cancellationToken));

    [HttpGet("access-roles")]
    [Authorize(Policy = PermissionCodes.AnalyticsUpdate)]
    public async Task<ActionResult<IReadOnlyList<ReportAccessRoleDto>>> AccessRoles(CancellationToken cancellationToken)
    {
        var roles = await _dashboards.ListRolesAsync(cancellationToken);
        return Ok(roles.Select(ReportAccessRoleDto.From).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCodes.AnalyticsView)]
    public async Task<ActionResult<AnalyticsDashboardDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var item = await _dashboards.GetAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        await LogDashboardAsync(
            item,
            "analytics.view",
            "Opened analytics dashboard",
            null,
            cancellationToken);
        return Ok(AnalyticsDashboardDetailDto.From(item, _currentUser));
    }

    [HttpPost("{id:guid}/query")]
    [Authorize(Policy = PermissionCodes.AnalyticsView)]
    public async Task<ActionResult<AnalyticsSnapshotResult>> Query(
        Guid id,
        [FromBody] AnalyticsQueryRequest body,
        CancellationToken cancellationToken)
    {
        var request = body ?? new AnalyticsQueryRequest();
        var result = await _queries.QuerySnapshotAsync(id, request, cancellationToken);
        if (request.Track != false)
        {
            var selectionCount = request.Selections.Sum(pair => pair.Value.Count);
            await _log.WriteAsync(
                new ActivityEvent
                {
                    Category = ActivityCategories.Analytics,
                    Action = "analytics.query",
                    Message = "Queried analytics dashboard.",
                    EntityType = "AnalyticsDashboard",
                    EntityId = id,
                    Details = $"{{\"selections\":{selectionCount}}}"
                },
                cancellationToken);
        }

        return Ok(result);
    }

    [HttpPost("{id:guid}/data")]
    [Authorize(Policy = PermissionCodes.AnalyticsView)]
    public async Task<ActionResult<AnalyticsDataResult>> Data(
        Guid id,
        [FromBody] AnalyticsQueryRequest body,
        CancellationToken cancellationToken)
        => Ok(await _queries.QueryDataAsync(id, body ?? new AnalyticsQueryRequest(), cancellationToken));

    [HttpPost("{id:guid}/map")]
    [Authorize(Policy = PermissionCodes.AnalyticsView)]
    public async Task<ActionResult<AnalyticsMapResult>> Map(
        Guid id,
        [FromBody] AnalyticsQueryRequest body,
        CancellationToken cancellationToken)
        => Ok(await _queries.QueryMapAsync(id, body ?? new AnalyticsQueryRequest(), cancellationToken));

    [HttpPost("{id:guid}/search")]
    [Authorize(Policy = PermissionCodes.AnalyticsView)]
    public async Task<ActionResult<AnalyticsSearchResult>> Search(
        Guid id,
        [FromBody] AnalyticsValueSearchRequest body,
        CancellationToken cancellationToken)
        => Ok(await _queries.SearchValuesAsync(id, body ?? new AnalyticsValueSearchRequest(), cancellationToken));

    [HttpPost("{id:guid}/export")]
    [Authorize(Policy = PermissionCodes.AnalyticsView)]
    public async Task<IActionResult> Export(
        Guid id,
        [FromBody] AnalyticsQueryRequest body,
        CancellationToken cancellationToken)
    {
        var (fileName, content, contentType) = await _queries.ExportCsvAsync(
            id,
            body ?? new AnalyticsQueryRequest(),
            cancellationToken);
        var item = await _dashboards.GetAsync(id, cancellationToken);
        if (item is not null)
        {
            await LogDashboardAsync(
                item,
                "analytics.export",
                "Exported analytics dashboard",
                "{\"format\":\"csv\"}",
                cancellationToken);
        }

        return File(content, contentType, fileName);
    }

    [HttpPost("{id:guid}/export-event")]
    [Authorize(Policy = PermissionCodes.AnalyticsView)]
    public async Task<IActionResult> ExportEvent(
        Guid id,
        [FromBody] AnalyticsExportEventDto? body,
        CancellationToken cancellationToken)
    {
        var item = await _dashboards.GetAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var format = string.IsNullOrWhiteSpace(body?.Format) ? "screen" : body.Format.Trim().ToLowerInvariant();
        if (format is not ("pdf" or "pptx" or "csv" or "screen"))
        {
            format = "screen";
        }

        await LogDashboardAsync(
            item,
            "analytics.export",
            "Exported analytics dashboard",
            $"{{\"format\":\"{format}\"}}",
            cancellationToken);
        return NoContent();
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.AnalyticsCreate)]
    public async Task<ActionResult<AnalyticsDashboardDetailDto>> Create(
        [FromBody] AnalyticsDashboardWriteDto body,
        CancellationToken cancellationToken)
    {
        var created = await _dashboards.CreateAsync(body.ToRequest(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, AnalyticsDashboardDetailDto.From(created, _currentUser));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCodes.AnalyticsUpdate)]
    public async Task<ActionResult<AnalyticsDashboardDetailDto>> Update(
        Guid id,
        [FromBody] AnalyticsDashboardWriteDto body,
        CancellationToken cancellationToken)
    {
        var updated = await _dashboards.UpdateAsync(id, body.ToRequest(), cancellationToken);
        return updated is null ? NotFound() : Ok(AnalyticsDashboardDetailDto.From(updated, _currentUser));
    }

    [HttpPost("{id:guid}/delete")]
    [Authorize(Policy = PermissionCodes.AnalyticsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _dashboards.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private Task LogDashboardAsync(
        AnalyticsDashboard item,
        string action,
        string verb,
        string? details,
        CancellationToken cancellationToken)
        => _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Analytics,
                Action = action,
                Message = $"{verb} {item.Name}.",
                EntityType = "AnalyticsDashboard",
                EntityId = item.Id,
                EntityName = item.Name,
                Details = details
            },
            cancellationToken);
}

public sealed class AnalyticsExportEventDto
{
    public string? Format { get; set; }
}

public sealed class AnalyticsDashboardWriteDto
{
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public string? Description { get; set; }
    public string? DescriptionArabic { get; set; }
    public AnalyticsDashboardConfig Config { get; set; } = new();
    public ReportStatus? Status { get; set; }
    public bool? IsRestricted { get; set; }
    public string? AccessMode { get; set; }
    public List<Guid>? AccessUserIds { get; set; }
    public List<Guid>? AccessRoleIds { get; set; }

    public AnalyticsDashboardWriteRequest ToRequest()
        => new()
        {
            Name = Name,
            NameArabic = NameArabic,
            Description = Description,
            DescriptionArabic = DescriptionArabic,
            Config = Config,
            Status = Status,
            IsRestricted = IsRestricted,
            AccessMode = AccessMode,
            AccessUserIds = AccessUserIds,
            AccessRoleIds = AccessRoleIds
        };
}

public sealed class AnalyticsDashboardSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? NameArabic { get; init; }
    public string? Description { get; init; }
    public ReportStatus Status { get; init; }
    public bool IsRestricted { get; init; }
    public string AccessMode { get; init; } = ReportAccessModes.Everyone;
    public bool CanEdit { get; init; }
    public bool CanDelete { get; init; }
    public int SourceCount { get; init; }
    public int DimensionCount { get; init; }
    public int MeasureCount { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public static AnalyticsDashboardSummaryDto From(AnalyticsDashboard entity, ICurrentUser user)
    {
        var config = AnalyticsConfigJson.Deserialize(entity.ConfigJson);
        var canDesign = AnalyticsAccessRules.CanDesign(entity, user);
        return new AnalyticsDashboardSummaryDto
        {
            Id = entity.Id,
            Name = entity.Name,
            NameArabic = entity.NameArabic,
            Description = entity.Description,
            Status = entity.Status,
            IsRestricted = entity.IsRestricted,
            AccessMode = ReportAccessModes.Normalize(entity.AccessMode, entity.IsRestricted),
            CanEdit = canDesign && user.Has(PermissionCodes.AnalyticsUpdate),
            CanDelete = canDesign && user.Has(PermissionCodes.AnalyticsDelete),
            SourceCount = config.Sources.Count,
            DimensionCount = config.Dimensions.Count,
            MeasureCount = config.Measures.Count,
            UpdatedAt = entity.UpdatedAt
        };
    }
}

public sealed class AnalyticsDashboardDetailDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? NameArabic { get; init; }
    public string? Description { get; init; }
    public string? DescriptionArabic { get; init; }
    public ReportStatus Status { get; init; }
    public bool IsRestricted { get; init; }
    public string AccessMode { get; init; } = ReportAccessModes.Everyone;
    public bool CanEdit { get; init; }
    public bool CanDelete { get; init; }
    public IReadOnlyList<Guid> AccessUserIds { get; init; } = [];
    public IReadOnlyList<Guid> AccessRoleIds { get; init; } = [];
    public IReadOnlyList<ReportAccessRoleDto> AccessRoles { get; init; } = [];
    public AnalyticsDashboardConfig Config { get; init; } = new();
    public Guid? CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public static AnalyticsDashboardDetailDto From(AnalyticsDashboard entity, ICurrentUser user)
    {
        var canDesign = AnalyticsAccessRules.CanDesign(entity, user);
        return new()
        {
            Id = entity.Id,
            Name = entity.Name,
            NameArabic = entity.NameArabic,
            Description = entity.Description,
            DescriptionArabic = entity.DescriptionArabic,
            Status = entity.Status,
            IsRestricted = entity.IsRestricted,
            AccessMode = ReportAccessModes.Normalize(entity.AccessMode, entity.IsRestricted),
            CanEdit = canDesign && user.Has(PermissionCodes.AnalyticsUpdate),
            CanDelete = canDesign && user.Has(PermissionCodes.AnalyticsDelete),
            AccessUserIds = entity.Access.Select(row => row.UserId).ToList(),
            AccessRoleIds = entity.RoleAccess.Select(row => row.RoleId).ToList(),
            AccessRoles = entity.RoleAccess.Select(ReportAccessRoleDto.From).ToList(),
            Config = AnalyticsConfigJson.Deserialize(entity.ConfigJson),
            CreatedBy = entity.CreatedBy,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
