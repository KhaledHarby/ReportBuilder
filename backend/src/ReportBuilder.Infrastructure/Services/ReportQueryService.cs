using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Cache;
using ReportBuilder.Infrastructure.Data;
using ReportBuilder.Infrastructure.Export;

namespace ReportBuilder.Infrastructure.Services;

public sealed class ReportQueryService : IReportQueryService
{
    private readonly ReportBuilderDbContext _db;
    private readonly SqlSchemaReader _schema;
    private readonly ICurrentUser _currentUser;
    private readonly ICacheSettingsService _cacheSettings;
    private readonly ClickHouseHttpClient _clickHouse;
    private readonly RedisCacheClient _redis;
    private readonly MongoCacheClient _mongo;
    private readonly PostgresCacheClient _postgres;
    private readonly ILookupService _lookups;
    private readonly IAppBrandingService _branding;
    private ICurrentUser? _exportActor;
    private UserScope? _deliveryScope;
    private bool _exportTruncated;
    private int _exportRowCount;
    private bool _stampExportTitles;
    private string? _exportEnglishTitle;
    private string? _exportArabicTitle;
    private ICurrentUser Actor => _exportActor ?? _currentUser;

    public ReportQueryService(
        ReportBuilderDbContext db,
        ICurrentUser currentUser,
        ICacheSettingsService cacheSettings,
        ClickHouseHttpClient clickHouse,
        RedisCacheClient redis,
        MongoCacheClient mongo,
        PostgresCacheClient postgres,
        ILookupService lookups,
        IAppBrandingService branding)
    {
        _db = db;
        _currentUser = currentUser;
        _cacheSettings = cacheSettings;
        _clickHouse = clickHouse;
        _redis = redis;
        _mongo = mongo;
        _postgres = postgres;
        _lookups = lookups;
        _branding = branding;
        _schema = new SqlSchemaReader(db);
    }

    public async Task<ReportResult> PreviewAsync(
        Guid datasetId,
        ReportLayout layout,
        int skip,
        int take,
        string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var dataset = await LoadEnabledDatasetAsync(datasetId, cancellationToken);
        RequiredFilters.EnsureSatisfied(layout);
        layout = UserScopeInjector.Apply(layout, dataset, _currentUser);
        layout = await WithLookupFiltersAsync(dataset, layout, cancellationToken);
        await _lookups.AttachSortsAsync(dataset, layout, cancellationToken);
        var plan = LookupColumnPlan.Prepare(dataset, layout);
        var result = await QueryEngineAsync(dataset, plan.EngineLayout, skip, take, allRows: false, cancellationToken);
        result = await _lookups.ApplyReportColumnsAsync(result, plan, lang, cancellationToken);
        return WithColumnDisplayNames(result, dataset, lang);
    }

