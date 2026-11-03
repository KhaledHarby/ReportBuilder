using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Core.Query;

public static partial class AnalyticsQueryBuilder
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;
    public const int MaxDimensionValues = 500;
    public const int MaxChartValues = 30;
    public const int MaxMapPoints = 300;
    public const int MaxSearchHits = 24;
    public const int MaxSearchHitsPerDimension = 8;
    public const int MinSearchLength = 2;

    /// <summary>Type used when a value is compared as text via <c>CONVERT(nvarchar(4000), …)</c>.</summary>
    public const string TextParameterType = "nvarchar(4000)";

    private static readonly Regex SafeAlias = AliasRegex();

    public sealed class BuiltAnalyticsQuery
    {
        public required string Sql { get; init; }
        public required List<QueryParameter> Parameters { get; init; }
    }

    /// <summary>
    /// Allocates parameter names across every statement that shares one command. Values are deduplicated
    /// by (value, type) so a selection reused by several statements binds once, and names stay unique so
    /// statements can be concatenated into a single batch.
    /// </summary>
    public sealed class ParameterBag
    {
        private readonly List<QueryParameter> _parameters = [];
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

        public List<QueryParameter> Parameters => _parameters;

        public string Add(object value, string? sqlType)
        {
            var key = (sqlType ?? string.Empty) + "\n" + Convert.ToString(value, CultureInfo.InvariantCulture);
            if (_names.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var name = "@p" + _parameters.Count;
            _parameters.Add(new QueryParameter(name, value, sqlType));
            _names[key] = name;
            return name;
        }
    }

    public sealed class Context
    {
        public required AnalyticsDashboardConfig Config { get; init; }
        public required IReadOnlyDictionary<string, Dataset> DatasetsBySourceId { get; init; }
        public required IReadOnlyDictionary<string, string> AliasBySourceId { get; init; }
        public required string FromSql { get; init; }
        public IReadOnlyDictionary<string, string>? ColumnMap { get; init; }
        public string? MaterializedGrain { get; init; }
    }

    /// <summary>
    /// Copies the join into a temp table once per batch. Only columns the snapshot actually reads are
    /// copied: a dashboard with eight filters over two wide sources used to write every column of both
    /// sources into tempdb on every click, when the filters, measures, and charts touch a handful.
    /// Pass <paramref name="referencedOnly"/> as <c>false</c> to copy every visible column.
    /// </summary>
    public static (string Sql, Context Materialized) BuildMaterialize(Context context, bool referencedOnly = true)
    {
        const string table = "#rb_analytics";
        const string grainColumn = "[__rb_grain]";
        var selectParts = new List<string>();
        var columnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedPhysical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referenced = referencedOnly ? CollectReferencedColumns(context) : null;

        foreach (var source in context.Config.Sources)
        {
            var alias = context.AliasBySourceId[source.Id];
            var dataset = context.DatasetsBySourceId[source.Id];
            foreach (var column in dataset.Columns.Where(item => !item.IsHidden))
            {
                if (referenced is not null && !referenced.Contains(MapKey(alias, column.ColumnName)))
                {
                    continue;
                }

                var physical = UniquePhysicalName(alias, column.ColumnName, usedPhysical);
                columnMap[MapKey(alias, column.ColumnName)] = SqlIdentifiers.Quote(physical);
                selectParts.Add(
                    $"{alias}.{SqlIdentifiers.Quote(column.ColumnName)} AS {SqlIdentifiers.Quote(physical)}");
            }
        }

        selectParts.Add($"{BuildDenseRankGrain(context)} AS {grainColumn}");

        var sql = $"""
            IF OBJECT_ID('tempdb..{table}') IS NOT NULL DROP TABLE {table};
            SELECT {string.Join(", ", selectParts)} INTO {table} FROM {context.FromSql}
            """;
        return (sql, new Context
        {
            Config = context.Config,
            DatasetsBySourceId = context.DatasetsBySourceId,
            AliasBySourceId = context.AliasBySourceId,
            FromSql = table,
            ColumnMap = columnMap,
            MaterializedGrain = grainColumn
        });
    }

    /// <summary>
    /// Columns the snapshot (filters, measures, charts) reads, keyed like <see cref="Context.ColumnMap"/>.
    /// An expression contributes every column whose compiled reference appears in its SQL; if an
    /// expression cannot be compiled here, its whole source is included so the real query reports the error.
    /// </summary>
    private static HashSet<string> CollectReferencedColumns(Context context)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddField(string sourceId, string? field)
        {
            if (string.IsNullOrWhiteSpace(field)
                || !context.AliasBySourceId.TryGetValue(sourceId, out var alias)
                || !context.DatasetsBySourceId.TryGetValue(sourceId, out var dataset))
            {
                return;
            }

            var column = dataset.Columns.FirstOrDefault(item =>
                item.ColumnName.Equals(field, StringComparison.OrdinalIgnoreCase) && !item.IsHidden);
            if (column is not null)
            {
                referenced.Add(MapKey(alias, column.ColumnName));
            }
        }

        void AddExpression(string sourceId, string? expression, bool allowAggregates)
        {
            if (string.IsNullOrWhiteSpace(expression)
                || !context.AliasBySourceId.TryGetValue(sourceId, out var alias)
                || !context.DatasetsBySourceId.TryGetValue(sourceId, out var dataset))
            {
                return;
            }

            string compiled;
            try
            {
                compiled = AnalyticsExpression.Compile(expression, alias, dataset, allowAggregates);
            }
            catch (ReportQueryException)
            {
                foreach (var column in dataset.Columns.Where(item => !item.IsHidden))
                {
                    referenced.Add(MapKey(alias, column.ColumnName));
                }

                return;
            }

            foreach (var column in dataset.Columns.Where(item => !item.IsHidden))
            {
                if (compiled.Contains($"{alias}.{SqlIdentifiers.Quote(column.ColumnName)}", StringComparison.OrdinalIgnoreCase))
                {
                    referenced.Add(MapKey(alias, column.ColumnName));
                }
            }
        }

        foreach (var dimension in context.Config.Dimensions)
        {
            AddField(dimension.SourceId, dimension.Field);
            AddExpression(dimension.SourceId, dimension.Expression, allowAggregates: false);
        }

        foreach (var measure in context.Config.Measures)
        {
            AddField(measure.SourceId, measure.Field);
            AddExpression(measure.SourceId, measure.Expression, allowAggregates: true);
        }

        return referenced;
    }

    public static Context CreateContext(
        AnalyticsDashboardConfig config,
        IReadOnlyDictionary<Guid, Dataset> datasetsById)
    {
        if (config.Sources.Count == 0)
        {
            throw new ReportQueryException("Dashboard has no sources.");
        }

        var datasetsBySourceId = new Dictionary<string, Dataset>(StringComparer.OrdinalIgnoreCase);
        var aliasBySourceId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in config.Sources)
        {
            if (!datasetsById.TryGetValue(source.DatasetId, out var dataset))
            {
                throw new ReportQueryException($"Source '{source.Alias}' references a missing dataset.");
            }

            if (!dataset.IsEnabled)
            {
                throw new ReportQueryException($"Dataset '{dataset.DisplayName}' is not enabled.");
            }

            var alias = NormalizeAlias(source.Id, usedAliases);
            datasetsBySourceId[source.Id] = dataset;
            aliasBySourceId[source.Id] = alias;
        }

        return new Context
        {
            Config = config,
            DatasetsBySourceId = datasetsBySourceId,
            AliasBySourceId = aliasBySourceId,
            FromSql = BuildFromClause(config, datasetsBySourceId, aliasBySourceId)
        };
    }

    public static BuiltAnalyticsQuery BuildDimensionDistribution(
        Context context,
        AnalyticsDimension dimension,
        Dictionary<string, List<string>> selections,
        string excludeDimensionId)
    {
        var bag = new ParameterBag();
        var sql = DimensionDistributionSql(context, dimension, selections, excludeDimensionId, bag);
        return new BuiltAnalyticsQuery { Sql = sql, Parameters = bag.Parameters };
    }

    public static string DimensionDistributionSql(
        Context context,
        AnalyticsDimension dimension,
        Dictionary<string, List<string>> selections,
        string excludeDimensionId,
        ParameterBag bag)
    {
        var fieldExpr = ResolveDimensionExpr(context, dimension);
        var filterWhere = BuildWhere(context, selections, excludeDimensionId, bag);
        var valueExpr = $"CASE WHEN {fieldExpr} IS NULL THEN N'{AnalyticsNullValue.Sentinel}' ELSE CONVERT(nvarchar(4000), {fieldExpr}) END";
        var countExpr = BuildAssociativeCount(context, filterWhere);

        var sql = $"""
            SELECT TOP ({MaxDimensionValues})
                [Value],
                [Cnt]
            FROM (
                SELECT
                    {valueExpr} AS [Value],
                    CAST({countExpr} AS bigint) AS [Cnt]
                FROM {context.FromSql}
                GROUP BY {fieldExpr}
            ) grouped
            ORDER BY CASE WHEN [Cnt] > 0 THEN 0 ELSE 1 END, [Cnt] DESC, [Value]
            """;

        return sql;
    }

    /// <summary>
    /// True when every filter list can be produced by <see cref="DimensionsSinglePassSql"/>: the source
    /// has one row per observation (so counts are plain conditional counts rather than distinct counts,
    /// which SQL Server would otherwise have to compute once per dimension inside a single statement),
    /// and no two dimensions group on the same expression, which <c>GROUPING()</c> could not tell apart.
    /// </summary>
    public static bool CanUseSinglePassDimensions(Context context)
    {
        if (!IsRowGrain(context) || context.Config.Dimensions.Count < 2)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dimension in context.Config.Dimensions)
        {
            if (!seen.Add(ResolveDimensionExpr(context, dimension)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Produces every filter list in one statement. Each dimension needs the counts under every other
    /// dimension's selection, so the per-dimension version scanned the source once per filter list.
    /// <c>GROUPING SETS</c> lets SQL Server read the source once, spool it, and aggregate each set from
    /// the spool; identical filters (every dimension without its own selection shares one) collapse to
    /// one conditional count. Rows come back as (dimension index, value, count), already trimmed to
    /// <see cref="MaxDimensionValues"/> per dimension in the same order the per-dimension query used.
    /// </summary>
    public static string DimensionsSinglePassSql(
        Context context,
        Dictionary<string, List<string>> selections,
        ParameterBag bag)
    {
        var dimensions = context.Config.Dimensions;
        var innerParts = new List<string>();
        var groupingSets = new List<string>(dimensions.Count);
        var countAliasByWhere = new Dictionary<string, string>(StringComparer.Ordinal);
        var countParts = new List<string>();
        var dimCases = new List<string>(dimensions.Count);
        var valueCases = new List<string>(dimensions.Count);
        var cntCases = new List<string>(dimensions.Count);

        for (var index = 0; index < dimensions.Count; index++)
        {
            var dimension = dimensions[index];
            var fieldExpr = ResolveDimensionExpr(context, dimension);
            var valueColumn = $"[__d{index}]";
            var flagColumn = $"[__g{index}]";
            groupingSets.Add($"({fieldExpr})");
            innerParts.Add($"{fieldExpr} AS {valueColumn}");
            innerParts.Add($"GROUPING({fieldExpr}) AS {flagColumn}");

            var filterWhere = BuildWhere(context, selections, dimension.Id, bag);
            if (!countAliasByWhere.TryGetValue(filterWhere, out var countAlias))
            {
                countAlias = $"[__c{countAliasByWhere.Count}]";
                countAliasByWhere[filterWhere] = countAlias;
                countParts.Add($"CAST({BuildAssociativeCount(context, filterWhere)} AS bigint) AS {countAlias}");
            }

            var isThisSet = $"{flagColumn} = 0";
            dimCases.Add($"WHEN {isThisSet} THEN {index}");
            valueCases.Add(
                $"WHEN {isThisSet} THEN CASE WHEN {valueColumn} IS NULL THEN N'{AnalyticsNullValue.Sentinel}' ELSE CONVERT(nvarchar(4000), {valueColumn}) END");
            cntCases.Add($"WHEN {isThisSet} THEN {countAlias}");
        }

        innerParts.AddRange(countParts);

        return $"""
            SELECT [Dim], [Value], [Cnt]
            FROM (
                SELECT
                    [Dim],
                    [Value],
                    [Cnt],
                    ROW_NUMBER() OVER (
                        PARTITION BY [Dim]
                        ORDER BY CASE WHEN [Cnt] > 0 THEN 0 ELSE 1 END, [Cnt] DESC, [Value]) AS [Rn]
                FROM (
                    SELECT
                        CASE {string.Join(" ", dimCases)} END AS [Dim],
                        CASE {string.Join(" ", valueCases)} END AS [Value],
                        CASE {string.Join(" ", cntCases)} END AS [Cnt]
                    FROM (
                        SELECT
                            {string.Join(",\n                            ", innerParts)}
                        FROM {context.FromSql}
                        GROUP BY GROUPING SETS ({string.Join(", ", groupingSets)})
                    ) sets
                ) grouped
            ) ranked
            WHERE [Rn] <= {MaxDimensionValues}
            ORDER BY [Dim], [Rn]
            """;
    }

    /// <summary>
    /// Finds dimension values whose display text contains <paramref name="query"/>, including values
    /// that the snapshot's TOP 500 list never returned.
    /// </summary>
    public static string DimensionSearchSql(
        Context context,
        AnalyticsDimension dimension,
        Dictionary<string, List<string>> selections,
        string query,
        int take,
        ParameterBag bag)
    {
        take = take <= 0 ? MaxSearchHitsPerDimension : Math.Min(take, MaxSearchHits);
        var fieldExpr = ResolveDimensionExpr(context, dimension);
        var filterWhere = BuildWhere(context, selections, dimension.Id, bag);
        var valueExpr = $"CASE WHEN {fieldExpr} IS NULL THEN N'{AnalyticsNullValue.Sentinel}' ELSE CONVERT(nvarchar(4000), {fieldExpr}) END";
        var countExpr = BuildAssociativeCount(context, filterWhere);
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var likePattern = terms.Length == 0
            ? "%"
            : "%" + string.Join("%", terms.Select(EscapeLike)) + "%";
        var likeName = bag.Add(likePattern, TextParameterType);

        return $"""
            SELECT TOP ({take})
                [Value],
                [Cnt]
            FROM (
                SELECT
                    {valueExpr} AS [Value],
                    CAST({countExpr} AS bigint) AS [Cnt]
                FROM {context.FromSql}
                WHERE {valueExpr} LIKE {likeName} ESCAPE N'\'
                GROUP BY {fieldExpr}
            ) grouped
            ORDER BY CASE WHEN [Cnt] > 0 THEN 0 ELSE 1 END, [Cnt] DESC, [Value]
            """;
    }

    private static string EscapeLike(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);

    public static BuiltAnalyticsQuery BuildMeasures(
        Context context,
        Dictionary<string, List<string>> selections)
    {
        var bag = new ParameterBag();
        var sql = MeasuresSql(context, selections, bag);
        return new BuiltAnalyticsQuery { Sql = sql, Parameters = bag.Parameters };
    }

    public static string MeasuresSql(
        Context context,
        Dictionary<string, List<string>> selections,
        ParameterBag bag)
    {
        if (context.Config.Measures.Count == 0)
        {
            return "SELECT CAST(0 AS int) AS [_empty] WHERE 1 = 0";
        }

        var whereSql = BuildWhere(context, selections, excludeDimensionId: null, bag);
        var selectParts = context.Config.Measures
            .Select(measure => $"{BuildAggregate(context, measure)} AS {SqlIdentifiers.Quote(measure.Id)}")
            .ToList();

        var sql = new StringBuilder();
        sql.Append("SELECT ").Append(string.Join(", ", selectParts))
            .Append(" FROM ").Append(context.FromSql);
        if (whereSql.Length > 0)
        {
            sql.Append(" WHERE ").Append(whereSql);
        }

        return sql.ToString();
    }

    public static BuiltAnalyticsQuery BuildGrid(
        Context context,
        Dictionary<string, List<string>> selections,
        int skip,
        int take)
    {
        var bag = new ParameterBag();
        var sql = GridSql(context, selections, skip, take, bag);
        return new BuiltAnalyticsQuery { Sql = sql, Parameters = bag.Parameters };
    }

    public static string GridSql(
        Context context,
        Dictionary<string, List<string>> selections,
        int skip,
        int take,
        ParameterBag bag)
    {
        take = take <= 0 ? DefaultTake : Math.Min(take, MaxTake);
        skip = Math.Max(skip, 0);

        var columns = context.Config.DetailColumns;
        if (columns.Count == 0)
        {
            columns = context.Config.Dimensions
                .Select(dimension => new AnalyticsDetailColumn
                {
                    Id = dimension.Id,
                    SourceId = dimension.SourceId,
                    Field = dimension.Field,
                    Label = dimension.Label
                })
                .ToList();
        }

        if (columns.Count == 0)
        {
            throw new ReportQueryException("Configure detail columns or dimensions to query the grid.");
        }

        var whereSql = BuildWhere(context, selections, excludeDimensionId: null, bag);
        var selectParts = columns
            .Select(column => $"{ResolveDetailExpr(context, column)} AS {SqlIdentifiers.Quote(column.Id)}")
            .ToList();
        var orderBy = BuildGridOrderBy(context, columns);

        var pageSql = new StringBuilder();
        pageSql.Append("SELECT ").Append(string.Join(", ", selectParts))
            .Append(", COUNT_BIG(*) OVER() AS [_total]")
            .Append(" FROM ").Append(context.FromSql);
        if (whereSql.Length > 0)
        {
            pageSql.Append(" WHERE ").Append(whereSql);
        }

        pageSql.Append(" ORDER BY ").Append(orderBy)
            .Append(" OFFSET ").Append(skip)
            .Append(" ROWS FETCH NEXT ").Append(take)
            .Append(" ROWS ONLY");

        return pageSql.ToString();
    }

    /// <summary>
    /// Counts the filtered set. Only needed when a page comes back empty, since the page query already
    /// carries the total in <c>_total</c> whenever it returns at least one row.
    /// </summary>
    public static string GridCountSql(
        Context context,
        Dictionary<string, List<string>> selections,
        ParameterBag bag)
    {
        var whereSql = BuildWhere(context, selections, excludeDimensionId: null, bag);
        var sql = new StringBuilder("SELECT COUNT_BIG(*) AS [_total] FROM ").Append(context.FromSql);
        if (whereSql.Length > 0)
        {
            sql.Append(" WHERE ").Append(whereSql);
        }

        return sql.ToString();
    }

    /// <summary>
    /// Sorting on every detail column forces a full sort of the filtered set for a single page. Prefer a
    /// single stable column, falling back to the full list only when nothing better is available.
    /// </summary>
    private static string BuildGridOrderBy(Context context, IReadOnlyList<AnalyticsDetailColumn> columns)
    {
        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column.Field))
            {
                continue;
            }

            if (TryGetColumnSqlType(context, column.SourceId, column.Field, out var sqlType)
                && BaseSqlType(sqlType) is "int" or "bigint" or "smallint" or "uniqueidentifier"
                    or "date" or "datetime" or "datetime2" or "datetimeoffset" or "smalldatetime")
            {
                return SqlIdentifiers.Quote(column.Id);
            }
        }

        return string.Join(", ", columns.Select(column => SqlIdentifiers.Quote(column.Id)));
    }

    public static BuiltAnalyticsQuery BuildChart(
        Context context,
        AnalyticsChart chart,
        Dictionary<string, List<string>> selections)
    {
        var bag = new ParameterBag();
        var sql = ChartSql(context, chart, selections, bag);
        return new BuiltAnalyticsQuery { Sql = sql, Parameters = bag.Parameters };
    }

    public static string ChartSql(
        Context context,
        AnalyticsChart chart,
        Dictionary<string, List<string>> selections,
        ParameterBag bag)
    {
        var dimension = context.Config.Dimensions.FirstOrDefault(item =>
                item.Id.Equals(chart.DimensionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ReportQueryException($"Chart '{chart.Title}' references an unknown dimension.");
        var measure = context.Config.Measures.FirstOrDefault(item =>
                item.Id.Equals(chart.MeasureId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ReportQueryException($"Chart '{chart.Title}' references an unknown measure.");

        var fieldExpr = ResolveDimensionExpr(context, dimension);
        var metricExpr = BuildAggregate(context, measure);
        var filterWhere = BuildWhere(context, selections, excludeDimensionId: dimension.Id, bag);
        var filterClause = filterWhere.Length > 0 ? " WHERE " + filterWhere : string.Empty;
        var valueExpr = $"CASE WHEN {fieldExpr} IS NULL THEN N'{AnalyticsNullValue.Sentinel}' ELSE CONVERT(nvarchar(4000), {fieldExpr}) END";

        var sql = $"""
            SELECT TOP ({MaxChartValues})
                [Value],
                [Metric]
            FROM (
                SELECT
                    {valueExpr} AS [Value],
                    {metricExpr} AS [Metric]
                FROM {context.FromSql}
                {filterClause}
                GROUP BY {fieldExpr}
            ) grouped
            ORDER BY [Metric] DESC
            """;

        return sql;
    }

    public static BuiltAnalyticsQuery BuildMap(
        Context context,
        AnalyticsMap map,
        Dictionary<string, List<string>> selections)
    {
        var bag = new ParameterBag();
        var sql = MapSql(context, map, selections, bag);
        return new BuiltAnalyticsQuery { Sql = sql, Parameters = bag.Parameters };
    }

    public static string MapSql(
        Context context,
        AnalyticsMap map,
        Dictionary<string, List<string>> selections,
        ParameterBag bag)
    {
        var lat = QualifyField(context, map.SourceId, map.LatField);
        var lng = QualifyField(context, map.SourceId, map.LngField);
        var label = string.IsNullOrWhiteSpace(map.LabelField)
            ? $"CONVERT(nvarchar(4000), {lat})"
            : $"CONVERT(nvarchar(4000), {QualifyField(context, map.SourceId, map.LabelField)})";
        var filterWhere = BuildWhere(context, selections, excludeDimensionId: null, bag);
        var whereParts = new List<string>
        {
            $"{lat} IS NOT NULL",
            $"{lng} IS NOT NULL"
        };
        if (filterWhere.Length > 0)
        {
            whereParts.Add(filterWhere);
        }

        var sql = $"""
            SELECT TOP ({MaxMapPoints})
                TRY_CONVERT(float, {lat}) AS [Lat],
                TRY_CONVERT(float, {lng}) AS [Lng],
                {label} AS [Label]
            FROM {context.FromSql}
            WHERE {string.Join(" AND ", whereParts)}
            """;

        return sql;
    }

    public static string ResolveValueState(string value, long count, IReadOnlyCollection<string> selected)
    {
        if (selected.Any(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            return AnalyticsValueStates.Selected;
        }

        return count > 0 ? AnalyticsValueStates.Associated : AnalyticsValueStates.Excluded;
    }

    public static string FormatCell(object? value)
    {
        if (value is null or DBNull)
        {
            return AnalyticsNullValue.Sentinel;
        }

        return value switch
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
    }

    private static string BuildAggregate(Context context, AnalyticsMeasure measure)
    {
        var aggregation = measure.Aggregation.Trim().ToLowerInvariant();

        if (aggregation is "expr" or "expression" or "calc")
        {
            if (string.IsNullOrWhiteSpace(measure.Expression))
            {
                throw new ReportQueryException($"Measure '{measure.Label}' needs a custom expression.");
            }

            return ResolveMeasureInner(context, measure, allowAggregates: true);
        }

        if (aggregation == "count"
            && string.IsNullOrWhiteSpace(measure.Field)
            && string.IsNullOrWhiteSpace(measure.Expression))
        {
            return BuildAssociativeCount(context, filterWhere: string.Empty);
        }

        var inner = ResolveMeasureInner(context, measure, allowAggregates: false);
        return aggregation switch
        {
            "count" => $"COUNT_BIG(DISTINCT {inner})",
            "sum" => $"SUM(TRY_CONVERT(float, {inner}))",
            "avg" => $"AVG(TRY_CONVERT(float, {inner}))",
            "min" => $"MIN({inner})",
            "max" => $"MAX({inner})",
            _ => throw new ReportQueryException($"Unsupported aggregation '{measure.Aggregation}'.")
        };
    }

    private static string ResolveDimensionExpr(Context context, AnalyticsDimension dimension)
    {
        if (!string.IsNullOrWhiteSpace(dimension.Expression))
        {
            return CompileOnSource(context, dimension.SourceId, dimension.Expression, allowAggregates: false);
        }

        if (string.IsNullOrWhiteSpace(dimension.Field))
        {
            throw new ReportQueryException($"Dimension '{dimension.Label}' needs a field or expression.");
        }

        return QualifyField(context, dimension.SourceId, dimension.Field);
    }

    private static string ResolveDetailExpr(Context context, AnalyticsDetailColumn column)
    {
        if (string.IsNullOrWhiteSpace(column.Field)
            && context.Config.Dimensions.FirstOrDefault(item =>
                item.Id.Equals(column.Id, StringComparison.OrdinalIgnoreCase)) is { } dimension
            && !string.IsNullOrWhiteSpace(dimension.Expression))
        {
            return ResolveDimensionExpr(context, dimension);
        }

        return QualifyField(context, column.SourceId, column.Field);
    }

    private static string ResolveMeasureInner(Context context, AnalyticsMeasure measure, bool allowAggregates)
    {
        if (!string.IsNullOrWhiteSpace(measure.Expression))
        {
            return CompileOnSource(context, measure.SourceId, measure.Expression, allowAggregates);
        }

        if (string.IsNullOrWhiteSpace(measure.Field))
        {
            throw new ReportQueryException($"Measure '{measure.Label}' needs a field or expression.");
        }

        return QualifyField(context, measure.SourceId, measure.Field);
    }

    private static string CompileOnSource(
        Context context,
        string sourceId,
        string expression,
        bool allowAggregates)
    {
        if (!context.DatasetsBySourceId.TryGetValue(sourceId, out var dataset)
            || !context.AliasBySourceId.TryGetValue(sourceId, out var alias))
        {
            throw new ReportQueryException($"Unknown source '{sourceId}'.");
        }

        var compiled = AnalyticsExpression.Compile(expression, alias, dataset, allowAggregates);
        if (context.ColumnMap is null)
        {
            return compiled;
        }

        foreach (var column in dataset.Columns)
        {
            var from = $"{alias}.{SqlIdentifiers.Quote(column.ColumnName)}";
            if (context.ColumnMap.TryGetValue(MapKey(alias, column.ColumnName), out var mapped))
            {
                compiled = compiled.Replace(from, mapped, StringComparison.OrdinalIgnoreCase);
            }
        }

        return compiled;
    }

    /// <summary>
    /// Associative counts must not inflate when a join fans out. A single-source FROM clause has one
    /// row per observation, so <c>COUNT_BIG(*)</c> is exact and cheaper than distinct over a guessed
    /// key. After a join the grain is the materialized <c>DENSE_RANK</c> integer, not a concatenated
    /// string.
    /// </summary>
    private static string BuildAssociativeCount(Context context, string filterWhere)
    {
        if (IsRowGrain(context))
        {
            return string.IsNullOrWhiteSpace(filterWhere)
                ? "COUNT_BIG(*)"
                : $"COUNT_BIG(CASE WHEN ({filterWhere}) THEN 1 END)";
        }

        var grain = BuildGrainExpression(context);
        return string.IsNullOrWhiteSpace(filterWhere)
            ? $"COUNT_BIG(DISTINCT {grain})"
            : $"COUNT_BIG(DISTINCT CASE WHEN ({filterWhere}) THEN {grain} END)";
    }

    private static bool IsRowGrain(Context context)
        => string.IsNullOrWhiteSpace(context.MaterializedGrain) && context.Config.Sources.Count <= 1;

    private static string BuildDenseRankGrain(Context context)
        => $"DENSE_RANK() OVER (ORDER BY {string.Join(", ", ResolveGrainParts(context))})";

    /// <summary>
    /// Prefer join keys on the first source as a stable grain to reduce join fan-out inflation.
    /// Falls back to a compact identity-like column, then the first visible column.
    /// </summary>
    private static string BuildGrainExpression(Context context)
    {
        if (!string.IsNullOrWhiteSpace(context.MaterializedGrain))
        {
            return context.MaterializedGrain;
        }

        var parts = ResolveGrainParts(context);
        return parts.Count == 1
            ? parts[0]
            : $"CONCAT_WS(N'|', {string.Join(", ", parts)})";
    }

    private static List<string> ResolveGrainParts(Context context)
    {
        var first = context.Config.Sources[0];
        var alias = context.AliasBySourceId[first.Id];
        var dataset = context.DatasetsBySourceId[first.Id];

        return ResolveGrainColumns(context, first, dataset)
            .Select(column =>
            {
                var quoted = $"{alias}.{SqlIdentifiers.Quote(column.ColumnName)}";
                return NeedsWideConvert(column.SqlType) ? $"CONVERT(nvarchar(4000), {quoted})" : quoted;
            })
            .ToList();
    }

    private static List<DatasetColumn> ResolveGrainColumns(
        Context context,
        AnalyticsSource first,
        Dataset dataset)
    {
        var keyFields = context.Config.Links
            .Where(link =>
                link.LeftSourceId.Equals(first.Id, StringComparison.OrdinalIgnoreCase)
                || link.RightSourceId.Equals(first.Id, StringComparison.OrdinalIgnoreCase))
            .SelectMany(link =>
                link.LeftSourceId.Equals(first.Id, StringComparison.OrdinalIgnoreCase)
                    ? link.LeftKeys
                    : link.RightKeys)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var visible = dataset.Columns
            .Where(column => !column.IsHidden)
            .OrderBy(column => column.Ordinal)
            .ToList();

        if (keyFields.Count > 0)
        {
            return keyFields
                .Select(field =>
                    visible.FirstOrDefault(item =>
                        item.ColumnName.Equals(field, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ReportQueryException(
                        $"Grain key '{field}' was not found on source '{first.Alias}'."))
                .ToList();
        }

        var identity = visible.FirstOrDefault(column =>
            BaseSqlType(column.SqlType) is "int" or "bigint" or "smallint" or "tinyint" or "uniqueidentifier");
        if (identity is not null)
        {
            return [identity];
        }

        var fallback = visible.FirstOrDefault()
            ?? throw new ReportQueryException($"Source '{first.Alias}' has no usable columns for counting.");
        return [fallback];
    }

    private static string BuildWhere(
        Context context,
        Dictionary<string, List<string>> selections,
        string? excludeDimensionId,
        ParameterBag bag)
    {
        var clauses = new List<string>();

        foreach (var dimension in context.Config.Dimensions)
        {
            if (excludeDimensionId is not null
                && dimension.Id.Equals(excludeDimensionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!selections.TryGetValue(dimension.Id, out var values) || values.Count == 0)
            {
                continue;
            }

            var fieldExpr = ResolveDimensionExpr(context, dimension);
            var normalized = values
                .Select(value => value?.Trim() ?? string.Empty)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalized.Count == 0)
            {
                continue;
            }

            var hasNull = normalized.Any(value =>
                value.Equals(AnalyticsNullValue.Sentinel, StringComparison.Ordinal));
            var nonNull = normalized
                .Where(value => !value.Equals(AnalyticsNullValue.Sentinel, StringComparison.Ordinal))
                .ToList();

            var parts = new List<string>();
            if (nonNull.Count > 0)
            {
                parts.Add(BuildInClause(context, dimension, fieldExpr, nonNull, bag));
            }

            if (hasNull)
            {
                parts.Add($"{fieldExpr} IS NULL");
            }

            if (parts.Count > 0)
            {
                clauses.Add("(" + string.Join(" OR ", parts) + ")");
            }
        }

        return string.Join(" AND ", clauses);
    }

    /// <summary>
    /// Compares against the raw column with a parameter of the column's own type whenever possible.
    /// Binding a mistyped parameter (the inferred nvarchar) would force an implicit conversion on the
    /// column side, which is not sargable and turns an index seek into a scan.
    /// </summary>
    private static string BuildInClause(
        Context context,
        AnalyticsDimension dimension,
        string fieldExpr,
        List<string> values,
        ParameterBag bag)
    {
        if (string.IsNullOrWhiteSpace(dimension.Expression)
            && TryGetColumnSqlType(context, dimension.SourceId, dimension.Field, out var sqlType)
            && CanFilterNatively(sqlType)
            && TryConvertAll(sqlType, values, out var typed))
        {
            var nativeNames = typed.Select(value => bag.Add(value, sqlType));
            return $"{fieldExpr} IN ({string.Join(", ", nativeNames)})";
        }

        var textNames = values.Select(value => bag.Add(value, TextParameterType));
        return $"CONVERT(nvarchar(4000), {fieldExpr}) IN ({string.Join(", ", textNames)})";
    }

    /// <summary>
    /// Converts every selection value to the column's CLR type, or fails as a whole so the caller can
    /// fall back to text comparison rather than silently dropping a value the user picked.
    /// </summary>
    private static bool TryConvertAll(string sqlType, List<string> values, out List<object> converted)
    {
        converted = new List<object>(values.Count);
        var type = BaseSqlType(sqlType);
        foreach (var value in values)
        {
            switch (type)
            {
                case "int" or "smallint" or "tinyint" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i):
                    converted.Add(i);
                    break;
                case "bigint" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l):
                    converted.Add(l);
                    break;
                case "bit" when TryParseBit(value, out var b):
                    converted.Add(b);
                    break;
                case "uniqueidentifier" when Guid.TryParse(value, out var g):
                    converted.Add(g);
                    break;
                case "nvarchar" or "varchar" or "nchar" or "char" or "sysname":
                    converted.Add(value);
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool TryParseBit(string value, out bool result)
    {
        if (bool.TryParse(value, out result))
        {
            return true;
        }

        if (value is "1")
        {
            result = true;
            return true;
        }

        if (value is "0")
        {
            result = false;
            return true;
        }

        return false;
    }

    private static string BuildFromClause(
        AnalyticsDashboardConfig config,
        IReadOnlyDictionary<string, Dataset> datasetsBySourceId,
        IReadOnlyDictionary<string, string> aliasBySourceId)
    {
        var first = config.Sources[0];
        var firstDataset = datasetsBySourceId[first.Id];
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { first.Id };
        var sql = new StringBuilder();
        sql.Append(DatasetSource.SqlServerRelation(firstDataset))
            .Append(' ')
            .Append(aliasBySourceId[first.Id]);

        var pending = config.Links.ToList();
        var guard = 0;
        while (pending.Count > 0 && guard++ < 64)
        {
            var progress = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var link = pending[i];
                string? left;
                string? right;
                List<string> leftKeys;
                List<string> rightKeys;
                var joinType = link.JoinType;

                if (included.Contains(link.LeftSourceId) && !included.Contains(link.RightSourceId))
                {
                    left = link.LeftSourceId;
                    right = link.RightSourceId;
                    leftKeys = link.LeftKeys;
                    rightKeys = link.RightKeys;
                }
                else if (included.Contains(link.RightSourceId) && !included.Contains(link.LeftSourceId))
                {
                    left = link.RightSourceId;
                    right = link.LeftSourceId;
                    leftKeys = link.RightKeys;
                    rightKeys = link.LeftKeys;
                }
                else if (included.Contains(link.LeftSourceId) && included.Contains(link.RightSourceId))
                {
                    pending.RemoveAt(i);
                    continue;
                }
                else
                {
                    continue;
                }

                if (leftKeys.Count == 0 || leftKeys.Count != rightKeys.Count)
                {
                    throw new ReportQueryException("Each link must have matching left/right keys.");
                }

                var rightDataset = datasetsBySourceId[right];
                var sqlJoin = joinType.Equals("left", StringComparison.OrdinalIgnoreCase) ? "LEFT" : "INNER";
                sql.Append(' ').Append(sqlJoin).Append(" JOIN ")
                    .Append(DatasetSource.SqlServerRelation(rightDataset))
                    .Append(' ')
                    .Append(aliasBySourceId[right])
                    .Append(" ON ");

                var onParts = new List<string>();
                for (var k = 0; k < leftKeys.Count; k++)
                {
                    var leftField = RequireColumn(datasetsBySourceId[left], leftKeys[k]);
                    var rightField = RequireColumn(rightDataset, rightKeys[k]);
                    onParts.Add(
                        $"{aliasBySourceId[left]}.{SqlIdentifiers.Quote(leftField)} = {aliasBySourceId[right]}.{SqlIdentifiers.Quote(rightField)}");
                }

                sql.Append(string.Join(" AND ", onParts));
                included.Add(right);
                pending.RemoveAt(i);
                progress = true;
            }

            if (!progress)
            {
                break;
            }
        }

        if (included.Count != config.Sources.Count)
        {
            if (config.Sources.Count > 1 && config.Links.Count == 0)
            {
                throw new ReportQueryException("Multiple sources require at least one join link.");
            }

            throw new ReportQueryException("Could not connect all sources with the configured links.");
        }

        return sql.ToString();
    }

    private static string QualifyField(Context context, string sourceId, string field)
    {
        if (!context.DatasetsBySourceId.TryGetValue(sourceId, out var dataset)
            || !context.AliasBySourceId.TryGetValue(sourceId, out var alias))
        {
            throw new ReportQueryException($"Unknown source '{sourceId}'.");
        }

        var column = RequireColumn(dataset, field);
        if (context.ColumnMap is not null
            && context.ColumnMap.TryGetValue(MapKey(alias, column), out var mapped))
        {
            return mapped;
        }

        return $"{alias}.{SqlIdentifiers.Quote(column)}";
    }

    private static string MapKey(string alias, string columnName)
        => alias + "\n" + columnName;

    private static string UniquePhysicalName(string alias, string columnName, HashSet<string> used)
    {
        var physical = alias + "__" + columnName;
        if (physical.Length > 120)
        {
            physical = alias + "__" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(columnName)))[..16];
        }

        var candidate = physical;
        var n = 1;
        while (!used.Add(candidate))
        {
            candidate = physical + "_" + n++;
        }

        return candidate;
    }

    private static bool TryGetColumnSqlType(Context context, string sourceId, string field, out string sqlType)
    {
        sqlType = string.Empty;
        if (!context.DatasetsBySourceId.TryGetValue(sourceId, out var dataset))
        {
            return false;
        }

        var column = dataset.Columns.FirstOrDefault(item =>
            item.ColumnName.Equals(field, StringComparison.OrdinalIgnoreCase) && !item.IsHidden);
        if (column is null)
        {
            return false;
        }

        sqlType = column.SqlType ?? string.Empty;
        return true;
    }

    private static bool CanFilterNatively(string sqlType)
        => BaseSqlType(sqlType) is "int" or "bigint" or "smallint" or "tinyint" or "bit"
            or "uniqueidentifier"
            or "nvarchar" or "varchar" or "nchar" or "char" or "sysname";

    /// <summary>Strips any length/precision suffix, e.g. <c>nvarchar(200)</c> to <c>nvarchar</c>.</summary>
    public static string BaseSqlType(string? sqlType)
    {
        var type = (sqlType ?? string.Empty).Trim().ToLowerInvariant();
        var paren = type.IndexOf('(', StringComparison.Ordinal);
        return (paren >= 0 ? type[..paren] : type).Trim();
    }

    private static string RequireColumn(Dataset dataset, string field)
    {
        var column = dataset.Columns.FirstOrDefault(item =>
            item.ColumnName.Equals(field, StringComparison.OrdinalIgnoreCase) && !item.IsHidden)
            ?? throw new ReportQueryException(
                $"Column '{field}' was not found on {dataset.SchemaName}.{dataset.ObjectName}.");
        return column.ColumnName;
    }

    private static bool NeedsWideConvert(string sqlType)
    {
        var type = (sqlType ?? string.Empty).Trim().ToLowerInvariant();
        return type.Contains("max", StringComparison.Ordinal)
            || type is "text" or "ntext" or "image" or "xml" or "sql_variant";
    }

    private static string NormalizeAlias(string sourceId, HashSet<string> used)
    {
        var cleaned = SafeAlias.IsMatch(sourceId)
            ? sourceId
            : "s_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId)))[..8];

        var alias = cleaned;
        var n = 1;
        while (!used.Add(alias))
        {
            alias = cleaned + "_" + n++;
        }

        return alias;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex AliasRegex();
}
