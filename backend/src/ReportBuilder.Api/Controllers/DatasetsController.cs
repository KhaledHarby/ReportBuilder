using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReportBuilder.Api.Contracts;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/datasets")]
public sealed class DatasetsController : ControllerBase
{
    private readonly IDatasetCatalogService _catalog;
    private readonly IDatasetCacheService _cache;
    private readonly IReportQueryService _queries;
    private readonly ICurrentUser _currentUser;

    public DatasetsController(
        IDatasetCatalogService catalog,
        IDatasetCacheService cache,
        IReportQueryService queries,
        ICurrentUser currentUser)
    {
        _catalog = catalog;
        _cache = cache;
        _queries = queries;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.DatasetsView)]
    public async Task<ActionResult<IReadOnlyList<DatasetDto>>> List([FromQuery] bool enabledOnly = false, CancellationToken cancellationToken = default)
    {
        var datasets = await _catalog.ListAsync(enabledOnly, cancellationToken);
        if (enabledOnly)
        {
            datasets = datasets.Where(dataset => DatasetAccessRules.CanUseInDesigner(dataset, _currentUser)).ToList();
        }

        return Ok(datasets.Select(DatasetDto.From).ToList());
    }

    [HttpGet("people")]
    [Authorize(Policy = PermissionCodes.DatasetsUpdate)]
    public async Task<ActionResult<IReadOnlyList<ReportPersonDto>>> People(CancellationToken cancellationToken)
    {
        var people = await _catalog.ListPeopleAsync(cancellationToken);
        return Ok(people.Select(ReportPersonDto.From).ToList());
    }

    [HttpGet("access-roles")]
    [Authorize(Policy = PermissionCodes.DatasetsUpdate)]
    public async Task<ActionResult<IReadOnlyList<ReportAccessRoleDto>>> AccessRoles(CancellationToken cancellationToken)
    {
        var roles = await _catalog.ListRolesAsync(cancellationToken);
        return Ok(roles.Select(ReportAccessRoleDto.From).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCodes.DatasetsView)]
    public async Task<ActionResult<DatasetDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var dataset = await _catalog.GetAsync(id, cancellationToken);
        return dataset is null ? NotFound() : Ok(DatasetDto.From(dataset));
    }

    [HttpPost("discover")]
    [Authorize(Policy = PermissionCodes.DatasetsDiscover)]
    public async Task<ActionResult<IReadOnlyList<DatasetDto>>> Discover(CancellationToken cancellationToken)
    {
        var datasets = await _catalog.DiscoverAsync(cancellationToken);
        return Ok(datasets.Select(DatasetDto.From).ToList());
    }

    [HttpPost("query/preview")]
    [Authorize(Policy = PermissionCodes.DatasetsUpdate)]
    public async Task<ActionResult<IReadOnlyList<DatasetLiveColumn>>> PreviewQuery(
        [FromBody] DatasetQueryWriteRequest? request,
        CancellationToken cancellationToken)
    {
        var columns = await _catalog.PreviewQueryColumnsAsync(request?.SqlText, cancellationToken);
        return Ok(columns);
    }

    [HttpPost("query")]
    [Authorize(Policy = PermissionCodes.DatasetsUpdate)]
    public async Task<ActionResult<DatasetDto>> CreateQuery(
        [FromBody] DatasetQueryWriteRequest? request,
        CancellationToken cancellationToken)
    {
        var dataset = await _catalog.CreateQueryAsync(request ?? new DatasetQueryWriteRequest(), cancellationToken);
        return Ok(DatasetDto.From(dataset));
    }

    [HttpGet("{id:guid}/missing-columns")]
    [Authorize(Policy = PermissionCodes.DatasetsView)]
    public async Task<ActionResult<DatasetColumnGap>> MissingColumns(Guid id, CancellationToken cancellationToken)
    {
        var gap = await _catalog.GetColumnGapAsync(id, cancellationToken);
        return gap is null ? NotFound() : Ok(gap);
    }

    [HttpGet("{id:guid}/sql")]
    public async Task<ActionResult<DatasetSqlScriptDto>> Sql(Guid id, CancellationToken cancellationToken)
    {
        var dataset = await _catalog.GetAsync(id, cancellationToken);
        if (dataset is null)
        {
            return NotFound();
        }

        var queryDataset = dataset.ObjectType == DatasetObjectType.Query;
        var canRead = _currentUser.Has(PermissionCodes.SqlView) || _currentUser.Has(PermissionCodes.SqlModify);
        if (queryDataset)
        {
            canRead = canRead || _currentUser.Has(PermissionCodes.DatasetsUpdate);
        }

        if (!canRead)
        {
            return Forbid();
        }

        var script = await _catalog.GetSqlScriptAsync(id, cancellationToken);
        return script is null ? NotFound() : Ok(DatasetSqlScriptDto.From(script));
    }

    [HttpPost("{id:guid}/sql")]
    public async Task<ActionResult<DatasetSqlScriptDto>> ApplySql(
        Guid id,
        [FromBody] ApplyDatasetSqlRequest? request,
        CancellationToken cancellationToken)
    {
        var dataset = await _catalog.GetAsync(id, cancellationToken);
        if (dataset is null)
        {
            return NotFound();
        }

        var allowed = dataset.ObjectType == DatasetObjectType.Query
            ? _currentUser.Has(PermissionCodes.DatasetsUpdate) || _currentUser.Has(PermissionCodes.SqlModify)
            : _currentUser.Has(PermissionCodes.SqlModify);
        if (!allowed)
        {
            return Forbid();
        }

        var script = await _catalog.ApplySqlScriptAsync(id, request ?? new ApplyDatasetSqlRequest(), cancellationToken);
        return Ok(DatasetSqlScriptDto.From(script));
    }

    [HttpPost("{id:guid}/import-columns")]
    [Authorize(Policy = PermissionCodes.DatasetsUpdate)]
    public async Task<ActionResult<DatasetDto>> ImportColumns(
        Guid id,
        [FromBody] ImportColumnsRequest? request,
        CancellationToken cancellationToken)
    {
        var dataset = await _catalog.ImportColumnsAsync(id, request ?? new ImportColumnsRequest(), cancellationToken);
        return Ok(DatasetDto.From(dataset));
    }

    [HttpPut("{id:guid}")]
    [HttpPost("{id:guid}")]
    [Authorize(Policy = PermissionCodes.DatasetsUpdate)]
    public async Task<ActionResult<DatasetDto>> Update(Guid id, [FromBody] DatasetUpdateRequest request, CancellationToken cancellationToken)
    {
        var dataset = await _catalog.UpdateAsync(id, request, cancellationToken);
        return Ok(DatasetDto.From(dataset));
    }

    [HttpPut("{id:guid}/cache")]
    [HttpPost("{id:guid}/cache")]
    [Authorize(Policy = PermissionCodes.DatasetsCache)]
    public async Task<ActionResult<DatasetDto>> ConfigureCache(
        Guid id,
        [FromBody] DatasetCacheConfigureRequest request,
        CancellationToken cancellationToken)
    {
        var dataset = await _cache.ConfigureAsync(id, request, cancellationToken);
        return Ok(DatasetDto.From(dataset));
    }

    [HttpPost("{id:guid}/cache/refresh")]
    [Authorize(Policy = PermissionCodes.DatasetsCache)]
    public async Task<ActionResult<DatasetDto>> RefreshCache(Guid id, CancellationToken cancellationToken)
    {
        var dataset = await _cache.RequestRefreshAsync(id, _currentUser.UserId, cancellationToken);
        return Ok(DatasetDto.From(dataset));
    }

    [HttpDelete("{id:guid}/cache")]
    [Authorize(Policy = PermissionCodes.DatasetsCache)]
    public async Task<ActionResult<DatasetDto>> ClearCache(Guid id, CancellationToken cancellationToken)
    {
        var dataset = await _cache.ClearAsync(id, _currentUser.UserId, cancellationToken);
        return Ok(DatasetDto.From(dataset));
    }

    [HttpGet("{id:guid}/values/{field}")]
    [Authorize(Policy = PermissionCodes.DatasetsView)]
    public async Task<ActionResult<ColumnValuesDto>> Values(
        Guid id,
        string field,
        [FromQuery] int take = 200,
        [FromQuery] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _queries.ListColumnValuesAsync(id, field, take, lang: lang, cancellationToken: cancellationToken);
        return Ok(ColumnValuesDto.From(result));
    }

    [HttpPost("{id:guid}/values")]
    [Authorize(Policy = PermissionCodes.DatasetsView)]
    public async Task<ActionResult<ColumnValuesDto>> Values(
        Guid id,
        [FromBody] ColumnValuesRequest request,
        CancellationToken cancellationToken)
    {
        var field = request.Field?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(field))
        {
            return BadRequest("Field is required.");
        }

        var take = request.Take <= 0 ? 200 : request.Take;
        var result = await _queries.ListColumnValuesAsync(id, field, take, request.Filters, request.Lang, cancellationToken);
        return Ok(ColumnValuesDto.From(result));
    }
}