    /// <summary>
    /// Link templates and column titles use the names designers see, such as ID1 or Transaction ID,
    /// while the row key is the physical column. Send both display names so a viewer can fill a link
    /// and show the Arabic title without opening the dataset catalog.
    /// </summary>
    private static ReportResult WithColumnDisplayNames(ReportResult result, Dataset dataset, string? lang)
    {
        var byName = dataset.Columns.ToDictionary(
            column => column.ColumnName,
            column => column,
            StringComparer.OrdinalIgnoreCase);
        var columns = result.Columns.Select(column =>
        {
            if (!byName.TryGetValue(column.Name, out var meta))
            {
                return column;
            }

            var english = string.IsNullOrWhiteSpace(meta.DisplayName) ? null : meta.DisplayName.Trim();
            var arabic = string.IsNullOrWhiteSpace(meta.DisplayNameArabic) ? null : meta.DisplayNameArabic.Trim();
            if (english is null && arabic is null)
            {
                return column;
            }

            return new ReportResultColumn
            {
                Name = column.Name,
                Label = column.Hidden ? column.Label : LocalizedColumnLabel(column.Label, english, arabic, lang),
                DisplayName = english,
                DisplayNameArabic = arabic,
                Hidden = column.Hidden
            };
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = result.Rows,
            TotalCount = result.TotalCount,
            Skip = result.Skip,
            Take = result.Take
        };
    }

    private static string LocalizedColumnLabel(string current, string? english, string? arabic, string? lang)
    {
        if (lang is not null
            && lang.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(arabic))
        {
            return arabic;
        }

        if (!string.IsNullOrWhiteSpace(english))
        {
            return english;
        }

        return string.IsNullOrWhiteSpace(current) ? arabic ?? current : current;
    }

    private async Task<ReportLayout> WithLookupFiltersAsync(
        Dataset dataset,
        ReportLayout layout,
        CancellationToken cancellationToken)
    {
        var filters = await _lookups.RewriteFiltersAsync(dataset, layout.Filters, cancellationToken);
        if (ReferenceEquals(filters, layout.Filters))
        {
            return layout;
        }

        return new ReportLayout
        {
            Kind = layout.Kind,
            Columns = layout.Columns,
            GroupBy = layout.GroupBy,
            Filters = filters.ToList(),
            OrderBy = layout.OrderBy,
            Chart = layout.Chart,
            UserScope = layout.UserScope,
            Detail = layout.Detail
        };
    }

    private async Task<ReportResult> QueryEngineAsync(
        Dataset dataset,
        ReportLayout layout,
        int skip,
        int take,
        bool allRows,
        CancellationToken cancellationToken,
        int? maxTake = null)
    {
        var calculated = layout.Columns.Any(column => !string.IsNullOrWhiteSpace(column.Expression));
        if (!calculated && await TryMongoAsync(dataset, cancellationToken) is { } mongo)
        {
            return await _mongo.QueryReportAsync(
                mongo.Connection,
                mongo.Collection,
                dataset,
                layout,
                skip,
                take,
                allRows,
                cancellationToken,
                maxTake);
        }

        if (!calculated && await TryRedisAsync(dataset, cancellationToken) is { } redis)
        {
            return await _redis.QueryReportAsync(
                redis.Connection,
                redis.Key,
                dataset,
                layout,
                skip,
                take,
                allRows,
                cancellationToken,
                maxTake);
        }

        var clickHouse = calculated ? null : await TryClickHouseAsync(dataset, cancellationToken);
        if (clickHouse is { } route)
        {
            var built = ReportQueryBuilder.Build(dataset, layout, skip, take, allRows, route.Dialect, maxTake);
            return await ExecuteClickHouseAsync(route.Connection, built, layout, cancellationToken);
        }

        var postgres = calculated ? null : await TryPostgresAsync(dataset, cancellationToken);
        if (postgres is { } pg)
        {
            var built = ReportQueryBuilder.Build(dataset, layout, skip, take, allRows, pg.Dialect, maxTake);
            return await ExecutePostgresAsync(pg.Connection, built, layout, cancellationToken);
        }

        try
        {
            var built = ReportQueryBuilder.Build(dataset, layout, skip, take, allRows, maxTake: maxTake);
            return await ExecuteAsync(built, layout, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 207)
        {
            dataset = await RefreshAndReloadAsync(dataset, cancellationToken);
            try
            {
                var built = ReportQueryBuilder.Build(dataset, layout, skip, take, allRows, maxTake: maxTake);
                return await ExecuteAsync(built, layout, cancellationToken);
            }
            catch (SqlException retry) when (retry.Number == 207)
            {
                throw new ReportQueryException(
                    "One or more selected columns are no longer in this data source. Refresh the page and try again.");
            }
        }
    }

    public async Task<ColumnValuesResult> ListColumnValuesAsync(
        Guid datasetId,
        string field,
        int take,
        IReadOnlyList<LayoutFilter>? filters = null,
        string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var dataset = await LoadEnabledDatasetAsync(datasetId, cancellationToken);
        take = take <= 0 ? ReportQueryBuilder.DefaultDistinctTake : Math.Min(take, ReportQueryBuilder.MaxDistinctTake);
        if (LookupColumnPlan.TryResolve(dataset, field, out var fact, out _))
        {
            return await ListScopedLookupAsync(
                dataset,
                field,
                fact.ColumnName,
                take,
                filters,
                lang,
                lookupField: true,
                cancellationToken);
        }

        var bound = dataset.Columns.FirstOrDefault(column =>
            column.ColumnName.Equals(field, StringComparison.OrdinalIgnoreCase) && !column.IsHidden);
        if (bound?.Lookup is { IsValid: true })
        {
            return await ListScopedLookupAsync(
                dataset,
                field,
                bound.ColumnName,
                take,
                filters,
                lang,
                lookupField: false,
                cancellationToken);
        }

        if (filters is { Count: > 0 })
        {
            filters = await _lookups.RewriteFiltersAsync(dataset, filters, cancellationToken);
        }

        return await QueryStoredDistinctAsync(dataset, field, take, filters, cancellationToken);
    }

    private async Task<ColumnValuesResult> ListScopedLookupAsync(
        Dataset dataset,
        string field,
        string factColumn,
        int take,
        IReadOnlyList<LayoutFilter>? filters,
        string? lang,
        bool lookupField,
        CancellationToken cancellationToken)
    {
        var ancestors = ReportQueryBuilder.ScopeFiltersForDistinct(field, filters);
        if (!ReportQueryBuilder.HasActiveFilter(ancestors))
        {
            return await UnscopedLookupAsync(dataset, field, take, lang, lookupField, cancellationToken);
        }

        var rewritten = await _lookups.RewriteFiltersAsync(dataset, ancestors, cancellationToken);
        var keys = await QueryStoredDistinctAsync(
            dataset,
            factColumn,
            take,
            LinkForDistinct(factColumn, rewritten),
            cancellationToken);
        return lookupField
            ? await _lookups.TryListLookupColumnValuesAsync(
                dataset, field, take, lang, cancellationToken, keys.Values, keys.Truncated) ?? EmptyValues(field)
            : await _lookups.TryListValuesAsync(
                dataset.Columns.First(column =>
                    column.ColumnName.Equals(factColumn, StringComparison.OrdinalIgnoreCase)).LookupId!.Value,
                take,
                cancellationToken,
                keys.Values,
                keys.Truncated) is { } lookedUp
                ? new ColumnValuesResult
                {
                    Field = field,
                    Values = lookedUp.Values,
                    Choices = lookedUp.Choices,
                    Truncated = lookedUp.Truncated
                }
                : EmptyValues(field);
    }

    private async Task<ColumnValuesResult> UnscopedLookupAsync(
        Dataset dataset,
        string field,
        int take,
        string? lang,
        bool lookupField,
        CancellationToken cancellationToken)
    {
        if (lookupField)
        {
            return await _lookups.TryListLookupColumnValuesAsync(dataset, field, take, lang, cancellationToken)
                ?? EmptyValues(field);
        }

        var lookupId = dataset.Columns.First(column =>
            column.ColumnName.Equals(field, StringComparison.OrdinalIgnoreCase)).LookupId!.Value;
        var lookedUp = await _lookups.TryListValuesAsync(lookupId, take, cancellationToken);
        if (lookedUp is null)
        {
            return EmptyValues(field);
        }

        return new ColumnValuesResult
        {
            Field = field,
            Values = lookedUp.Values,
            Choices = lookedUp.Choices,
            Truncated = lookedUp.Truncated
        };
    }

    private static ColumnValuesResult EmptyValues(string field)
        => new()
        {
            Field = field,
            Values = [],
            Choices = []
        };

    /// <summary>
    /// Distinct queries keep only filters named by the target field's DependsOn chain.
    /// Ancestor filters are already chosen, so hang them off a blank filter on the fact column.
    /// </summary>
    private static List<LayoutFilter> LinkForDistinct(string field, IReadOnlyList<LayoutFilter> filters)
    {
        var names = filters
            .Select(item => item.Field)
            .Where(item => !string.IsNullOrWhiteSpace(item)
                && !item.Equals(field, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var linked = new List<LayoutFilter>
        {
            new()
            {
                Field = field,
                Operator = "eq",
                DependsOn = names.Count == 0 ? null : names[0]
            }
        };
        foreach (var item in filters)
        {
            var nameIndex = names.FindIndex(name => name.Equals(item.Field, StringComparison.OrdinalIgnoreCase));
            linked.Add(new LayoutFilter
            {
                Field = item.Field,
                Operator = item.Operator,
                Value = item.Value,
                IsFixed = item.IsFixed,
                IsHidden = item.IsHidden,
                DependsOn = nameIndex >= 0 && nameIndex + 1 < names.Count ? names[nameIndex + 1] : null
            });
        }

        return linked;
    }

    private async Task<ColumnValuesResult> QueryStoredDistinctAsync(
        Dataset dataset,
        string field,
        int take,
        IReadOnlyList<LayoutFilter>? filters,
        CancellationToken cancellationToken)
    {
        if (await TryMongoAsync(dataset, cancellationToken) is { } mongo)
        {
            return await _mongo.QueryDistinctAsync(
                mongo.Connection,
                mongo.Collection,
                dataset,
                field,
                take,
                filters,
                cancellationToken);
        }

        if (await TryRedisAsync(dataset, cancellationToken) is { } redis)
        {
            return await _redis.QueryDistinctAsync(
                redis.Connection,
                redis.Key,
                dataset,
                field,
                take,
                filters,
                cancellationToken);
        }

        var clickHouse = await TryClickHouseAsync(dataset, cancellationToken);
        if (clickHouse is { } route)
        {
            var query = ReportQueryBuilder.BuildDistinctValuesQuery(dataset, field, take, filters, route.Dialect);
            return await ReadDistinctClickHouseAsync(route.Connection, query, field, take, cancellationToken);
        }

        var postgres = await TryPostgresAsync(dataset, cancellationToken);
        if (postgres is { } pg)
        {
            var query = ReportQueryBuilder.BuildDistinctValuesQuery(dataset, field, take, filters, pg.Dialect);
            return await ReadDistinctPostgresAsync(pg.Connection, query, field, take, cancellationToken);
        }

        try
        {
            return await ReadDistinctValuesAsync(dataset, field, take, filters, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 207)
        {
            dataset = await RefreshAndReloadAsync(dataset, cancellationToken);
            if (!dataset.Columns.Any(column =>
                    column.ColumnName.Equals(field, StringComparison.OrdinalIgnoreCase) && !column.IsHidden))
            {
                throw new ReportQueryException(
                    $"Column '{field}' is no longer in {dataset.SchemaName}.{dataset.ObjectName}. Refresh the page to update the column list.");
            }

            return await ReadDistinctValuesAsync(dataset, field, take, filters, cancellationToken);
        }
    }

    private async Task<Dataset> LoadEnabledDatasetAsync(Guid datasetId, CancellationToken cancellationToken)
    {
        var dataset = await _db.Datasets
            .Include(d => d.Columns).ThenInclude(c => c.Lookup)
            .Include(d => d.CacheState)
            .FirstOrDefaultAsync(d => d.Id == datasetId, cancellationToken)
            ?? throw new ReportQueryException("Dataset was not found.");

        if (!dataset.IsEnabled)
        {
            throw new ReportQueryException("Dataset is not enabled. Enable it in the data catalog first.");
        }

        return dataset;
    }

    private async Task<Dataset> RefreshAndReloadAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        await _schema.RefreshColumnsAsync(dataset, cancellationToken);
        _db.ChangeTracker.Clear();
        return await LoadEnabledDatasetAsync(dataset.Id, cancellationToken);
    }

    private async Task<ColumnValuesResult> ReadDistinctValuesAsync(
        Dataset dataset,
        string field,
        int take,
        IReadOnlyList<LayoutFilter>? filters,
        CancellationToken cancellationToken)
    {
        var query = ReportQueryBuilder.BuildDistinctValuesQuery(dataset, field, take, filters);
        var connectionString = _db.Database.GetConnectionString()
            ?? throw new ReportQueryException("Database connection is not configured.");

        var values = new List<string>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = query.Sql;
        command.CommandTimeout = 60;
        AddFilterParameters(command, query.Parameters);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            var formatted = FormatDistinctValue(reader.GetValue(0));
            if (!string.IsNullOrWhiteSpace(formatted)
                && values.TrueForAll(existing => !existing.Equals(formatted, StringComparison.OrdinalIgnoreCase)))
            {
                values.Add(formatted);
            }
        }

        var truncated = values.Count > take;
        if (truncated)
        {
            values.RemoveRange(take, values.Count - take);
        }

        return new ColumnValuesResult
        {
            Field = field,
            Values = values,
            Truncated = truncated
        };
    }

    private static string FormatDistinctValue(object value) =>
        value switch
        {
            DateTime dt => dt.TimeOfDay == TimeSpan.Zero
                ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

    public async Task<ReportExportOutcome> WriteExportForAsync(
        Guid templateId,
        string reportName,
        ReportExportFormat format,
        Stream output,
        ICurrentUser actor,
        string? lang = null,
        CancellationToken cancellationToken = default,
        UserScope? deliveryScope = null)
    {
        _exportActor = actor;
        _deliveryScope = deliveryScope;
        _exportTruncated = false;
        _exportRowCount = 0;
        try
        {
            await WriteExportAsync(templateId, reportName, format, output, lang, cancellationToken);
            return new ReportExportOutcome(_exportTruncated, _exportRowCount);
        }
        finally
        {
            _exportActor = null;
            _deliveryScope = null;
        }
    }

    public async Task WriteExportAsync(
        Guid templateId,
        string reportName,
        ReportExportFormat format,
        Stream output,
        string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var template = await _db.ReportTemplates
            .Include(t => t.Dataset).ThenInclude(d => d.Columns).ThenInclude(c => c.Lookup)
            .Include(t => t.Dataset).ThenInclude(d => d.CacheState)
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken)
            ?? throw new ReportQueryException("Report template was not found.");

        var layout = LayoutJson.Parse(template.LayoutJson);
        _stampExportTitles = true;
        _exportEnglishTitle = string.IsNullOrWhiteSpace(template.Name) ? reportName : template.Name;
        _exportArabicTitle = template.NameArabic;
        try
        {
            await WriteExportAsync(template.Dataset, layout, template.Name, format, output, lang, cancellationToken);
        }
        finally
        {
            _stampExportTitles = false;
            _exportEnglishTitle = null;
            _exportArabicTitle = null;
        }
    }

    public async Task WritePreviewExportAsync(
        Guid datasetId,
        ReportLayout layout,
        string reportName,
        ReportExportFormat format,
        Stream output,
        string? lang = null,
        string? reportNameArabic = null,
        CancellationToken cancellationToken = default)
    {
        var dataset = await _db.Datasets
            .Include(d => d.Columns).ThenInclude(c => c.Lookup)
            .Include(d => d.CacheState)
            .FirstOrDefaultAsync(d => d.Id == datasetId, cancellationToken)
            ?? throw new ReportQueryException("Dataset was not found.");

        _stampExportTitles = true;
        _exportEnglishTitle = reportName;
        _exportArabicTitle = reportNameArabic;
        try
        {
            await WriteExportAsync(dataset, layout, reportName, format, output, lang, cancellationToken);
        }
        finally
        {
            _stampExportTitles = false;
            _exportEnglishTitle = null;
            _exportArabicTitle = null;
        }
    }

    private async Task WriteExportAsync(
        Dataset dataset,
        ReportLayout layout,
        string reportName,
        ReportExportFormat format,
        Stream output,
        string? lang,
        CancellationToken cancellationToken)
    {
        if (!dataset.IsEnabled)
        {
            throw new ReportQueryException("Dataset is not enabled. Enable it in the data catalog first.");
        }

        var deliveryScope = _deliveryScope;
        if (deliveryScope is { Enabled: true } && !string.IsNullOrWhiteSpace(deliveryScope.Field))
        {
            layout = WithDeliveryPlaceholder(layout, deliveryScope.Field);
        }

        RequiredFilters.EnsureSatisfied(layout);
        layout = UserScopeInjector.Apply(layout, dataset, Actor);
        if (deliveryScope is { Enabled: true } && !string.IsNullOrWhiteSpace(deliveryScope.Field))
        {
            layout.UserScope = deliveryScope;
            layout = UserScopeInjector.Apply(layout, dataset, Actor);
        }
        layout = await WithLookupFiltersAsync(dataset, layout, cancellationToken);
        await _lookups.AttachSortsAsync(dataset, layout, cancellationToken);
        var lookupPlan = LookupColumnPlan.Prepare(dataset, layout);
        if (lookupPlan.HasLookups)
        {
            await WriteLookupExportAsync(dataset, lookupPlan, reportName, format, output, lang, cancellationToken);
            return;
        }

        if (await TryMongoAsync(dataset, cancellationToken) is { } mongo)
        {
            if (format == ReportExportFormat.Csv)
            {
                _exportRowCount = await _mongo.WriteCsvAsync(
                    mongo.Connection,
                    mongo.Collection,
                    dataset,
                    layout,
                    output,
                    lang,
                    cancellationToken);
                return;
            }

            var exported = await _mongo.QueryExportAsync(
                mongo.Connection,
                mongo.Collection,
                dataset,
                layout,
                reportName,
                format,
                ResolveExportUserName(),
                lang,
                cancellationToken);
            _exportTruncated = exported.Truncated;
            await WriteFormattedAsync(exported, format, lang, output, cancellationToken);
            return;
        }

        if (await TryRedisAsync(dataset, cancellationToken) is { } redis)
        {
            var exported = await _redis.QueryExportAsync(
                redis.Connection,
                redis.Key,
                dataset,
                layout,
                reportName,
                format,
                ResolveExportUserName(),
                lang,
                cancellationToken);
            _exportTruncated = exported.Truncated;
            await WriteFormattedAsync(exported, format, lang, output, cancellationToken);
            return;
        }

        ReportExportTable table;
        var clickHouse = await TryClickHouseAsync(dataset, cancellationToken);
        if (clickHouse is { } route)
        {
            var built = ReportQueryBuilder.Build(dataset, layout, skip: 0, take: 0, allRows: true, dialect: route.Dialect);
            table = await LoadExportTableClickHouseAsync(
                route.Connection,
                built,
                layout,
                reportName,
                format,
                lang,
                cancellationToken);
        }
        else
        {
            var postgres = await TryPostgresAsync(dataset, cancellationToken);
            if (postgres is { } pg)
            {
                var built = ReportQueryBuilder.Build(dataset, layout, skip: 0, take: 0, allRows: true, dialect: pg.Dialect);
                table = await LoadExportTablePostgresAsync(
                    pg.Connection,
                    built,
                    layout,
                    reportName,
                    format,
                    lang,
                    cancellationToken);
            }
            else
            {
                table = await LoadExportTableAsync(dataset, layout, reportName, format, lang, cancellationToken);
            }
        }

        _exportTruncated = table.Truncated;
        await WriteFormattedAsync(table, format, lang, output, cancellationToken);
    }

    private async Task WriteLookupExportAsync(
        Dataset dataset,
        LookupColumnPlan plan,
        string reportName,
        ReportExportFormat format,
        Stream output,
        string? lang,
        CancellationToken cancellationToken)
    {
        var cap = ReportExportWriter.RowCapFor(format);
        var result = cap is int take
            ? await QueryEngineAsync(dataset, plan.EngineLayout, 0, take, allRows: false, cancellationToken, maxTake: take)
            : await QueryEngineAsync(dataset, plan.EngineLayout, 0, 0, allRows: true, cancellationToken);
        result = await _lookups.ApplyReportColumnsAsync(result, plan, lang, cancellationToken);
        result = WithColumnDisplayNames(result, dataset, lang);

        var include = new List<int>();
        for (var index = 0; index < result.Columns.Count; index++)
        {
            if (result.Columns[index].Hidden
                || !LayoutColumnLocales.ShouldInclude(index, plan.SourceLayout, lang))
            {
                continue;
            }

            include.Add(index);
        }

        var rows = new List<IReadOnlyList<string>>(result.Rows.Count);
        foreach (var row in result.Rows)
        {
            var cells = new string[include.Count];
            for (var cell = 0; cell < include.Count; cell++)
            {
                var index = include[cell];
                var column = result.Columns[index];
                row.TryGetValue(column.Name, out var value);
                var layoutColumn = index < plan.SourceLayout.Columns.Count
                    ? plan.SourceLayout.Columns[index]
                    : null;
                cells[cell] = ColumnValueFormatter.FormatLayout(value, layoutColumn?.Format, layoutColumn, column.Name);
            }

            rows.Add(cells);
        }

        var truncated = cap is int limit && result.TotalCount > result.Rows.Count;
        _exportTruncated = truncated;
        await WriteFormattedAsync(new ReportExportTable
        {
            Title = string.IsNullOrWhiteSpace(reportName) ? "Report" : reportName.Trim(),
            Columns = include.Select(index =>
            {
                var column = result.Columns[index];
                return string.IsNullOrWhiteSpace(column.Label) ? column.Name : column.Label;
            }).ToArray(),
            ColumnStyles = ExportColumnStyleBuilder.For(include, plan.SourceLayout, lang),
            Rows = rows,
            Truncated = truncated,
            ExportedBy = ResolveExportUserName()
        }, format, lang, output, cancellationToken);
    }

    private async Task WriteFormattedAsync(
        ReportExportTable table,
        ReportExportFormat format,
        string? lang,
        Stream output,
        CancellationToken cancellationToken)
    {
        _exportRowCount = table.Rows.Count;
        if (_stampExportTitles)
        {
            table = WithTitles(table, _exportEnglishTitle, _exportArabicTitle);
        }

        if (format == ReportExportFormat.Pdf)
        {
            table = await ApplyLetterheadAsync(table, lang, cancellationToken);
        }
        else if (string.Equals(lang, "ar", StringComparison.OrdinalIgnoreCase))
        {
            table = CopyTable(table, arabic: true);
        }

        await ReportExportWriter.WriteAsync(table, format, output, cancellationToken);
    }

    private async Task<ReportExportTable> ApplyLetterheadAsync(
        ReportExportTable table,
        string? lang,
        CancellationToken cancellationToken)
    {
        var branding = await _branding.GetAsync(cancellationToken);
        var arabic = string.Equals(lang, "ar", StringComparison.OrdinalIgnoreCase);
        var organization = FirstText(branding.OrganizationName, null);
        var organizationArabic = FirstText(branding.OrganizationNameArabic, null);
        byte[]? logo = null;
        if (branding.HasLogo)
        {
            var file = await _branding.LogoAsync(cancellationToken);
            if (file is not null && IsPdfLogo(file.Bytes))
            {
                logo = file.Bytes;
            }
        }

        return CopyTable(table, organization, organizationArabic, logo, arabic);
    }

    private static ReportExportTable WithTitles(ReportExportTable table, string? english, string? arabic)
    {
        var title = string.IsNullOrWhiteSpace(english) ? table.Title : english.Trim();
        var titleArabic = string.IsNullOrWhiteSpace(arabic) ? null : arabic.Trim();
        if (titleArabic is not null && titleArabic.Equals(title, StringComparison.Ordinal))
        {
            titleArabic = null;
        }

        return new ReportExportTable
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Report" : title,
            TitleArabic = titleArabic,
            Columns = table.Columns,
            ColumnStyles = table.ColumnStyles,
            Rows = table.Rows,
            Truncated = table.Truncated,
            ExportedBy = table.ExportedBy,
            Organization = table.Organization,
            OrganizationArabic = table.OrganizationArabic,
            Application = table.Application,
            Logo = table.Logo,
            Arabic = table.Arabic
        };
    }

    private static ReportExportTable CopyTable(
        ReportExportTable table,
        string? organization = null,
        string? organizationArabic = null,
        byte[]? logo = null,
        bool arabic = false)
        => new()
        {
            Title = table.Title,
            TitleArabic = table.TitleArabic,
            Columns = table.Columns,
            ColumnStyles = table.ColumnStyles,
            Rows = table.Rows,
            Truncated = table.Truncated,
            ExportedBy = table.ExportedBy,
            Organization = organization,
            OrganizationArabic = organizationArabic,
            Logo = logo,
            Arabic = arabic
        };

    private static ReportLayout WithDeliveryPlaceholder(ReportLayout layout, string field)
    {
        var filters = layout.Filters
            .Where(filter => !filter.Field.Equals(field, StringComparison.OrdinalIgnoreCase))
            .ToList();
        filters.Add(new LayoutFilter
        {
            Field = field,
            Operator = "eq",
            Value = "*",
            IsFixed = true
        });
        return new ReportLayout
        {
            Kind = layout.Kind,
            Columns = layout.Columns,
            GroupBy = layout.GroupBy,
            Filters = filters,
            FiltersPerRow = layout.FiltersPerRow,
            OrderBy = layout.OrderBy,
            Chart = layout.Chart,
            UserScope = layout.UserScope,
            LookupSorts = layout.LookupSorts,
            Detail = layout.Detail
        };
    }

    private static string FirstText(string? preferred, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred.Trim();
        }

        return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();
    }

    private static bool IsPdfLogo(byte[] bytes)
        => bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            || bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF
            || bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46;

    private async Task<ReportExportTable> LoadExportTableAsync(
        Dataset dataset,
        ReportLayout layout,
        string reportName,
        ReportExportFormat format,
        string? lang,
        CancellationToken cancellationToken)
    {
        var built = ReportQueryBuilder.Build(dataset, layout, skip: 0, take: 0, allRows: true);
        var connectionString = _db.Database.GetConnectionString()
            ?? throw new ReportQueryException("Database connection is not configured.");
        var rowCap = ReportExportWriter.RowCapFor(format);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = built.DataSql;
        command.CommandTimeout = 180;
        AddFilterParameters(command, built.FilterParameters);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var include = new List<int>();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            if (!built.HiddenResultNames.Contains(name) && LayoutColumnLocales.ShouldInclude(i, layout, lang))
            {
                include.Add(i);
            }
        }

        var columns = include.Select(i => reader.GetName(i)).ToArray();
        var columnStyles = ExportColumnStyleBuilder.For(include, layout, lang);

        var rows = new List<IReadOnlyList<string>>();
        var truncated = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rowCap is int cap && rows.Count >= cap)
            {
                truncated = true;
                break;
            }

