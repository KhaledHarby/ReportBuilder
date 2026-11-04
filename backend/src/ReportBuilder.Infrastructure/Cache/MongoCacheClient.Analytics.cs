using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Services;

namespace ReportBuilder.Infrastructure.Cache;

/// <summary>
/// Serves analytics dashboards from the MongoDB dataset cache. A single-source dashboard without
/// SQL expressions maps directly onto the cached documents, so a click never touches the live SQL view.
///
/// Every pane is its own aggregation and they run concurrently: MongoDB executes one aggregation on one
/// thread, so a single <c>$facet</c> would push every document through every sub-pipeline serially,
/// while independent commands spread across cores. Each pipeline starts with <c>$match</c> on the
/// active selections, which the per-column cache indexes serve, so a filtered click scans the matching
/// documents rather than the whole collection. Only the unfiltered state has to read everything, and
/// that state is shared by every viewer and cached for the life of the snapshot.
/// </summary>
public sealed partial class MongoCacheClient
{
    public const string AnalyticsSourceName = AnalyticsDataSources.MongoCache;

    /// <summary>
    /// A dashboard can be served from the document cache when it reads one dataset with plain
    /// column references. Joins and SQL expressions stay on SQL Server.
    /// </summary>
    public static bool SupportsAnalytics(AnalyticsDashboardConfig config)
    {
        if (config.Sources.Count != 1 || config.Links.Count > 0)
        {
            return false;
        }

        if (config.Dimensions.Any(item => !string.IsNullOrWhiteSpace(item.Expression) || string.IsNullOrWhiteSpace(item.Field)))
        {
            return false;
        }

        foreach (var measure in config.Measures)
        {
            if (!string.IsNullOrWhiteSpace(measure.Expression))
            {
                return false;
            }

            var aggregation = measure.Aggregation.Trim().ToLowerInvariant();
            if (aggregation is "expr" or "expression" or "calc")
            {
                return false;
            }

            if (aggregation is not ("count" or "sum" or "avg" or "min" or "max"))
            {
                return false;
            }
        }

        if (config.DetailColumns.Any(column => string.IsNullOrWhiteSpace(column.Field)))
        {
            return false;
        }

        return true;
    }

