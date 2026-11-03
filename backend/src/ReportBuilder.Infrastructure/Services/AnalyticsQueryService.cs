using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Cache;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

public sealed class AnalyticsQueryService : IAnalyticsQueryService
{
    private static readonly AsyncLocal<IAnalyticsSqlLogSession?> SqlLog = new();

    private readonly ReportBuilderDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAnalyticsSqlLog _sqlLog;
    private readonly IMemoryCache _cache;
    private readonly MongoCacheClient _mongo;
    private readonly ICacheSettingsService _cacheSettings;
    private readonly TimeSpan _cacheTtl;
    private readonly TimeSpan _publishedCacheTtl;
    private readonly TimeSpan _unfilteredCacheTtl;
    private readonly int _commandTimeoutSeconds;
    private readonly bool _singlePassDimensions;
    private readonly bool _useDocumentCache;
    private readonly ILogger<AnalyticsQueryService> _logger;

    public AnalyticsQueryService(
        ReportBuilderDbContext db,
        ICurrentUser currentUser,
        IAnalyticsSqlLog sqlLog,
        IMemoryCache cache,
        MongoCacheClient mongo,
        ICacheSettingsService cacheSettings,
        IConfiguration configuration,
        ILogger<AnalyticsQueryService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _sqlLog = sqlLog;
        _cache = cache;
        _mongo = mongo;
        _cacheSettings = cacheSettings;
        _logger = logger;
        _cacheTtl = TimeSpan.FromSeconds(
            Math.Clamp(configuration.GetValue("Analytics:ResultCacheSeconds", 60), 0, 3600));
        // A published dashboard is read by many people clicking through the same handful of states, so
        // its results stay shared longer. Drafts keep the short window because the designer is editing.
        _publishedCacheTtl = TimeSpan.FromSeconds(
            Math.Clamp(configuration.GetValue("Analytics:PublishedResultCacheSeconds", 300), 0, 3600));
        // The unfiltered state is the one every visitor opens first and the only one that has to read
        // the whole dataset. When the data comes from a dataset snapshot it cannot change until the next
        // refresh, and the refresh bumps the cache version in the key, so it can be kept far longer.
        _unfilteredCacheTtl = TimeSpan.FromSeconds(
            Math.Clamp(configuration.GetValue("Analytics:UnfilteredResultCacheSeconds", 21600), 0, 86400));
        _commandTimeoutSeconds =
            Math.Clamp(configuration.GetValue("Analytics:CommandTimeoutSeconds", 60), 5, 600);
        _singlePassDimensions = configuration.GetValue("Analytics:SinglePassDimensions", true);
        _useDocumentCache = configuration.GetValue("Analytics:UseDatasetCache", true);
    }

    private sealed record Prepared(
        AnalyticsDashboard Dashboard,
        AnalyticsQueryBuilder.Context Context,
        Dictionary<string, List<string>> Selections,
        int Skip,
        int Take,
        string StateKey,
        DocumentRoute? Document,
        string Source)
    {
        public bool ServedFromSnapshot => Source != AnalyticsDataSources.Live;
    }

    /// <summary>
    /// Resolved when the dashboard's only dataset has a ready snapshot in a document store the analytics
    /// engine can query directly (MongoDB today) and the dashboard uses plain column references, so
    /// every pane can be answered from the cache without touching the live view.
    /// </summary>
    private sealed record DocumentRoute(CacheProviderConnection Connection, string Collection, Dataset Dataset);

    public async Task<AnalyticsSnapshotResult> QuerySnapshotAsync(
        Guid dashboardId,
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(dashboardId, request, cancellationToken);
        return await RunAsync(
            prepared,
            "snapshot",
            discriminator: string.Empty,
            (connection, ct) => ReadSnapshotAsync(connection, prepared, ct),
            (route, ct) => _mongo.QueryAnalyticsSnapshotAsync(
                route.Connection,
                route.Collection,
                route.Dataset,
                prepared.Context.Config,
                prepared.Selections,
                CommandTimeout,
                RecordDocumentQuery,
                ct),
            cancellationToken);
    }

    private TimeSpan CommandTimeout => TimeSpan.FromSeconds(_commandTimeoutSeconds);

    public async Task<AnalyticsDataResult> QueryDataAsync(
        Guid dashboardId,
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(dashboardId, request, cancellationToken);
        return await RunAsync(
            prepared,
            "data",
            discriminator: $"{prepared.Skip}:{prepared.Take}",
            async (connection, ct) =>
            {
                var (columns, rows, total) = await ReadGridAsync(
                    connection,
                    prepared.Context,
                    prepared.Selections,
                    prepared.Skip,
                    prepared.Take,
                    ct);
                return ToDataResult(prepared, columns, rows, total);
            },
            async (route, ct) =>
            {
                var (columns, rows, total) = await _mongo.QueryAnalyticsGridAsync(
                    route.Connection,
                    route.Collection,
                    route.Dataset,
                    prepared.Context.Config,
                    prepared.Selections,
                    prepared.Skip,
                    prepared.Take,
                    CommandTimeout,
                    RecordDocumentQuery,
                    ct);
                return ToDataResult(prepared, columns, rows, total);
            },
            cancellationToken);
    }

