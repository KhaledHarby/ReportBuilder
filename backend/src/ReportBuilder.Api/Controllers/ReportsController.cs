using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using ReportBuilder.Api.Contracts;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly IReportTemplateService _templates;
    private readonly IReportQueryService _queries;
    private readonly IDatasetCatalogService _catalog;
    private readonly IReportScheduleService _schedules;
    private readonly IReportScheduleRunner _scheduleRunner;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityLog _log;

    public ReportsController(
        IReportTemplateService templates,
        IReportQueryService queries,
        IDatasetCatalogService catalog,
        IReportScheduleService schedules,
        IReportScheduleRunner scheduleRunner,
        ICurrentUser currentUser,
        IActivityLog log)
    {
        _templates = templates;
        _queries = queries;
        _catalog = catalog;
        _schedules = schedules;
        _scheduleRunner = scheduleRunner;
        _currentUser = currentUser;
        _log = log;
    }

    [HttpGet("schedules/health")]
    [Authorize(Policy = PermissionCodes.ReportsDelivery)]
    public async Task<ActionResult<IReadOnlyList<ReportDeliveryRow>>> DeliveryHealth(CancellationToken cancellationToken)
        => Ok(await _schedules.ListHealthAsync(cancellationToken));

    [HttpGet("{id:guid}/schedule")]
    [Authorize(Policy = PermissionCodes.ReportsSchedule)]
    public async Task<ActionResult<ReportScheduleModel>> GetSchedule(Guid id, CancellationToken cancellationToken)
        => Ok(await _schedules.GetAsync(id, cancellationToken));

    [HttpPut("{id:guid}/schedule")]
    [Authorize(Policy = PermissionCodes.ReportsSchedule)]
    public async Task<ActionResult<ReportScheduleModel>> SaveSchedule(
        Guid id,
        [FromBody] ReportScheduleWrite request,
        CancellationToken cancellationToken)
        => Ok(await _schedules.SaveAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/schedule/send")]
    [Authorize(Policy = PermissionCodes.ReportsSchedule)]
    public async Task<ActionResult<ReportScheduleModel>> SendSchedule(Guid id, CancellationToken cancellationToken)
    {
        await _schedules.GetAsync(id, cancellationToken);
        await _scheduleRunner.SendNowAsync(id, cancellationToken);
        return Ok(await _schedules.GetAsync(id, cancellationToken));
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.ReportsView)]
    public async Task<ActionResult<IReadOnlyList<ReportSummaryDto>>> List(CancellationToken cancellationToken)
    {
        var reports = await _templates.ListAsync(cancellationToken);
        var visible = reports.Where(report => ReportAccessRules.CanView(report, _currentUser)).ToList();
        var names = await NamesAsync(visible, cancellationToken);
        return Ok(visible.Select(report => ReportSummaryDto.From(report, _currentUser, names)).ToList());
    }

    [HttpGet("people")]
    [Authorize(Policy = PermissionCodes.ReportsView)]
    public async Task<ActionResult<IReadOnlyList<ReportPersonDto>>> People(CancellationToken cancellationToken)
    {
        var people = await _templates.ListPeopleAsync(cancellationToken);
        return Ok(people.Select(ReportPersonDto.From).ToList());
    }

    [HttpGet("access-roles")]
    [Authorize(Policy = PermissionCodes.ReportsView)]
    public async Task<ActionResult<IReadOnlyList<ReportAccessRoleDto>>> AccessRoles(CancellationToken cancellationToken)
    {
        var roles = await _templates.ListRolesAsync(cancellationToken);
        return Ok(roles.Select(ReportAccessRoleDto.From).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCodes.ReportsView)]
    public async Task<ActionResult<ReportDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var report = await VisibleAsync(id, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        return Ok(await DetailAsync(report, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.ReportsCreate)]
    public async Task<ActionResult<ReportDetailDto>> Create([FromBody] ReportWriteRequest request, CancellationToken cancellationToken)
    {
        var created = await _templates.CreateAsync(ToWrite(request), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, await DetailAsync(created, cancellationToken));
    }

    [HttpPost("{id:guid}/duplicate")]
    [Authorize(Policy = PermissionCodes.ReportsCreate)]
    public async Task<ActionResult<ReportDetailDto>> Duplicate(Guid id, CancellationToken cancellationToken)
    {
        var existing = await VisibleAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var copy = await _templates.DuplicateAsync(id, cancellationToken);
        return copy is null
            ? NotFound()
            : CreatedAtAction(nameof(Get), new { id = copy.Id }, await DetailAsync(copy, cancellationToken));
    }

    [HttpPut("{id:guid}")]
    [HttpPost("{id:guid}")]
    [Authorize(Policy = PermissionCodes.ReportsUpdate)]
    public async Task<ActionResult<ReportDetailDto>> Update(Guid id, [FromBody] ReportWriteRequest request, CancellationToken cancellationToken)
    {
        var existing = await VisibleAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (DenyDesign(existing) is { } denied)
        {
            return denied;
        }

        var updated = await _templates.UpdateAsync(id, ToWrite(request), cancellationToken);
        return updated is null ? NotFound() : Ok(await DetailAsync(updated, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [HttpPost("{id:guid}/delete")]
    [Authorize(Policy = PermissionCodes.ReportsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var existing = await VisibleAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (DenyDesign(existing) is { } denied)
        {
            return denied;
        }

        return await _templates.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("expressions/validate")]
    [Authorize(Policy = PermissionCodes.ReportsView)]
    public async Task<IActionResult> ValidateExpression(
        [FromBody] ValidateExpressionRequest request,
        CancellationToken cancellationToken)
    {
        var dataset = await _catalog.GetAsync(request.DatasetId, cancellationToken);
        if (dataset is null)
        {
            return NotFound();
        }

        AnalyticsExpression.Validate(request.Expression ?? string.Empty, dataset, request.AllowAggregates);
        return NoContent();
    }

    [HttpPost("preview")]
    [Authorize(Policy = PermissionCodes.ReportsView)]
    public async Task<ActionResult<ReportResultDto>> Preview([FromBody] PreviewRequest request, CancellationToken cancellationToken)
    {
        var result = await _queries.PreviewAsync(request.DatasetId, request.Layout, request.Skip, request.Take, request.Lang, cancellationToken);
        return Ok(ReportResultDto.From(result));
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = PermissionCodes.ReportsUpdate)]
    public async Task<ActionResult<ReportDetailDto>> Publish(Guid id, CancellationToken cancellationToken)
    {
        var existing = await VisibleAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (DenyDesign(existing) is { } denied)
        {
            return denied;
        }

        var published = await _templates.SetStatusAsync(id, ReportStatus.Published, cancellationToken);
        return published is null ? NotFound() : Ok(await DetailAsync(published, cancellationToken));
    }

    [HttpPost("{id:guid}/unpublish")]
    [Authorize(Policy = PermissionCodes.ReportsUpdate)]
    public async Task<ActionResult<ReportDetailDto>> Unpublish(Guid id, CancellationToken cancellationToken)
    {
        var existing = await VisibleAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (DenyDesign(existing) is { } denied)
        {
            return denied;
        }

        var unpublished = await _templates.SetStatusAsync(id, ReportStatus.Draft, cancellationToken);
        return unpublished is null ? NotFound() : Ok(await DetailAsync(unpublished, cancellationToken));
    }

    [HttpGet("{id:guid}/revisions")]
    [Authorize(Policy = PermissionCodes.ReportsUpdate)]
    public async Task<ActionResult<ReportHistory>> Revisions(Guid id, CancellationToken cancellationToken)
    {
        var existing = await VisibleAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (DenyDesign(existing) is { } denied)
        {
            return denied;
        }

        return Ok(await _templates.ListRevisionsAsync(id, cancellationToken));
    }

    [HttpPost("{id:guid}/revisions/{version:int}/restore")]
    [Authorize(Policy = PermissionCodes.ReportsUpdate)]
    public async Task<ActionResult<ReportDetailDto>> RestoreRevision(
        Guid id,
        int version,
        CancellationToken cancellationToken)
    {
        var existing = await VisibleAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (DenyDesign(existing) is { } denied)
        {
            return denied;
        }

        var restored = await _templates.RestoreRevisionAsync(id, version, cancellationToken);
        return restored is null ? NotFound() : Ok(await DetailAsync(restored, cancellationToken));
    }

    [HttpGet("{id:guid}/filter-values/{field}")]
    [Authorize(Policy = PermissionCodes.ReportsView)]
    public async Task<ActionResult<ColumnValuesDto>> FilterValues(
        Guid id,
        string field,
        [FromQuery] int take = 200,
        [FromQuery] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var report = await VisibleAsync(id, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        if (!ReportFilterFields(report).Contains(field))
        {
            return BadRequest("Field is not used in this report's filters.");
        }

        var result = await _queries.ListColumnValuesAsync(report.DatasetId, field, take, lang: lang, cancellationToken: cancellationToken);
        return Ok(ColumnValuesDto.From(result));
    }

    [HttpPost("{id:guid}/filter-values")]
    [Authorize(Policy = PermissionCodes.ReportsView)]
    public async Task<ActionResult<ColumnValuesDto>> FilterValues(
        Guid id,
        [FromBody] ColumnValuesRequest request,
        CancellationToken cancellationToken)
    {
        var report = await VisibleAsync(id, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        var field = request.Field?.Trim() ?? string.Empty;
        if (!ReportFilterFields(report).Contains(field))
        {
            return BadRequest("Field is not used in this report's filters.");
        }

        var take = request.Take <= 0 ? 200 : request.Take;
        var result = await _queries.ListColumnValuesAsync(
            report.DatasetId,
            field,
            take,
            request.Filters,
            request.Lang,
            cancellationToken);
        return Ok(ColumnValuesDto.From(result));
    }

    [HttpPost("{id:guid}/run")]
    [Authorize(Policy = PermissionCodes.ReportsView)]
    public async Task<ActionResult<ReportResultDto>> Run(Guid id, [FromBody] RunRequest? request, CancellationToken cancellationToken)
    {
        var report = await VisibleAsync(id, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        var skip = request?.Skip ?? 0;
        var take = request?.Take ?? 50;
        var result = await _queries.RunAsync(id, skip, take, request?.Layout, request?.Lang, cancellationToken);

        var servingFrom = report.Dataset is not null && DatasetCacheRouting.IsReady(report.Dataset)
            ? "cache"
            : "live";

        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Reports,
                Action = "report.run",
                Message = $"Ran report {report.Name}.",
                EntityType = "Report",
                EntityId = report.Id,
                EntityName = report.Name,
                Details = $"{{\"servingFrom\":\"{servingFrom}\",\"rows\":{result.TotalCount},\"skip\":{skip},\"take\":{take}}}"
            },
            cancellationToken);

        return Ok(ReportResultDto.From(result));
    }

    [HttpPost("{id:guid}/details")]
    [Authorize(Policy = PermissionCodes.ReportsView)]
    public async Task<ActionResult<ReportResultDto>> Details(
        Guid id,
        [FromBody] DetailRowsRequest request,
        CancellationToken cancellationToken)
    {
        var report = await VisibleAsync(id, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        var result = await _queries.QueryDetailAsync(
            id,
            request.ParentValue,
            request.Skip,
            request.Take,
            request.Layout,
            ReportAccessRules.CanDesign(report, _currentUser),
            request.Lang,
            cancellationToken);
        return Ok(ReportResultDto.From(result));
    }

    [HttpGet("{id:guid}/export")]
    [Authorize(Policy = PermissionCodes.ReportsExport)]
    public async Task<IActionResult> Export(
        Guid id,
        [FromQuery] string format = "csv",
        [FromQuery] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var report = await VisibleAsync(id, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        if (!TryParseFormat(format, out var parsed))
        {
            return BadRequest("format must be csv, xlsx, pdf, or docx.");
        }

        return await SendExportAsync(
            BilingualFileName(report.Name, report.NameArabic),
            parsed,
            (output, token) => _queries.WriteExportAsync(id, report.Name, parsed, output, lang, token),
            preview: false,
            report.Id,
            cancellationToken);
    }

    [HttpPost("preview/export")]
    [Authorize(Policy = PermissionCodes.ReportsExport)]
    public async Task<IActionResult> ExportPreview(
        [FromBody] PreviewRequest request,
        [FromQuery] string format = "csv",
        [FromQuery] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseFormat(format, out var parsed))
        {
            return BadRequest("format must be csv, xlsx, pdf, or docx.");
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? "report" : request.Name.Trim();
        return await SendExportAsync(
            BilingualFileName(name, request.NameArabic),
            parsed,
            (output, token) => _queries.WritePreviewExportAsync(request.DatasetId, request.Layout, name, parsed, output, lang, request.NameArabic, token),
            preview: true,
            entityId: null,
            cancellationToken);
    }

    private async Task<ReportTemplate?> VisibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var report = await _templates.GetAsync(id, cancellationToken);
        if (report is null || !ReportAccessRules.CanView(report, _currentUser))
        {
            return null;
        }

        return report;
    }

    private ObjectResult? DenyDesign(ReportTemplate report)
    {
        if (ReportAccessRules.CanDesign(report, _currentUser))
        {
            return null;
        }

        var detail = report.IsAdminCreated
            ? "Only an administrator can change this report."
            : "Only the report owner or an administrator can change this report.";
        return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
        {
            Title = "Not allowed",
            Detail = detail,
            Status = StatusCodes.Status403Forbidden
        });
    }

    private async Task<ReportDetailDto> DetailAsync(ReportTemplate report, CancellationToken cancellationToken)
    {
        var names = await NamesAsync([report], cancellationToken);
        var filterColumns = await FilterColumnsAsync(report, cancellationToken);
        return ReportDetailDto.From(report, _currentUser, names, filterColumns);
    }

    private async Task<IReadOnlyList<DatasetColumnDto>> FilterColumnsAsync(
        ReportTemplate report,
        CancellationToken cancellationToken)
    {
        var filterFields = ReportFilterFields(report);
        if (filterFields.Count == 0)
        {
            return [];
        }

        var dataset = await _catalog.GetAsync(report.DatasetId, cancellationToken);
        if (dataset is null)
        {
            return [];
        }

        var columns = dataset.Columns
            .Where(column => !column.IsHidden && filterFields.Contains(column.ColumnName))
            .OrderBy(column => column.Ordinal)
            .Select(DatasetColumnDto.From)
            .ToList();

        foreach (var field in filterFields)
        {
            if (!LookupColumnPlan.TryResolve(dataset, field, out var fact, out var lookupColumn))
            {
                continue;
            }

            var sqlType = LookupDto.From(fact.Lookup!).Columns
                .FirstOrDefault(column => column.Name.Equals(lookupColumn, StringComparison.OrdinalIgnoreCase))
                ?.SqlType;
            var control = LookupFields.IsControl(lookupColumn);
            columns.Add(new DatasetColumnDto
            {
                Id = fact.Id,
                ColumnName = field,
                SqlType = string.IsNullOrWhiteSpace(sqlType) ? "nvarchar" : sqlType,
                DisplayName = control ? fact.DisplayName : $"{fact.DisplayName} · {lookupColumn}",
                DisplayNameArabic = control
                    ? (string.IsNullOrWhiteSpace(fact.DisplayNameArabic) ? null : fact.DisplayNameArabic)
                    : (string.IsNullOrWhiteSpace(fact.DisplayNameArabic)
                        ? null
                        : $"{fact.DisplayNameArabic} · {lookupColumn}"),
                IsFilterable = true,
                DefaultSortDirection = SortDirections.Asc,
                Ordinal = fact.Ordinal
            });
        }

        return columns;
    }

    private static HashSet<string> ReportFilterFields(ReportTemplate report)
    {
        var layout = LayoutJson.Parse(report.LayoutJson);
        return layout.Filters
            .Select(filter => filter.Field)
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private Task<IReadOnlyDictionary<Guid, string>> NamesAsync(
        IEnumerable<ReportTemplate> reports,
        CancellationToken cancellationToken)
        => _templates.UserNamesAsync(reports.Select(report => report.CreatedBy), cancellationToken);

    private async Task<IActionResult> SendExportAsync(
        string name,
        ReportExportFormat format,
        Func<Stream, CancellationToken, Task> write,
        bool preview,
        Guid? entityId,
        CancellationToken cancellationToken)
    {
        var fileName = SafeFileName(name) + Extension(format);
        if (format == ReportExportFormat.Csv)
        {
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            Response.ContentType = ContentType(format);
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(fileName);
            Response.Headers.ContentDisposition = disposition.ToString();
            await write(Response.Body, cancellationToken);
            await LogExportAsync(name, format, preview, entityId, cancellationToken);
            return new EmptyResult();
        }

        var stream = new MemoryStream();
        await write(stream, cancellationToken);
        stream.Position = 0;
        await LogExportAsync(name, format, preview, entityId, cancellationToken);
        return File(stream, ContentType(format), fileName);
    }

    private Task LogExportAsync(
        string name,
        ReportExportFormat format,
        bool preview,
        Guid? entityId,
        CancellationToken cancellationToken)
    {
        var kind = ExportKind(format);
        var details = preview
            ? $"{{\"format\":\"{kind}\",\"preview\":true}}"
            : $"{{\"format\":\"{kind}\"}}";
        return _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Reports,
                Action = "report.export",
                Message = $"Exported as {kind}: {name}.",
                EntityType = entityId is null ? null : "Report",
                EntityId = entityId,
                EntityName = name,
                Details = details
            },
            cancellationToken);
    }

    private static string ExportKind(ReportExportFormat format) => format switch
    {
        ReportExportFormat.Pdf => "pdf",
        ReportExportFormat.Docx => "word",
        ReportExportFormat.Xlsx => "excel",
        _ => "csv"
    };

    private static bool TryParseFormat(string? format, out ReportExportFormat parsed)
    {
        parsed = ReportExportFormat.Csv;
        if (string.IsNullOrWhiteSpace(format) || format.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
        {
            parsed = ReportExportFormat.Pdf;
            return true;
        }

        if (format.Equals("docx", StringComparison.OrdinalIgnoreCase)
            || format.Equals("word", StringComparison.OrdinalIgnoreCase))
        {
            parsed = ReportExportFormat.Docx;
            return true;
        }

        if (format.Equals("xlsx", StringComparison.OrdinalIgnoreCase)
            || format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            parsed = ReportExportFormat.Xlsx;
            return true;
        }

        return false;
    }

    private static string ContentType(ReportExportFormat format) => format switch
    {
        ReportExportFormat.Pdf => "application/pdf",
        ReportExportFormat.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ReportExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "text/csv; charset=utf-8"
    };

    private static string Extension(ReportExportFormat format) => format switch
    {
        ReportExportFormat.Pdf => ".pdf",
        ReportExportFormat.Docx => ".docx",
        ReportExportFormat.Xlsx => ".xlsx",
        _ => ".csv"
    };

    private static string BilingualFileName(string? english, string? arabic)
    {
        var en = (english ?? "").Trim();
        var ar = (arabic ?? "").Trim();
        var combined = string.IsNullOrWhiteSpace(ar) || ar.Equals(en, StringComparison.Ordinal)
            ? en
            : $"{en} - {ar}";
        if (string.IsNullOrWhiteSpace(combined))
        {
            combined = "report";
        }

        if (combined.Length > 180)
        {
            combined = combined[..180].Trim();
        }

        return SafeFileName(combined);
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Concat((name ?? "report").Select(ch => invalid.Contains(ch) ? '-' : ch)).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "report" : cleaned;
    }

    private static ReportTemplateWriteRequest ToWrite(ReportWriteRequest request) => new()
    {
        Name = request.Name,
        NameArabic = request.NameArabic,
        Description = request.Description,
        DescriptionArabic = request.DescriptionArabic,
        DatasetId = request.DatasetId,
        Layout = request.Layout,
        Status = request.Status,
        CategoryId = request.CategoryId,
        IsRestricted = request.IsRestricted,
        AccessMode = request.AccessMode,
        AccessUserIds = request.AccessUserIds,
        AccessRoleIds = request.AccessRoleIds
    };
}