            var cells = new string[include.Count];
            for (var c = 0; c < include.Count; c++)
            {
                var i = include[c];
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                var layoutColumn = i < layout.Columns.Count ? layout.Columns[i] : null;
                cells[c] = ColumnValueFormatter.FormatLayout(value, layoutColumn?.Format, layoutColumn, reader.GetName(i));
            }

            rows.Add(cells);
        }

        return new ReportExportTable
        {
            Title = string.IsNullOrWhiteSpace(reportName) ? "Report" : reportName.Trim(),
            Columns = columns,
            ColumnStyles = columnStyles,
            Rows = rows,
            Truncated = truncated,
            ExportedBy = ResolveExportUserName()
        };
    }

    private string? ResolveExportUserName()
    {
        if (!Actor.IsAuthenticated)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(Actor.DisplayName))
        {
            return Actor.DisplayName.Trim();
        }

        return string.IsNullOrWhiteSpace(Actor.UserName)
            ? null
            : Actor.UserName.Trim();
    }

    public async Task<ReportResult> RunAsync(
        Guid templateId,
        int skip,
        int take,
        ReportLayout? layout = null,
        string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var template = await _db.ReportTemplates.Include(t => t.Dataset).ThenInclude(d => d.Columns)
            .Include(t => t.Dataset).ThenInclude(d => d.CacheState)
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken)
            ?? throw new ReportQueryException("Report template was not found.");

        var effectiveLayout = layout ?? LayoutJson.Parse(template.LayoutJson);
        return await PreviewAsync(template.DatasetId, effectiveLayout, skip, take, lang, cancellationToken);
    }

    public async Task<ReportResult> QueryDetailAsync(
        Guid templateId,
        string parentValue,
        int skip,
        int take,
        ReportLayout? layoutOverride,
        bool allowLayoutOverride,
        string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var template = await _db.ReportTemplates.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == templateId, cancellationToken)
            ?? throw new ReportQueryException("Report template was not found.");

        var source = allowLayoutOverride && layoutOverride?.Detail is not null
            ? layoutOverride
            : LayoutJson.Parse(template.LayoutJson);
        var detail = source.Detail;
        if (detail is not { Enabled: true })
        {
            throw new ReportQueryException("This report has no detail rows.");
        }

        if (detail.DatasetId == Guid.Empty)
        {
            throw new ReportQueryException("Choose a detail dataset.");
        }

        var parentField = detail.ParentField?.Trim() ?? string.Empty;
        var childField = detail.ChildField?.Trim() ?? string.Empty;
        if (parentField.Length == 0 || childField.Length == 0)
        {
            throw new ReportQueryException("Choose the parent column and the matching detail column.");
        }

        var key = (parentValue ?? string.Empty).Trim();
        if (key.Length == 0 || key.Length > 400)
        {
            throw new ReportQueryException("This row has no value to match detail rows.");
        }

        var dataset = await LoadEnabledDatasetAsync(detail.DatasetId, cancellationToken);
        var child = dataset.Columns.FirstOrDefault(column =>
            column.ColumnName.Equals(childField, StringComparison.OrdinalIgnoreCase) && !column.IsHidden);
        if (child is null)
        {
            throw new ReportQueryException($"Detail column '{childField}' is not on the detail dataset.");
        }

        var columns = detail.Columns
            .Where(column =>
                !string.IsNullOrWhiteSpace(column.Expression) ||
                dataset.Columns.Any(meta =>
                    !meta.IsHidden &&
                    meta.ColumnName.Equals(column.Field?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (columns.Count == 0)
        {
            throw new ReportQueryException("Choose at least one detail column.");
        }

        var filters = detail.Filters
            .Where(filter => !filter.Field.Equals(child.ColumnName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        filters.Insert(0, new LayoutFilter
        {
            Field = child.ColumnName,
            Operator = "eq",
            Value = key,
            IsFixed = true,
            IsHidden = true
        });

        var detailLayout = new ReportLayout
        {
            Kind = "table",
            Columns = columns,
            Filters = filters,
            OrderBy = detail.OrderBy ?? [],
            UserScope = DetailUserScope(source.UserScope, dataset)
        };

        var pageTake = Math.Clamp(take <= 0 ? 20 : take, 1, 100);
        var pageSkip = Math.Max(0, skip);
        return await PreviewAsync(dataset.Id, detailLayout, pageSkip, pageTake, lang, cancellationToken);
    }

    private static UserScope? DetailUserScope(UserScope? scope, Dataset dataset)
    {
        if (scope?.Enabled != true || string.IsNullOrWhiteSpace(scope.Field))
        {
            return null;
        }

        var meta = dataset.Columns.FirstOrDefault(column =>
            column.ColumnName.Equals(scope.Field.Trim(), StringComparison.OrdinalIgnoreCase));
        if (meta is null || meta.IsHidden || !meta.IsFilterable)
        {
            return null;
        }

        if (!UserScopeProperties.IsCompatible(scope.Property, meta.SqlType))
        {
            return null;
        }

        return scope;
    }

    private async Task<ReportResult> ExecuteAsync(BuiltQuery query, ReportLayout layout, CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var total = 0;
            await using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = query.CountSql;
                AddFilterParameters(countCommand, query.FilterParameters);
                var scalar = await countCommand.ExecuteScalarAsync(cancellationToken);
                total = Convert.ToInt32(scalar);
            }

            var columns = new List<ReportResultColumn>();
            var rows = new List<IReadOnlyDictionary<string, object?>>();

            await using (var dataCommand = connection.CreateCommand())
            {
                dataCommand.CommandText = query.DataSql;
                AddFilterParameters(dataCommand, query.FilterParameters);
                dataCommand.Parameters.Add(new SqlParameter("@skip", SqlDbType.Int) { Value = query.Skip });
                dataCommand.Parameters.Add(new SqlParameter("@take", SqlDbType.Int) { Value = query.Take });

                await using var reader = await dataCommand.ExecuteReaderAsync(cancellationToken);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    columns.Add(new ReportResultColumn
                    {
                        Name = name,
                        Label = name,
                        Hidden = query.HiddenResultNames.Contains(name)
                    });
                }

                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        row[reader.GetName(i)] = NormalizeValue(value);
                    }

                    rows.Add(row);
                }
            }

            // Align labels with layout aliases when the query used them as column names.
            for (var i = 0; i < columns.Count && i < layout.Columns.Count; i++)
            {
                var alias = layout.Columns[i].Alias;
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    columns[i] = new ReportResultColumn
                    {
                        Name = columns[i].Name,
                        Label = alias,
                        Hidden = columns[i].Hidden
                    };
                }
            }

            return new ReportResult
            {
                Columns = columns,
                Rows = rows,
                TotalCount = total,
                Skip = query.Skip,
                Take = query.Take
            };
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<(CacheProviderConnection Connection, string Collection)?> TryMongoAsync(
        Dataset dataset,
        CancellationToken cancellationToken)
    {
        if (!DatasetCacheRouting.TryGetMongoSource(dataset, out var collection))
        {
            return null;
        }

        var settings = await _cacheSettings.GetRuntimeAsync(cancellationToken);
        var connection = MongoCacheClient.Normalize(settings.Mongo);
        MongoCacheClient.Validate(connection);
        return (connection, collection);
    }

    private async Task<(CacheProviderConnection Connection, string Key)?> TryRedisAsync(
        Dataset dataset,
        CancellationToken cancellationToken)
    {
        if (!DatasetCacheRouting.TryGetRedisSource(dataset, out var key))
        {
            return null;
        }

        var settings = await _cacheSettings.GetRuntimeAsync(cancellationToken);
        var connection = RedisCacheClient.Normalize(settings.Redis);
        RedisCacheClient.Validate(connection);
        return (connection, key);
    }

    private async Task<ClickHouseRoute?> TryClickHouseAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        var settings = await _cacheSettings.GetRuntimeAsync(cancellationToken);
        if (!DatasetCacheRouting.TryGetClickHouseSource(
                dataset,
                settings.ClickHouse.Database,
                out var database,
                out var table))
        {
            return null;
        }

        ClickHouseHttpClient.Validate(settings.ClickHouse);
        return new ClickHouseRoute(settings.ClickHouse, QueryDialect.ClickHouse(database, table));
    }

    private async Task<PostgresRoute?> TryPostgresAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        if (!DatasetCacheRouting.TryGetPostgresSource(dataset, out var schema, out var table))
        {
            return null;
        }

        var settings = await _cacheSettings.GetRuntimeAsync(cancellationToken);
        var connection = PostgresCacheClient.Normalize(settings.Postgres);
        PostgresCacheClient.Validate(connection);
        return new PostgresRoute(connection, QueryDialect.Postgres(schema, table));
    }

    private async Task<ReportResult> ExecuteClickHouseAsync(
        CacheProviderConnection connection,
        BuiltQuery query,
        ReportLayout layout,
        CancellationToken cancellationToken)
    {
        var countResult = await _clickHouse.QueryAsync(connection, query.CountSql, cancellationToken, query.FilterParameters);
        var total = ScalarInt(countResult.Rows, countResult.Columns);
        var data = await _clickHouse.QueryAsync(
            connection,
            query.DataSql,
            cancellationToken,
            query.FilterParameters,
            query.AllRows ? null : query.Skip,
            query.AllRows ? null : query.Take);
        return ToReportResult(data.Columns, data.Rows, total, query, layout);
    }

    private async Task<ColumnValuesResult> ReadDistinctClickHouseAsync(
        CacheProviderConnection connection,
        BuiltDistinctQuery query,
        string field,
        int take,
        CancellationToken cancellationToken)
    {
        var result = await _clickHouse.QueryAsync(connection, query.Sql, cancellationToken, query.Parameters);
        return DistinctFromRows(result.Columns, result.Rows, field, take);
    }

    private async Task<ReportExportTable> LoadExportTableClickHouseAsync(
        CacheProviderConnection connection,
        BuiltQuery built,
        ReportLayout layout,
        string reportName,
        ReportExportFormat format,
        string? lang,
        CancellationToken cancellationToken)
    {
        var data = await _clickHouse.QueryAsync(connection, built.DataSql, cancellationToken, built.FilterParameters);
        var include = data.Columns
            .Select((name, index) => (name, index))
            .Where(item => !built.HiddenResultNames.Contains(item.name) && LayoutColumnLocales.ShouldInclude(item.index, layout, lang))
            .ToList();
        var columns = include.Select(item => item.name).ToArray();
        var columnStyles = ExportColumnStyleBuilder.For(include.Select(item => item.index).ToArray(), layout, lang);
        var rowCap = ReportExportWriter.RowCapFor(format);
        var rows = new List<IReadOnlyList<string>>();
        var truncated = false;
        foreach (var row in data.Rows)
        {
            if (rowCap is int cap && rows.Count >= cap)
            {
                truncated = true;
                break;
            }

            var cells = new string[include.Count];
            for (var c = 0; c < include.Count; c++)
            {
                var name = include[c].name;
                var value = row.GetValueOrDefault(name);
                var layoutColumn = include[c].index < layout.Columns.Count ? layout.Columns[include[c].index] : null;
                cells[c] = ColumnValueFormatter.FormatLayout(value, layoutColumn?.Format, layoutColumn, name);
            }

            rows.Add(cells);
        }

        return new ReportExportTable
        {
            Title = string.IsNullOrWhiteSpace(reportName) ? "Report" : reportName.Trim(),
            Columns = columns,
            ColumnStyles = columnStyles,
            Rows = rows,
            Truncated = truncated,
            ExportedBy = ResolveExportUserName()
        };
    }

    private async Task<ReportResult> ExecutePostgresAsync(
        CacheProviderConnection connection,
        BuiltQuery query,
        ReportLayout layout,
        CancellationToken cancellationToken)
    {
        var countResult = await _postgres.QueryAsync(connection, query.CountSql, cancellationToken, query.FilterParameters);
        var total = ScalarInt(countResult.Rows, countResult.Columns);
        var data = await _postgres.QueryAsync(
            connection,
            query.DataSql,
            cancellationToken,
            query.FilterParameters,
            query.AllRows ? null : query.Skip,
            query.AllRows ? null : query.Take);
        return ToReportResult(data.Columns, data.Rows, total, query, layout);
    }

    private async Task<ColumnValuesResult> ReadDistinctPostgresAsync(
        CacheProviderConnection connection,
        BuiltDistinctQuery query,
        string field,
        int take,
        CancellationToken cancellationToken)
    {
        var result = await _postgres.QueryAsync(connection, query.Sql, cancellationToken, query.Parameters);
        return DistinctFromRows(result.Columns, result.Rows, field, take);
    }

    private async Task<ReportExportTable> LoadExportTablePostgresAsync(
        CacheProviderConnection connection,
        BuiltQuery built,
        ReportLayout layout,
        string reportName,
        ReportExportFormat format,
        string? lang,
        CancellationToken cancellationToken)
    {
        var data = await _postgres.QueryAsync(connection, built.DataSql, cancellationToken, built.FilterParameters);
        var include = data.Columns
            .Select((name, index) => (name, index))
            .Where(item => !built.HiddenResultNames.Contains(item.name) && LayoutColumnLocales.ShouldInclude(item.index, layout, lang))
            .ToList();
        var columns = include.Select(item => item.name).ToArray();
        var columnStyles = ExportColumnStyleBuilder.For(include.Select(item => item.index).ToArray(), layout, lang);
        var rowCap = ReportExportWriter.RowCapFor(format);
        var rows = new List<IReadOnlyList<string>>();
        var truncated = false;
        foreach (var row in data.Rows)
        {
            if (rowCap is int cap && rows.Count >= cap)
            {
                truncated = true;
                break;
            }

            var cells = new string[include.Count];
            for (var c = 0; c < include.Count; c++)
            {
                var name = include[c].name;
                var value = row.GetValueOrDefault(name);
                var layoutColumn = include[c].index < layout.Columns.Count ? layout.Columns[include[c].index] : null;
                cells[c] = ColumnValueFormatter.FormatLayout(value, layoutColumn?.Format, layoutColumn, name);
            }

            rows.Add(cells);
        }

        return new ReportExportTable
        {
            Title = string.IsNullOrWhiteSpace(reportName) ? "Report" : reportName.Trim(),
            Columns = columns,
            ColumnStyles = columnStyles,
            Rows = rows,
            Truncated = truncated,
            ExportedBy = ResolveExportUserName()
        };
    }

    private readonly record struct ClickHouseRoute(CacheProviderConnection Connection, QueryDialect Dialect);

    private readonly record struct PostgresRoute(CacheProviderConnection Connection, QueryDialect Dialect);

    private static int ScalarInt(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<string> columns)
    {
        if (rows.Count == 0 || columns.Count == 0)
        {
            return 0;
        }

        var scalar = rows[0].GetValueOrDefault(columns[0]);
        return scalar is null ? 0 : Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    }

    private static ReportResult ToReportResult(
        IReadOnlyList<string> names,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> dataRows,
        int total,
        BuiltQuery query,
        ReportLayout layout)
    {
        var columns = names.Select(name => new ReportResultColumn
        {
            Name = name,
            Label = name,
            Hidden = query.HiddenResultNames.Contains(name)
        }).ToList();

        var rows = dataRows.Select(row =>
        {
            var mapped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in names)
            {
                mapped[column] = NormalizeValue(row.GetValueOrDefault(column));
            }

            return (IReadOnlyDictionary<string, object?>)mapped;
        }).ToList();

        for (var i = 0; i < columns.Count && i < layout.Columns.Count; i++)
        {
            var alias = layout.Columns[i].Alias;
            if (!string.IsNullOrWhiteSpace(alias))
            {
                columns[i] = new ReportResultColumn
                {
                    Name = columns[i].Name,
                    Label = alias,
                    Hidden = columns[i].Hidden
                };
            }
        }

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            TotalCount = total,
            Skip = query.Skip,
            Take = query.Take
        };
    }

    private static ColumnValuesResult DistinctFromRows(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string field,
        int take)
    {
        var values = new List<string>();
        foreach (var row in rows)
        {
            if (columns.Count == 0)
            {
                continue;
            }

            var formatted = FormatDistinctValue(row.GetValueOrDefault(columns[0]) ?? DBNull.Value);
            if (!string.IsNullOrWhiteSpace(formatted)
                && values.TrueForAll(existing => !existing.Equals(formatted, StringComparison.OrdinalIgnoreCase)))
            {
                values.Add(formatted);
            }
        }

        var truncated = values.Count > take;
        if (truncated)
        {
            values.RemoveRange(take, values.Count - take);
        }

        return new ColumnValuesResult
        {
            Field = field,
            Values = values,
            Truncated = truncated
        };
    }

    private static void AddFilterParameters(SqlCommand command, IReadOnlyList<QueryParameter> parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new SqlParameter(parameter.Name, parameter.Value ?? DBNull.Value));
        }
    }

    private static object? NormalizeValue(object? value)
        => value switch
        {
            DBNull => null,
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            DateTimeOffset dto => dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            DateOnly d => d.ToString("yyyy-MM-dd"),
            decimal m => m,
            byte[] => "[binary]",
            _ => value
        };
}