    private static AnalyticsDataResult ToDataResult(
        Prepared prepared,
        IReadOnlyList<AnalyticsGridColumn> columns,
        IReadOnlyList<Dictionary<string, object?>> rows,
        long total)
        => new()
        {
            Columns = columns,
            Rows = rows,
            TotalCount = total,
            Skip = prepared.Skip,
            Take = prepared.Take
        };

    public async Task<AnalyticsMapResult> QueryMapAsync(
        Guid dashboardId,
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(dashboardId, request, cancellationToken);
        if (prepared.Context.Config.Map is not { Enabled: true })
        {
            return new AnalyticsMapResult { MapPoints = [] };
        }

        return await RunAsync(
            prepared,
            "map",
            discriminator: string.Empty,
            async (connection, ct) => new AnalyticsMapResult
            {
                MapPoints = await ReadMapAsync(connection, prepared.Context, prepared.Selections, ct)
            },
            async (route, ct) => new AnalyticsMapResult
            {
                MapPoints = await _mongo.QueryAnalyticsMapAsync(
                    route.Connection,
                    route.Collection,
                    route.Dataset,
                    prepared.Context.Config,
                    prepared.Selections,
                    CommandTimeout,
                    RecordDocumentQuery,
                    ct)
            },
            cancellationToken);
    }