    public async Task<AnalyticsSnapshotResult> QueryAnalyticsSnapshotAsync(
        CacheProviderConnection connection,
        string collectionName,
        Dataset dataset,
        AnalyticsDashboardConfig config,
        Dictionary<string, List<string>> selections,
        TimeSpan maxTime,
        Action<string, string>? log,
        AnalyticsChart? drillChart,
        CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var (collection, shape) = await OpenAsync(connection, collectionName, cancellationToken);
        var plan = new AnalyticsDocumentPlan(shape, dataset, config, selections, maxTime, log);

        var dimensionTasks = config.Dimensions
            .Select(dimension => ReadDimensionValuesAsync(collection, plan, dimension, regexPattern: null, AnalyticsQueryBuilder.MaxDimensionValues, cancellationToken))
            .ToList();

        var distinctMeasures = new Dictionary<int, Task<long>>();
        var simpleMeasures = new List<(int Index, AnalyticsMeasure Measure)>();
        for (var index = 0; index < config.Measures.Count; index++)
        {
            var measure = config.Measures[index];
            if (plan.IsDistinctCount(measure))
            {
                distinctMeasures[index] = ReadDistinctCountAsync(collection, plan, measure, cancellationToken);
            }
            else
            {
                simpleMeasures.Add((index, measure));
            }
        }

        var simpleTask = simpleMeasures.Count > 0
            ? ReadSimpleMeasuresAsync(collection, plan, simpleMeasures, cancellationToken)
            : Task.FromResult<BsonDocument?>(null);

        var chartTasks = config.Charts
            .Select(chart => ReadChartAsync(collection, plan, chart, cancellationToken))
            .ToList();
        var drillTask = drillChart is null
            ? null
            : ReadChartAsync(collection, plan, drillChart, cancellationToken);

        var pending = dimensionTasks
            .Cast<Task>()
            .Concat(distinctMeasures.Values)
            .Append(simpleTask)
            .Concat(chartTasks);
        if (drillTask is not null)
        {
            pending = pending.Append(drillTask);
        }

        await Task.WhenAll(pending);

        var dimensions = new List<AnalyticsDimensionResult>(config.Dimensions.Count);
        for (var index = 0; index < config.Dimensions.Count; index++)
        {
            dimensions.Add(ToDimensionResult(config.Dimensions[index], selections, dimensionTasks[index].Result));
        }

        var simpleRow = simpleTask.Result;
        var measures = new List<AnalyticsMeasureResult>(config.Measures.Count);
        for (var index = 0; index < config.Measures.Count; index++)
        {
            var measure = config.Measures[index];
            object? raw;
            if (distinctMeasures.TryGetValue(index, out var distinctTask))
            {
                raw = distinctTask.Result;
            }
            else if (simpleRow is null)
            {
                raw = plan.IsCount(measure) ? 0L : null;
            }
            else
            {
                raw = simpleRow.TryGetValue("m" + index, out var cell) ? FromBson(cell) : null;
            }

            measures.Add(new AnalyticsMeasureResult
            {
                MeasureId = measure.Id,
                Label = measure.Label,
                NumericValue = ToDouble(raw),
                FormattedValue = AnalyticsQueryService.FormatMeasure(raw, measure.Format)
            });
        }

        var charts = new List<AnalyticsChartResult>(config.Charts.Count);
        for (var index = 0; index < config.Charts.Count; index++)
        {
            var chart = config.Charts[index];
            var title = string.IsNullOrWhiteSpace(chart.Title)
                ? (config.Dimensions.FirstOrDefault(item =>
                    item.Id.Equals(chart.DimensionId, StringComparison.OrdinalIgnoreCase))?.Label ?? chart.Id)
                : chart.Title;
            charts.Add(new AnalyticsChartResult
            {
                ChartId = chart.Id,
                Title = title,
                Type = AnalyticsQueryService.NormalizeChartType(chart.Type),
                DimensionId = chart.DimensionId,
                MeasureId = chart.MeasureId,
                State = chart.State.Equals("compare", StringComparison.OrdinalIgnoreCase) ? "compare" : "default",
                Points = chartTasks[index].Result
            });
        }

        if (drillChart is not null && drillTask is not null)
        {
            charts.Add(new AnalyticsChartResult
            {
                ChartId = drillChart.Id,
                Title = string.IsNullOrWhiteSpace(drillChart.Title)
                    ? drillChart.DimensionId
                    : drillChart.Title,
                Type = AnalyticsQueryService.NormalizeChartType(drillChart.Type),
                DimensionId = drillChart.DimensionId,
                MeasureId = drillChart.MeasureId,
                State = "default",
                Points = drillTask.Result
            });
        }

        return new AnalyticsSnapshotResult
        {
            Dimensions = dimensions,
            Measures = measures,
            Charts = charts,
            Source = AnalyticsSourceName,
            ElapsedMs = started.ElapsedMilliseconds
        };
    }

    public async Task<(
        IReadOnlyList<AnalyticsGridColumn> Columns,
        IReadOnlyList<Dictionary<string, object?>> Rows,
        long Total)> QueryAnalyticsGridAsync(
        CacheProviderConnection connection,
        string collectionName,
        Dataset dataset,
        AnalyticsDashboardConfig config,
        Dictionary<string, List<string>> selections,
        int skip,
        int take,
        TimeSpan maxTime,
        Action<string, string>? log,
        CancellationToken cancellationToken)
    {
        var detailColumns = config.DetailColumns.Count > 0
            ? config.DetailColumns
            : config.Dimensions
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

        var (collection, shape) = await OpenAsync(connection, collectionName, cancellationToken);
        var plan = new AnalyticsDocumentPlan(shape, dataset, config, selections, maxTime, log);
        var filter = plan.MatchFilter(excludeDimensionId: null);

        var projection = Builders<BsonDocument>.Projection.Include("_id");
        foreach (var column in detailColumns)
        {
            projection = projection.Include(shape.Path(column.Field));
        }

        var find = collection.Find(filter, new FindOptions { MaxTime = maxTime })
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
            .Skip(skip)
            .Limit(take)
            .Project(projection);
        log?.Invoke("mongo grid", RenderFilter(collection, filter).ToJson() + $" skip {skip} limit {take}");

        // The page and the total are independent, so they run side by side.
        var pageTask = find.ToListAsync(cancellationToken);
        var totalTask = plan.HasSelections
            ? collection.CountDocumentsAsync(filter, new CountOptions { MaxTime = maxTime }, cancellationToken)
            : EstimateRowsAsync(collection, cancellationToken);
        await Task.WhenAll(pageTask, totalTask);

        var rows = new List<Dictionary<string, object?>>(pageTask.Result.Count);
        foreach (var document in pageTask.Result)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in detailColumns)
            {
                var raw = shape.ReadCell(document, column.Field, FromBson);
                var text = AnalyticsQueryBuilder.FormatCell(raw);
                row[column.Id] = text == AnalyticsNullValue.Sentinel ? null : text;
            }