    /// <summary>
    /// Serves a cached result when the same selection state was queried recently. Otherwise the work runs
    /// against the dataset's document cache when the dashboard can be answered from it, and against SQL
    /// Server on one connection for the whole unit of work — one connection per request keeps a busy
    /// dashboard from draining the pool, which a per-pane fan-out could do with a single user's click.
    /// </summary>
    private async Task<T> RunAsync<T>(
        Prepared prepared,
        string kind,
        string discriminator,
        Func<SqlConnection, CancellationToken, Task<T>> sqlWork,
        Func<DocumentRoute, CancellationToken, Task<T>>? documentWork,
        CancellationToken cancellationToken) where T : class
    {
        var ttl = ResolveCacheTtl(prepared);
        var cacheKey = $"analytics:{kind}:{prepared.StateKey}:{discriminator}";
        if (ttl > TimeSpan.Zero && _cache.TryGetValue<T>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        await using var sqlLog = _sqlLog.Begin(prepared.Dashboard.Id, prepared.Dashboard.Name, kind);
        SqlLog.Value = sqlLog;
        var started = Stopwatch.StartNew();
        try
        {
            T result;
            if (prepared.Document is { } route && documentWork is not null)
            {
                result = await documentWork(route, cancellationToken);
            }
            else
            {
                var connectionString = _db.Database.GetConnectionString()
                    ?? throw new ReportQueryException("Database connection is not configured.");
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                result = await sqlWork(connection, cancellationToken);
            }

            _logger.LogInformation(
                "Analytics {Kind} for {Dashboard} served from {Source} in {ElapsedMs} ms ({SelectionCount} selections).",
                kind,
                prepared.Dashboard.Name,
                prepared.Source,
                started.ElapsedMilliseconds,
                prepared.Selections.Count);

            if (ttl > TimeSpan.Zero)
            {
                // Low priority so these are the first entries evicted under memory pressure — they are
                // an optimization, and a miss only costs the query we would have run anyway.
                _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl,
                    Priority = CacheItemPriority.Low
                });
            }

            return result;
        }
        finally
        {
            SqlLog.Value = null;
        }
    }

    /// <summary>
    /// Draft dashboards keep the short window because the designer is editing. Published ones share
    /// results longer. The unfiltered state of a dashboard served from a dataset snapshot is kept until
    /// the snapshot is refreshed (the cache version is part of the key), since nothing else can change it.
    /// </summary>
    private TimeSpan ResolveCacheTtl(Prepared prepared)
    {
        var ttl = prepared.Dashboard.Status == ReportStatus.Published ? _publishedCacheTtl : _cacheTtl;
        if (prepared.Selections.Count == 0 && prepared.ServedFromSnapshot && _unfilteredCacheTtl > ttl)
        {
            ttl = _unfilteredCacheTtl;
        }

        return ttl;
    }

    /// <summary>Records a document-cache query in the same log the SQL statements go to.</summary>
    private static void RecordDocumentQuery(string name, string text)
        => SqlLog.Value?.Record(name, new AnalyticsQueryBuilder.BuiltAnalyticsQuery
        {
            Sql = text,
            Parameters = []
        });

    /// <summary>
    /// Every dimension, the measure row, and every chart run as one command with one result set each.
    /// They are independent queries, so batching them costs a single round trip instead of one per pane.
    /// A joined dashboard materializes the join into a temp table first, in the same batch, so the join
    /// executes once for the whole snapshot.
    /// </summary>
    private async Task<AnalyticsSnapshotResult> ReadSnapshotAsync(
        SqlConnection connection,
        Prepared prepared,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var context = prepared.Context;
        var selections = prepared.Selections;
        var bag = new AnalyticsQueryBuilder.ParameterBag();
        var batch = new StringBuilder("SET NOCOUNT ON;");

        if (context.Config.Sources.Count > 1)
        {
            var (materializeSql, materialized) = AnalyticsQueryBuilder.BuildMaterialize(context);
            batch.AppendLine().AppendLine(materializeSql).Append(';');
            context = materialized;
        }

        var sections = new List<Section>();
        if (_singlePassDimensions && AnalyticsQueryBuilder.CanUseSinglePassDimensions(context))
        {
            sections.Add(new Section(SectionKind.AllDimensions, null, null));
            batch.AppendLine().AppendLine(
                AnalyticsQueryBuilder.DimensionsSinglePassSql(context, selections, bag))
                .Append(';');
        }
        else
        {
            foreach (var dimension in context.Config.Dimensions)
            {
                sections.Add(new Section(SectionKind.Dimension, dimension, null));
                batch.AppendLine().AppendLine(
                    AnalyticsQueryBuilder.DimensionDistributionSql(context, dimension, selections, dimension.Id, bag))
                    .Append(';');
            }
        }

        if (context.Config.Measures.Count > 0)
        {
            sections.Add(new Section(SectionKind.Measures, null, null));
            batch.AppendLine().AppendLine(AnalyticsQueryBuilder.MeasuresSql(context, selections, bag)).Append(';');
        }

        foreach (var chart in context.Config.Charts)
        {
            sections.Add(new Section(SectionKind.Chart, null, chart));
            batch.AppendLine().AppendLine(AnalyticsQueryBuilder.ChartSql(context, chart, selections, bag)).Append(';');
        }

        var dimensions = new List<AnalyticsDimensionResult>(context.Config.Dimensions.Count);
        var charts = new List<AnalyticsChartResult>(context.Config.Charts.Count);
        IReadOnlyList<AnalyticsMeasureResult> measures = [];

        if (sections.Count == 0)
        {
            return new AnalyticsSnapshotResult
            {
                Dimensions = dimensions,
                Measures = measures,
                Charts = charts,
                Source = prepared.Source
            };
        }

        var built = new AnalyticsQueryBuilder.BuiltAnalyticsQuery
        {
            Sql = batch.ToString(),
            Parameters = bag.Parameters
        };

        await using var command = CreateCommand(connection, built, "snapshot");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        for (var index = 0; index < sections.Count; index++)
        {
            if (index > 0 && !await reader.NextResultAsync(cancellationToken))
            {
                break;
            }

            var section = sections[index];
            switch (section.Kind)
            {
                case SectionKind.Dimension:
                    dimensions.Add(await ReadDimensionAsync(reader, section.Dimension!, selections, cancellationToken));
                    break;
                case SectionKind.AllDimensions:
                    dimensions.AddRange(await ReadAllDimensionsAsync(reader, context.Config.Dimensions, selections, cancellationToken));
                    break;
                case SectionKind.Measures:
                    measures = await ReadMeasuresAsync(reader, context.Config.Measures, cancellationToken);
                    break;
                case SectionKind.Chart:
                    charts.Add(await ReadChartAsync(reader, context, section.Chart!, cancellationToken));
                    break;
            }
        }

        return new AnalyticsSnapshotResult
        {
            Dimensions = dimensions,
            Measures = measures,
            Charts = charts,
            Source = prepared.Source,
            ElapsedMs = started.ElapsedMilliseconds
        };
    }

    private enum SectionKind
    {
        Dimension,
        AllDimensions,
        Measures,
        Chart
    }

    /// <summary>
    /// Reads the (dimension index, value, count) rows of the single-pass statement back into one list per
    /// dimension. Rows arrive ordered by dimension then rank, and a dimension with no rows still gets an
    /// entry so the page renders every configured filter.
    /// </summary>
    private static async Task<List<AnalyticsDimensionResult>> ReadAllDimensionsAsync(
        SqlDataReader reader,
        IReadOnlyList<AnalyticsDimension> dimensions,
        Dictionary<string, List<string>> selections,
        CancellationToken cancellationToken)
    {
        var buckets = new List<List<AnalyticsDimensionValue>>(dimensions.Count);
        var selectedByIndex = new List<IReadOnlyList<string>>(dimensions.Count);
        foreach (var dimension in dimensions)
        {
            buckets.Add([]);
            selectedByIndex.Add(selections.TryGetValue(dimension.Id, out var values) ? values : []);
        }

        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            var index = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            if (index < 0 || index >= buckets.Count)
            {
                continue;
            }

            var value = reader.IsDBNull(1)
                ? AnalyticsNullValue.Sentinel
                : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? AnalyticsNullValue.Sentinel;
            var count = reader.IsDBNull(2) ? 0L : Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture);
            buckets[index].Add(new AnalyticsDimensionValue
            {
                Value = value,
                Count = count,
                State = AnalyticsQueryBuilder.ResolveValueState(value, count, selectedByIndex[index])
            });
        }

        var results = new List<AnalyticsDimensionResult>(dimensions.Count);
        for (var index = 0; index < dimensions.Count; index++)
        {
            var dimension = dimensions[index];
            results.Add(new AnalyticsDimensionResult
            {
                DimensionId = dimension.Id,
                Label = string.IsNullOrWhiteSpace(dimension.Label) ? dimension.Field : dimension.Label,
                SelectedValues = selectedByIndex[index],
                Values = buckets[index]
            });
        }

        return results;
    }

    private sealed record Section(SectionKind Kind, AnalyticsDimension? Dimension, AnalyticsChart? Chart);

    public async Task<AnalyticsSearchResult> SearchValuesAsync(
        Guid dashboardId,
        AnalyticsValueSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = (request.Query ?? string.Empty).Trim();
        if (query.Length < AnalyticsQueryBuilder.MinSearchLength)
        {
            return new AnalyticsSearchResult { Hits = [] };
        }

        var prepared = await PrepareAsync(
            dashboardId,
            new AnalyticsQueryRequest
            {
                Selections = request.Selections,
                Take = request.Take
            },
            cancellationToken);

        var take = request.Take <= 0
            ? AnalyticsQueryBuilder.MaxSearchHits
            : Math.Min(request.Take, AnalyticsQueryBuilder.MaxSearchHits);
        var dimensionId = string.IsNullOrWhiteSpace(request.DimensionId)
            ? null
            : request.DimensionId.Trim();

        return await RunAsync(
            prepared,
            "search",
            $"{dimensionId}:{take}:{query.ToLowerInvariant()}",
            (connection, ct) => ReadSearchAsync(connection, prepared, query, dimensionId, take, ct),
            async (route, ct) =>
            {
                var dimensions = SearchDimensions(prepared.Context, dimensionId);
                var hits = await _mongo.QueryAnalyticsSearchAsync(
                    route.Connection,
                    route.Collection,
                    route.Dataset,
                    prepared.Context.Config,
                    prepared.Selections,
                    dimensions,
                    query,
                    SearchTakePerDimension(dimensions.Count, take),
                    CommandTimeout,
                    RecordDocumentQuery,
                    ct);
                return RankSearchHits(hits, take);
            },
            cancellationToken);
    }

    private static List<AnalyticsDimension> SearchDimensions(AnalyticsQueryBuilder.Context context, string? dimensionId)
        => context.Config.Dimensions
            .Where(dimension =>
                dimensionId is null
                || string.Equals(dimension.Id, dimensionId, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static int SearchTakePerDimension(int dimensionCount, int take)
        => dimensionCount == 1 ? take : Math.Min(AnalyticsQueryBuilder.MaxSearchHitsPerDimension, take);

    private static AnalyticsSearchResult RankSearchHits(IEnumerable<AnalyticsSearchHit> hits, int take)
        => new()
        {
            Hits = hits
                .OrderBy(hit => hit.State == AnalyticsValueStates.Excluded ? 1 : 0)
                .ThenByDescending(hit => hit.Count)
                .ThenBy(hit => hit.DimensionLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(hit => hit.Value, StringComparer.OrdinalIgnoreCase)
                .Take(take)
                .ToList()
        };

    private async Task<AnalyticsSearchResult> ReadSearchAsync(
        SqlConnection connection,
        Prepared prepared,
        string query,
        string? dimensionId,
        int take,
        CancellationToken cancellationToken)
    {
        var context = prepared.Context;
        var selections = prepared.Selections;
        var dimensions = SearchDimensions(context, dimensionId);
        if (dimensions.Count == 0)
        {
            return new AnalyticsSearchResult { Hits = [] };
        }

        var takePer = SearchTakePerDimension(dimensions.Count, take);

        var bag = new AnalyticsQueryBuilder.ParameterBag();
        var batch = new StringBuilder("SET NOCOUNT ON;");
        if (context.Config.Sources.Count > 1)
        {
            var (materializeSql, materialized) = AnalyticsQueryBuilder.BuildMaterialize(context);
            batch.AppendLine().AppendLine(materializeSql).Append(';');
            context = materialized;
        }

        foreach (var dimension in dimensions)
        {
            batch.AppendLine().AppendLine(
                AnalyticsQueryBuilder.DimensionSearchSql(context, dimension, selections, query, takePer, bag))
                .Append(';');
        }

        var built = new AnalyticsQueryBuilder.BuiltAnalyticsQuery
        {
            Sql = batch.ToString(),
            Parameters = bag.Parameters
        };

        await using var command = CreateCommand(connection, built, "search");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var hits = new List<AnalyticsSearchHit>();
        for (var index = 0; index < dimensions.Count; index++)
        {
            if (index > 0 && !await reader.NextResultAsync(cancellationToken))
            {
                break;
            }

            var dimension = dimensions[index];
            var selected = selections.TryGetValue(dimension.Id, out var values) ? values : [];
            var label = string.IsNullOrWhiteSpace(dimension.Label) ? dimension.Field : dimension.Label;
            while (await reader.ReadAsync(cancellationToken))
            {
                var value = reader.IsDBNull(0)
                    ? AnalyticsNullValue.Sentinel
                    : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)
                      ?? AnalyticsNullValue.Sentinel;
                var count = reader.IsDBNull(1)
                    ? 0L
                    : Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
                hits.Add(new AnalyticsSearchHit
                {
                    DimensionId = dimension.Id,
                    DimensionLabel = label,
                    Value = value,
                    Count = count,
                    State = AnalyticsQueryBuilder.ResolveValueState(value, count, selected)
                });
            }
        }

        return RankSearchHits(hits, take);
    }

    public async Task<(string FileName, byte[] Content, string ContentType)> ExportCsvAsync(
        Guid dashboardId,
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var exportRequest = new AnalyticsQueryRequest
        {
            Selections = request.Selections,
            Skip = 0,
            Take = AnalyticsQueryBuilder.MaxTake
        };
        var prepared = await PrepareAsync(dashboardId, exportRequest, cancellationToken);

        await using var sqlLog = _sqlLog.Begin(prepared.Dashboard.Id, prepared.Dashboard.Name, "export");
        SqlLog.Value = sqlLog;
        try
        {
            if (prepared.Document is { } route)
            {
                var (documentColumns, documentRows, _) = await _mongo.QueryAnalyticsGridAsync(
                    route.Connection,
                    route.Collection,
                    route.Dataset,
                    prepared.Context.Config,
                    prepared.Selections,
                    skip: 0,
                    prepared.Take,
                    CommandTimeout,
                    RecordDocumentQuery,
                    cancellationToken);
                return await WriteCsvAsync(prepared.Dashboard.Name, documentColumns, documentRows);
            }

            var connectionString = _db.Database.GetConnectionString()
                ?? throw new ReportQueryException("Database connection is not configured.");
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            var (columns, gridRows, _) = await ReadGridAsync(
                connection,
                prepared.Context,
                prepared.Selections,
                skip: 0,
                prepared.Take,
                cancellationToken);

            return await WriteCsvAsync(prepared.Dashboard.Name, columns, gridRows);
        }
        finally
        {
            SqlLog.Value = null;
        }
    }

    private static async Task<(string FileName, byte[] Content, string ContentType)> WriteCsvAsync(
        string dashboardName,
        IReadOnlyList<AnalyticsGridColumn> columns,
        IReadOnlyList<Dictionary<string, object?>> gridRows)
    {
        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
        {
            await writer.WriteLineAsync(string.Join(",", columns.Select(column => CsvEscape(column.Label))));
            foreach (var row in gridRows)
            {
                var cells = columns.Select(column =>
                {
                    row.TryGetValue(column.Id, out var value);
                    return CsvEscape(value is null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                });
                await writer.WriteLineAsync(string.Join(",", cells));
            }
        }

        var safeName = string.Concat(dashboardName.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "analytics";
        }

        return ($"{safeName}.csv", stream.ToArray(), "text/csv");
    }

    private async Task<Prepared> PrepareAsync(
        Guid dashboardId,
        AnalyticsQueryRequest request,
        CancellationToken cancellationToken)
    {
        var dashboard = await _db.AnalyticsDashboards.AsNoTracking()
            .Include(item => item.Access)
            .Include(item => item.RoleAccess)
            .ThenInclude(row => row.Role)
            .FirstOrDefaultAsync(item => item.Id == dashboardId, cancellationToken)
            ?? throw new ReportQueryException("Analytics dashboard was not found.");

        if (!AnalyticsAccessRules.CanView(dashboard, _currentUser))
        {
            throw new IdentityException(
                dashboard.Status == ReportStatus.Draft
                    ? "This draft dashboard is only available to its owner or an administrator."
                    : "You do not have access to this analytics dashboard.",
                403);
        }

        var metadata = await GetMetadataAsync(dashboard, cancellationToken);

        foreach (var dataset in metadata.Datasets)
        {
            if (!DatasetAccessRules.CanUseInDesigner(dataset, _currentUser))
            {
                throw new IdentityException($"You do not have access to dataset '{dataset.DisplayName}'.", 403);
            }
        }

        var context = AnalyticsQueryBuilder.CreateContext(
            metadata.Config,
            metadata.Datasets.ToDictionary(item => item.Id));
        var selections = NormalizeSelections(request.Selections);
        var skip = Math.Max(request.Skip, 0);
        var take = request.Take <= 0
            ? AnalyticsQueryBuilder.DefaultTake
            : Math.Min(request.Take, AnalyticsQueryBuilder.MaxTake);
        var stateKey = BuildStateKey(dashboard, metadata, selections);
        var document = await ResolveDocumentRouteAsync(metadata, cancellationToken);
        var source = document is not null
            ? AnalyticsDataSources.MongoCache
            : metadata.Datasets.All(dataset => DatasetCacheRouting.TryGetCachedSource(dataset, out _, out _))
                ? AnalyticsDataSources.SqlServerCache
                : AnalyticsDataSources.Live;
        return new Prepared(dashboard, context, selections, skip, take, stateKey, document, source);
    }

    /// <summary>
    /// Dashboards read whichever dataset cache the settings point at, the same way reports do, and fall
    /// back to the live view otherwise. SQL Server snapshots are handled by the query builder (the
    /// <c>FROM</c> clause names the <c>cache.DsCache_…</c> table). MongoDB snapshots are queried here as
    /// documents when the dashboard has one source and plain columns. ClickHouse, PostgreSQL, and Redis
    /// snapshots are not yet queried by analytics, so those datasets read live. The dataset cache version
    /// is part of the state key, so a refresh invalidates every shared result.
    /// </summary>
    private async Task<DocumentRoute?> ResolveDocumentRouteAsync(DashboardMetadata metadata, CancellationToken cancellationToken)
    {
        if (!_useDocumentCache
            || metadata.Datasets.Count != 1
            || !MongoCacheClient.SupportsAnalytics(metadata.Config))
        {
            return null;
        }

        var dataset = metadata.Datasets[0];
        if (!DatasetCacheRouting.TryGetMongoSource(dataset, out var collection))
        {
            return null;
        }

        var settings = await _cacheSettings.GetRuntimeAsync(cancellationToken);
        var connection = MongoCacheClient.Normalize(settings.Mongo);
        MongoCacheClient.Validate(connection);
        return new DocumentRoute(connection, collection, dataset);
    }

    private sealed record DashboardMetadata(
        AnalyticsDashboardConfig Config,
        IReadOnlyList<Dataset> Datasets,
        int CacheVersion);

    /// <summary>
    /// Reloading the config and every dataset's columns on each request is wasted work now that a single
    /// interaction hits three endpoints. Keyed on the dashboard's own <c>UpdatedAt</c> so edits are picked
    /// up immediately, with a short expiry to bound how stale dataset metadata can get.
    /// </summary>
    private async Task<DashboardMetadata> GetMetadataAsync(
        AnalyticsDashboard dashboard,
        CancellationToken cancellationToken)
    {
        var key = $"analytics:meta:{dashboard.Id:N}:{dashboard.UpdatedAt.UtcTicks}";
        if (_cacheTtl > TimeSpan.Zero
            && _cache.TryGetValue<DashboardMetadata>(key, out var cached)
            && cached is not null)
        {
            return cached;
        }

        var config = AnalyticsConfigJson.Deserialize(dashboard.ConfigJson);
        if (config.Sources.Count == 0)
        {
            throw new ReportQueryException("Dashboard has no sources configured.");
        }

        var datasetIds = config.Sources.Select(item => item.DatasetId).Distinct().ToList();
        var datasets = await _db.Datasets.AsNoTracking()
            .Include(item => item.Columns)
            .Include(item => item.Access)
            .Include(item => item.RoleAccess)
                .ThenInclude(row => row.Role)
            .Include(item => item.CacheState)
            .Where(item => datasetIds.Contains(item.Id))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var cacheVersion = datasets.Aggregate(0, (max, item) => Math.Max(max, item.CacheState?.CacheVersion ?? 0));
        var metadata = new DashboardMetadata(config, datasets, cacheVersion);
        if (_cacheTtl > TimeSpan.Zero)
        {
            _cache.Set(key, metadata, _cacheTtl);
        }

        return metadata;
    }

    /// <summary>
    /// Identifies one selection state against one version of the data. Results do not vary by user — access
    /// is enforced before this point — so the entry is safely shared across callers.
    /// </summary>
    private static string BuildStateKey(
        AnalyticsDashboard dashboard,
        DashboardMetadata metadata,
        Dictionary<string, List<string>> selections)
    {
        var text = new StringBuilder()
            .Append(dashboard.Id.ToString("N"))
            .Append('|').Append(dashboard.UpdatedAt.UtcTicks)
            .Append('|').Append(metadata.CacheVersion);

        foreach (var key in selections.Keys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            text.Append('|').Append(key).Append('=');
            foreach (var value in selections[key].OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                text.Append(value).Append(',');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }

    private static Dictionary<string, List<string>> NormalizeSelections(
        Dictionary<string, List<string>>? selections)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (selections is null)
        {
            return result;
        }

        foreach (var (key, values) in selections)
        {
            if (string.IsNullOrWhiteSpace(key) || values is null || values.Count == 0)
            {
                continue;
            }

            result[key] = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return result;
    }

    private static async Task<AnalyticsDimensionResult> ReadDimensionAsync(
        SqlDataReader reader,
        AnalyticsDimension dimension,
        Dictionary<string, List<string>> selections,
        CancellationToken cancellationToken)
    {
        var selected = selections.TryGetValue(dimension.Id, out var values) ? values : [];
        var rows = new List<AnalyticsDimensionValue>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = reader.IsDBNull(0)
                ? AnalyticsNullValue.Sentinel
                : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? AnalyticsNullValue.Sentinel;
            var count = reader.IsDBNull(1) ? 0L : Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
            rows.Add(new AnalyticsDimensionValue
            {
                Value = value,
                Count = count,
                State = AnalyticsQueryBuilder.ResolveValueState(value, count, selected)
            });
        }

        return new AnalyticsDimensionResult
        {
            DimensionId = dimension.Id,
            Label = string.IsNullOrWhiteSpace(dimension.Label) ? dimension.Field : dimension.Label,
            SelectedValues = selected,
            Values = rows
        };
    }

    private static async Task<IReadOnlyList<AnalyticsMeasureResult>> ReadMeasuresAsync(
        SqlDataReader reader,
        IReadOnlyList<AnalyticsMeasure> measures,
        CancellationToken cancellationToken)
    {
        if (measures.Count == 0)
        {
            return [];
        }

        if (!await reader.ReadAsync(cancellationToken))
        {
            return measures
                .Select(measure => new AnalyticsMeasureResult
                {
                    MeasureId = measure.Id,
                    Label = measure.Label,
                    FormattedValue = "0",
                    NumericValue = 0
                })
                .ToList();
        }

        var ordinals = ResolveOrdinals(reader, measures.Select(measure => measure.Id));
        var results = new List<AnalyticsMeasureResult>(measures.Count);
        foreach (var measure in measures)
        {
            object? raw = null;
            if (ordinals.TryGetValue(measure.Id, out var ordinal) && !reader.IsDBNull(ordinal))
            {
                raw = reader.GetValue(ordinal);
            }

            double? numeric = null;
            if (raw is IConvertible convertible)
            {
                try
                {
                    numeric = convertible.ToDouble(CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    numeric = null;
                }
            }

            results.Add(new AnalyticsMeasureResult
            {
                MeasureId = measure.Id,
                Label = measure.Label,
                NumericValue = numeric,
                FormattedValue = FormatMeasure(raw, measure.Format)
            });
        }

        return results;
    }

    /// <summary>Resolves column ordinals once so row reads index by position instead of by name.</summary>
    private static Dictionary<string, int> ResolveOrdinals(SqlDataReader reader, IEnumerable<string> names)
    {
        var available = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            available[reader.GetName(i)] = i;
        }

        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (available.TryGetValue(name, out var ordinal))
            {
                ordinals[name] = ordinal;
            }
        }

        return ordinals;
    }

    private async Task<(
        IReadOnlyList<AnalyticsGridColumn> Columns,
        IReadOnlyList<Dictionary<string, object?>> Rows,
        long Total)> ReadGridAsync(
        SqlConnection connection,
        AnalyticsQueryBuilder.Context context,
        Dictionary<string, List<string>> selections,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var detailColumns = context.Config.DetailColumns.Count > 0
            ? context.Config.DetailColumns
            : context.Config.Dimensions
                .Select(dimension => new AnalyticsDetailColumn
                {
                    Id = dimension.Id,
                    SourceId = dimension.SourceId,
                    Field = dimension.Field,
                    Label = dimension.Label
                })
                .ToList();

        if (detailColumns.Count == 0)
        {
            return ([], [], 0);
        }

        var built = AnalyticsQueryBuilder.BuildGrid(context, selections, skip, take);

        var rows = new List<Dictionary<string, object?>>();
        long total = 0;
        await using (var pageCommand = CreateCommand(connection, built, "grid"))
        await using (var reader = await pageCommand.ExecuteReaderAsync(cancellationToken))
        {
            var ordinals = ResolveOrdinals(reader, detailColumns.Select(column => column.Id).Append("_total"));
            var hasTotal = ordinals.TryGetValue("_total", out var totalOrdinal);
            var cells = detailColumns
                .Select(column => (column.Id, Ordinal: ordinals.TryGetValue(column.Id, out var o) ? o : -1))
                .ToList();

            while (await reader.ReadAsync(cancellationToken))
            {
                if (rows.Count == 0 && hasTotal && !reader.IsDBNull(totalOrdinal))
                {
                    total = Convert.ToInt64(reader.GetValue(totalOrdinal), CultureInfo.InvariantCulture);
                }

                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (id, ordinal) in cells)
                {
                    if (ordinal < 0 || reader.IsDBNull(ordinal))
                    {
                        row[id] = null;
                        continue;
                    }

                    var text = AnalyticsQueryBuilder.FormatCell(reader.GetValue(ordinal));
                    row[id] = text == AnalyticsNullValue.Sentinel ? null : text;
                }

                rows.Add(row);
            }
        }

        // The page query carries the total only on rows it returns, so a page past the end reports none.
        if (rows.Count == 0 && skip > 0)
        {
            total = await CountGridAsync(connection, context, selections, cancellationToken);
        }

        var columns = detailColumns
            .Select(column => new AnalyticsGridColumn
            {
                Id = column.Id,
                Label = string.IsNullOrWhiteSpace(column.Label) ? column.Field : column.Label,
                UrlTemplate = string.IsNullOrWhiteSpace(column.UrlTemplate) ? null : column.UrlTemplate.Trim()
            })
            .ToList();

        return (columns, rows, total);
    }

    private async Task<long> CountGridAsync(
        SqlConnection connection,
        AnalyticsQueryBuilder.Context context,
        Dictionary<string, List<string>> selections,
        CancellationToken cancellationToken)
    {
        var bag = new AnalyticsQueryBuilder.ParameterBag();
        var built = new AnalyticsQueryBuilder.BuiltAnalyticsQuery
        {
            Sql = AnalyticsQueryBuilder.GridCountSql(context, selections, bag),
            Parameters = bag.Parameters
        };

        await using var command = CreateCommand(connection, built, "grid count");
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<AnalyticsChartResult> ReadChartAsync(
        SqlDataReader reader,
        AnalyticsQueryBuilder.Context context,
        AnalyticsChart chart,
        CancellationToken cancellationToken)
    {
        var points = new List<AnalyticsChartPoint>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = reader.IsDBNull(0)
                ? AnalyticsNullValue.Sentinel
                : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? AnalyticsNullValue.Sentinel;
            var metric = reader.IsDBNull(1)
                ? 0d
                : Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
            points.Add(new AnalyticsChartPoint { Value = value, Metric = metric });
        }

        var title = string.IsNullOrWhiteSpace(chart.Title)
            ? (context.Config.Dimensions.FirstOrDefault(item =>
                item.Id.Equals(chart.DimensionId, StringComparison.OrdinalIgnoreCase))?.Label ?? chart.Id)
            : chart.Title;
        return new AnalyticsChartResult
        {
            ChartId = chart.Id,
            Title = title,
            Type = NormalizeChartType(chart.Type),
            DimensionId = chart.DimensionId,
            MeasureId = chart.MeasureId,
            State = chart.State.Equals("compare", StringComparison.OrdinalIgnoreCase) ? "compare" : "default",
            Points = points
        };
    }

    private async Task<IReadOnlyList<AnalyticsMapPoint>> ReadMapAsync(
        SqlConnection connection,
        AnalyticsQueryBuilder.Context context,
        Dictionary<string, List<string>> selections,
        CancellationToken cancellationToken)
    {
        var map = context.Config.Map;
        if (map is null || !map.Enabled)
        {
            return [];
        }

        var built = AnalyticsQueryBuilder.BuildMap(context, map, selections);
        await using var command = CreateCommand(connection, built, "map");
        var points = new List<AnalyticsMapPoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                continue;
            }

            var lat = Convert.ToDouble(reader.GetValue(0), CultureInfo.InvariantCulture);
            var lng = Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
            if (double.IsNaN(lat) || double.IsNaN(lng) || Math.Abs(lat) > 90 || Math.Abs(lng) > 180)
            {
                continue;
            }

            points.Add(new AnalyticsMapPoint
            {
                Lat = lat,
                Lng = lng,
                Label = reader.IsDBNull(2)
                    ? string.Empty
                    : Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty
            });
        }

        return points;
    }

    internal static string NormalizeChartType(string? type)
    {
        var value = type?.Trim().ToLowerInvariant();
        return value is "line" or "pie" ? value : "bar";
    }

    private SqlCommand CreateCommand(
        SqlConnection connection,
        AnalyticsQueryBuilder.BuiltAnalyticsQuery built,
        string name)
    {
        SqlLog.Value?.Record(name, built);
        var command = connection.CreateCommand();
        command.CommandText = built.Sql;
        command.CommandTimeout = _commandTimeoutSeconds;
        foreach (var parameter in built.Parameters)
        {
            command.Parameters.Add(BuildParameter(parameter));
        }

        return command;
    }

    /// <summary>
    /// Binds an explicitly typed, fixed-size parameter. Letting SqlClient infer the type sizes a string to
    /// its own length — so every distinct value length compiles its own plan — and binds text as nvarchar,
    /// which forces an implicit conversion on a varchar or integer column and turns a seek into a scan.
    /// </summary>
    private static SqlParameter BuildParameter(QueryParameter parameter)
    {
        var value = parameter.Value ?? DBNull.Value;
        var sqlParameter = new SqlParameter(parameter.Name, value);
        switch (AnalyticsQueryBuilder.BaseSqlType(parameter.SqlType))
        {
            case "int" or "smallint" or "tinyint":
                sqlParameter.SqlDbType = SqlDbType.Int;
                break;
            case "bigint":
                sqlParameter.SqlDbType = SqlDbType.BigInt;
                break;
            case "bit":
                sqlParameter.SqlDbType = SqlDbType.Bit;
                break;
            case "uniqueidentifier":
                sqlParameter.SqlDbType = SqlDbType.UniqueIdentifier;
                break;
            case "varchar" or "char":
                sqlParameter.SqlDbType = SqlDbType.VarChar;
                sqlParameter.Size = 8000;
                break;
            case "nvarchar" or "nchar" or "sysname":
                sqlParameter.SqlDbType = SqlDbType.NVarChar;
                sqlParameter.Size = 4000;
                break;
            default:
                if (value is string)
                {
                    sqlParameter.SqlDbType = SqlDbType.NVarChar;
                    sqlParameter.Size = 4000;
                }

                break;
        }

        return sqlParameter;
    }

    internal static string FormatMeasure(object? value, string? format)
    {
        if (value is null)
        {
            return "—";
        }

        if (value is IFormattable formattable)
        {
            var preset = format?.Trim();
            if (!string.IsNullOrWhiteSpace(preset))
            {
                if (preset.Equals("aed", StringComparison.OrdinalIgnoreCase)
                    && value is double or float or decimal or int or long or short or byte)
                {
                    var amount = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    return "AED " + amount.ToString("N2", CultureInfo.InvariantCulture);
                }

                try
                {
                    return formattable.ToString(preset, CultureInfo.InvariantCulture) ?? "—";
                }
                catch (FormatException)
                {
                    // fall through
                }
            }

            if (value is double or float or decimal or int or long or short or byte)
            {
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return number.ToString("N0", CultureInfo.InvariantCulture);
            }

            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? "—";
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "—";
    }
}