            rows.Add(row);
        }

        var columns = detailColumns
            .Select(column => new AnalyticsGridColumn
            {
                Id = column.Id,
                Label = string.IsNullOrWhiteSpace(column.Label) ? column.Field : column.Label,
                UrlTemplate = string.IsNullOrWhiteSpace(column.UrlTemplate) ? null : column.UrlTemplate.Trim()
            })
            .ToList();
        return (columns, rows, totalTask.Result);
    }

    private static async Task<long> EstimateRowsAsync(IMongoCollection<BsonDocument> collection, CancellationToken cancellationToken)
    {
        var estimated = await collection.EstimatedDocumentCountAsync(cancellationToken: cancellationToken);
        return Math.Max(0, estimated - 1);
    }

    public async Task<IReadOnlyList<AnalyticsMapPoint>> QueryAnalyticsMapAsync(
        CacheProviderConnection connection,
        string collectionName,
        Dataset dataset,
        AnalyticsDashboardConfig config,
        Dictionary<string, List<string>> selections,
        TimeSpan maxTime,
        Action<string, string>? log,
        CancellationToken cancellationToken)
    {
        var map = config.Map;
        if (map is null || !map.Enabled)
        {
            return [];
        }

        var (collection, shape) = await OpenAsync(connection, collectionName, cancellationToken);
        var plan = new AnalyticsDocumentPlan(shape, dataset, config, selections, maxTime, log);
        var latPath = shape.Path(map.LatField);
        var lngPath = shape.Path(map.LngField);
        var filter = Builders<BsonDocument>.Filter.And(
            plan.MatchFilter(excludeDimensionId: null),
            Builders<BsonDocument>.Filter.Ne(latPath, BsonNull.Value),
            Builders<BsonDocument>.Filter.Ne(lngPath, BsonNull.Value));
        log?.Invoke("mongo map", RenderFilter(collection, filter).ToJson());

        var projection = Builders<BsonDocument>.Projection.Include(latPath).Include(lngPath);
        if (!string.IsNullOrWhiteSpace(map.LabelField))
        {
            var labelPath = shape.Path(map.LabelField);
            if (labelPath != latPath && labelPath != lngPath)
            {
                projection = projection.Include(labelPath);
            }
        }

        var docs = await collection.Find(filter, new FindOptions { MaxTime = maxTime })
            .Limit(AnalyticsQueryBuilder.MaxMapPoints)
            .Project(projection)
            .ToListAsync(cancellationToken);

        var points = new List<AnalyticsMapPoint>(docs.Count);
        foreach (var document in docs)
        {
            var lat = ToDouble(shape.ReadCell(document, map.LatField, FromBson));
            var lng = ToDouble(shape.ReadCell(document, map.LngField, FromBson));
            if (lat is null || lng is null || double.IsNaN(lat.Value) || double.IsNaN(lng.Value)
                || Math.Abs(lat.Value) > 90 || Math.Abs(lng.Value) > 180)
            {
                continue;
            }

            var labelRaw = string.IsNullOrWhiteSpace(map.LabelField)
                ? lat
                : shape.ReadCell(document, map.LabelField, FromBson);
            var label = AnalyticsQueryBuilder.FormatCell(labelRaw);
            points.Add(new AnalyticsMapPoint
            {
                Lat = lat.Value,
                Lng = lng.Value,
                Label = label == AnalyticsNullValue.Sentinel ? string.Empty : label
            });
        }

        return points;
    }

    public async Task<List<AnalyticsSearchHit>> QueryAnalyticsSearchAsync(
        CacheProviderConnection connection,
        string collectionName,
        Dataset dataset,
        AnalyticsDashboardConfig config,
        Dictionary<string, List<string>> selections,
        IReadOnlyList<AnalyticsDimension> dimensions,
        string query,
        int takePerDimension,
        TimeSpan maxTime,
        Action<string, string>? log,
        CancellationToken cancellationToken)
    {
        if (dimensions.Count == 0)
        {
            return [];
        }

        var (collection, shape) = await OpenAsync(connection, collectionName, cancellationToken);
        var plan = new AnalyticsDocumentPlan(shape, dataset, config, selections, maxTime, log);
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pattern = string.Join(".*", terms.Select(Regex.Escape));

        var tasks = dimensions
            .Select(dimension => ReadDimensionValuesAsync(collection, plan, dimension, pattern, takePerDimension, cancellationToken))
            .ToList();
        await Task.WhenAll(tasks);

        var hits = new List<AnalyticsSearchHit>();
        for (var index = 0; index < dimensions.Count; index++)
        {
            var dimension = dimensions[index];
            var selected = selections.TryGetValue(dimension.Id, out var values) ? values : [];
            var label = string.IsNullOrWhiteSpace(dimension.Label) ? dimension.Field : dimension.Label;
            foreach (var (value, count) in tasks[index].Result)
            {
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

        return hits;
    }

    /// <summary>
    /// One filter list. Counts come from a <c>$match</c> on the other dimensions' selections followed by
    /// a <c>$group</c>, so only matching documents are read. Values that the selection excludes still
    /// have to appear (greyed out, count 0), so they are filled in from an index-backed distinct scan
    /// when the counted list leaves room. Order matches the SQL path: matches first by count, then value.
    /// </summary>
    private async Task<List<(string Value, long Count)>> ReadDimensionValuesAsync(
        IMongoCollection<BsonDocument> collection,
        AnalyticsDocumentPlan plan,
        AnalyticsDimension dimension,
        string? regexPattern,
        int limit,
        CancellationToken cancellationToken)
    {
        var path = plan.Path(dimension.Field);
        var searchFilter = regexPattern is null ? null : RegexFilter(path, regexPattern);
        var filter = plan.MatchFilter(dimension.Id);
        if (searchFilter is not null)
        {
            filter = Builders<BsonDocument>.Filter.And(filter, searchFilter);
        }

        var pipeline = new List<BsonDocument>
        {
            new("$match", RenderFilter(collection, filter)),
            new("$group", new BsonDocument
            {
                { "_id", "$" + path },
                { "cnt", new BsonDocument("$sum", 1) }
            }),
            new("$sort", new BsonDocument { { "cnt", -1 }, { "_id", 1 } }),
            new("$limit", limit)
        };

        var rows = await plan.AggregateAsync(collection, "mongo dimension " + dimension.Field, pipeline, cancellationToken);
        var result = rows
            .Select(row => (Value: FormatValue(row.GetValue("_id", BsonNull.Value)), Count: row.GetValue("cnt", 0).ToInt64()))
            .ToList();

        if (!plan.HasOtherSelections(dimension.Id) || result.Count >= limit)
        {
            return result;
        }

        // Fill with excluded values so the user can still click them. Distinct is served by the column
        // index when one exists; a result too large for one reply simply means no fill for that list.
        var seen = new HashSet<string>(result.Select(item => item.Value), StringComparer.Ordinal);
        try
        {
            var baseFilter = Builders<BsonDocument>.Filter.Ne("_id", MetaId);
            if (searchFilter is not null)
            {
                baseFilter = Builders<BsonDocument>.Filter.And(baseFilter, searchFilter);
            }

            var distinct = await collection
                .DistinctAsync<BsonValue>(path, baseFilter, new DistinctOptions { MaxTime = plan.MaxTime }, cancellationToken);
            var excluded = new List<string>();
            while (await distinct.MoveNextAsync(cancellationToken))
            {
                foreach (var value in distinct.Current)
                {
                    var text = FormatValue(value);
                    if (seen.Add(text))
                    {
                        excluded.Add(text);
                    }
                }
            }

            excluded.Sort(StringComparer.OrdinalIgnoreCase);
            result.AddRange(excluded.Take(limit - result.Count).Select(value => (value, 0L)));
        }
        catch (MongoException ex)
        {
            _logger.LogInformation(ex, "Could not list excluded values for {Field}; showing matching values only.", dimension.Field);
        }

        return result;
    }

    private static async Task<long> ReadDistinctCountAsync(
        IMongoCollection<BsonDocument> collection,
        AnalyticsDocumentPlan plan,
        AnalyticsMeasure measure,
        CancellationToken cancellationToken)
    {
        var path = plan.Path(measure.Field!);
        var filter = Builders<BsonDocument>.Filter.And(
            plan.MatchFilter(excludeDimensionId: null),
            Builders<BsonDocument>.Filter.Ne(path, BsonNull.Value));
        var pipeline = new List<BsonDocument>
        {
            new("$match", RenderFilter(collection, filter)),
            new("$group", new BsonDocument("_id", "$" + path)),
            new("$count", "n")
        };

        var rows = await plan.AggregateAsync(collection, "mongo distinct " + measure.Field, pipeline, cancellationToken);
        return rows.Count == 0 ? 0L : rows[0].GetValue("n", 0).ToInt64();
    }

    private static async Task<BsonDocument?> ReadSimpleMeasuresAsync(
        IMongoCollection<BsonDocument> collection,
        AnalyticsDocumentPlan plan,
        IReadOnlyList<(int Index, AnalyticsMeasure Measure)> measures,
        CancellationToken cancellationToken)
    {
        var group = new BsonDocument("_id", BsonNull.Value);
        foreach (var (index, measure) in measures)
        {
            group["m" + index] = plan.Accumulator(measure);
        }

        var pipeline = new List<BsonDocument>
        {
            new("$match", RenderFilter(collection, plan.MatchFilter(excludeDimensionId: null))),
            new("$group", group)
        };

        var rows = await plan.AggregateAsync(collection, "mongo measures", pipeline, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    private static async Task<List<AnalyticsChartPoint>> ReadChartAsync(
        IMongoCollection<BsonDocument> collection,
        AnalyticsDocumentPlan plan,
        AnalyticsChart chart,
        CancellationToken cancellationToken)
    {
        var dimension = plan.Config.Dimensions.FirstOrDefault(item =>
                item.Id.Equals(chart.DimensionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ReportQueryException($"Chart '{chart.Title}' references an unknown dimension.");
        var measure = plan.Config.Measures.FirstOrDefault(item =>
                item.Id.Equals(chart.MeasureId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ReportQueryException($"Chart '{chart.Title}' references an unknown measure.");

        var dimPath = "$" + plan.Path(dimension.Field);
        var filter = plan.MatchFilter(dimension.Id);
        var pipeline = new List<BsonDocument>();

        if (plan.IsDistinctCount(measure))
        {
            var measurePath = plan.Path(measure.Field!);
            filter = Builders<BsonDocument>.Filter.And(filter, Builders<BsonDocument>.Filter.Ne(measurePath, BsonNull.Value));
            pipeline.Add(new BsonDocument("$match", RenderFilter(collection, filter)));
            pipeline.Add(new BsonDocument("$group", new BsonDocument(
                "_id",
                new BsonDocument { { "d", dimPath }, { "f", "$" + measurePath } })));
            pipeline.Add(new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$_id.d" },
                { "metric", new BsonDocument("$sum", 1) }
            }));
        }
        else
        {
            pipeline.Add(new BsonDocument("$match", RenderFilter(collection, filter)));
            pipeline.Add(new BsonDocument("$group", new BsonDocument
            {
                { "_id", dimPath },
                { "metric", plan.Accumulator(measure) }
            }));
        }

        pipeline.Add(new BsonDocument("$sort", new BsonDocument { { "metric", -1 }, { "_id", 1 } }));
        pipeline.Add(new BsonDocument("$limit", AnalyticsQueryBuilder.MaxChartValues));

        var rows = await plan.AggregateAsync(collection, "mongo chart " + chart.Id, pipeline, cancellationToken);
        return rows
            .Select(row => new AnalyticsChartPoint
            {
                Value = FormatValue(row.GetValue("_id", BsonNull.Value)),
                Metric = ToDouble(FromBson(row.GetValue("metric", BsonNull.Value))) ?? 0d
            })
            .ToList();
    }

    private static AnalyticsDimensionResult ToDimensionResult(
        AnalyticsDimension dimension,
        Dictionary<string, List<string>> selections,
        IReadOnlyList<(string Value, long Count)> rows)
    {
        var selected = selections.TryGetValue(dimension.Id, out var values) ? values : [];
        return new AnalyticsDimensionResult
        {
            DimensionId = dimension.Id,
            Label = string.IsNullOrWhiteSpace(dimension.Label) ? dimension.Field : dimension.Label,
            SelectedValues = selected,
            Values = rows
                .Select(row => new AnalyticsDimensionValue
                {
                    Value = row.Value,
                    Count = row.Count,
                    State = AnalyticsQueryBuilder.ResolveValueState(row.Value, row.Count, selected)
                })
                .ToList()
        };
    }

    private static FilterDefinition<BsonDocument> RegexFilter(string path, string pattern)
        => new BsonDocumentFilterDefinition<BsonDocument>(new BsonDocument(
            "$expr",
            new BsonDocument(
                "$regexMatch",
                new BsonDocument
                {
                    { "input", new BsonDocument("$toString", new BsonDocument("$ifNull", new BsonArray { "$" + path, "" })) },
                    { "regex", pattern },
                    { "options", "i" }
                })));

    private static string FormatValue(BsonValue value)
        => AnalyticsQueryBuilder.FormatCell(FromBson(value));

    private static double? ToDouble(object? raw)
    {
        if (raw is null)
        {
            return null;
        }

        if (raw is IConvertible convertible && raw is not string)
        {
            try
            {
                return convertible.ToDouble(CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                return null;
            }
        }

        if (raw is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>
    /// Resolves selections to typed document values once per request and runs the pipelines. Each
    /// dimension's selection becomes a query filter so <c>$match</c> and <c>find</c> can use the
    /// column indexes the cache refresh created.
    /// </summary>
    private sealed class AnalyticsDocumentPlan
    {
        private readonly CacheShape _shape;
        private readonly Dictionary<string, DatasetColumn> _columns;
        private readonly List<Selection> _selections = [];
        private readonly Action<string, string>? _log;

        public AnalyticsDocumentPlan(
            CacheShape shape,
            Dataset dataset,
            AnalyticsDashboardConfig config,
            Dictionary<string, List<string>> selections,
            TimeSpan maxTime,
            Action<string, string>? log)
        {
            _shape = shape;
            _log = log;
            Config = config;
            MaxTime = maxTime;
            _columns = dataset.Columns
                .Where(column => !column.IsHidden)
                .GroupBy(column => column.ColumnName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var dimension in config.Dimensions)
            {
                if (!selections.TryGetValue(dimension.Id, out var values) || values.Count == 0)
                {
                    continue;
                }

                var column = RequireColumn(dimension.Field);
                var path = shape.Path(column.ColumnName);
                var hasNull = false;
                var typed = new List<BsonValue>();
                foreach (var raw in values.Select(value => value?.Trim() ?? string.Empty).Where(value => value.Length > 0))
                {
                    if (raw.Equals(AnalyticsNullValue.Sentinel, StringComparison.Ordinal))
                    {
                        hasNull = true;
                        continue;
                    }

                    typed.AddRange(ConvertSelection(column, raw));
                }

                if (typed.Count == 0 && !hasNull)
                {
                    continue;
                }

                _selections.Add(new Selection(dimension.Id, path, typed, hasNull));
            }
        }

        public AnalyticsDashboardConfig Config { get; }

        public TimeSpan MaxTime { get; }

        public bool HasSelections => _selections.Count > 0;

        public bool HasOtherSelections(string dimensionId)
            => _selections.Any(selection => !selection.DimensionId.Equals(dimensionId, StringComparison.OrdinalIgnoreCase));

        public string Path(string field) => _shape.Path(RequireColumn(field).ColumnName);

        public bool IsCount(AnalyticsMeasure measure)
            => measure.Aggregation.Trim().Equals("count", StringComparison.OrdinalIgnoreCase);

        public bool IsDistinctCount(AnalyticsMeasure measure)
            => IsCount(measure) && !string.IsNullOrWhiteSpace(measure.Field);

        public async Task<List<BsonDocument>> AggregateAsync(
            IMongoCollection<BsonDocument> collection,
            string name,
            List<BsonDocument> pipeline,
            CancellationToken cancellationToken)
        {
            _log?.Invoke(name, new BsonArray(pipeline).ToJson());
            return await collection.Aggregate<BsonDocument>(pipeline, new AggregateOptions
            {
                AllowDiskUse = true,
                MaxTime = MaxTime
            }).ToListAsync(cancellationToken);
        }

        public BsonDocument Accumulator(AnalyticsMeasure measure)
        {
            var aggregation = measure.Aggregation.Trim().ToLowerInvariant();
            if (aggregation == "count")
            {
                return new BsonDocument("$sum", 1);
            }

            if (string.IsNullOrWhiteSpace(measure.Field))
            {
                throw new ReportQueryException($"Measure '{measure.Label}' needs a field.");
            }

            var path = "$" + Path(measure.Field);
            return aggregation switch
            {
                "sum" => new BsonDocument("$sum", path),
                "avg" => new BsonDocument("$avg", path),
                "min" => new BsonDocument("$min", path),
                "max" => new BsonDocument("$max", path),
                _ => throw new ReportQueryException($"Unsupported aggregation '{measure.Aggregation}'.")
            };
        }

        /// <summary>Query-filter form of the active selections, always excluding the metadata document.</summary>
        public FilterDefinition<BsonDocument> MatchFilter(string? excludeDimensionId)
        {
            var parts = new List<FilterDefinition<BsonDocument>>
            {
                Builders<BsonDocument>.Filter.Ne("_id", MetaId)
            };

            foreach (var selection in _selections)
            {
                if (excludeDimensionId is not null
                    && selection.DimensionId.Equals(excludeDimensionId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var alternatives = new List<FilterDefinition<BsonDocument>>();
                if (selection.Values.Count > 0)
                {
                    alternatives.Add(Builders<BsonDocument>.Filter.In(selection.Path, selection.Values));
                }

                if (selection.IncludesNull)
                {
                    alternatives.Add(Builders<BsonDocument>.Filter.Eq(selection.Path, BsonNull.Value));
                }

                parts.Add(alternatives.Count == 1 ? alternatives[0] : Builders<BsonDocument>.Filter.Or(alternatives));
            }

            return parts.Count == 1 ? parts[0] : Builders<BsonDocument>.Filter.And(parts);
        }

        private DatasetColumn RequireColumn(string field)
        {
            if (string.IsNullOrWhiteSpace(field) || !_columns.TryGetValue(field, out var column))
            {
                throw new ReportQueryException($"Column '{field}' was not found on the dataset.");
            }

            return column;
        }

        /// <summary>
        /// A selection arrives as the display text the list showed. Convert it back to the column's
        /// stored type, keeping the text form as well so a value cached as text still matches; when the
        /// text is not valid for the type (a value typed by hand), compare it as text only.
        /// </summary>
        private static IEnumerable<BsonValue> ConvertSelection(DatasetColumn column, string raw)
        {
            try
            {
                return FilterValues(column, raw);
            }
            catch (ReportQueryException)
            {
                return [raw];
            }
        }

        private sealed record Selection(string DimensionId, string Path, List<BsonValue> Values, bool IncludesNull);
    }
}
