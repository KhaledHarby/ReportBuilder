using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public static class ReportQueryBuilder
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;
    public const int DefaultDistinctTake = 200;
    public const int MaxDistinctTake = 500;

    private static readonly HashSet<string> DistinctBlockedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image", "varbinary", "binary", "xml", "sql_variant", "geography", "geometry",
        "hierarchyid", "text", "ntext", "filestream"
    };

    private static readonly HashSet<string> AllowedAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "COUNT", "AVG", "MIN", "MAX"
    };

    private static readonly HashSet<string> NumericSqlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "int", "bigint", "smallint", "tinyint", "decimal", "numeric", "money", "smallmoney", "float", "real"
    };

    public static BuiltQuery Build(
        Dataset dataset,
        ReportLayout layout,
        int skip,
        int take,
        bool allRows = false,
        QueryDialect? dialect = null,
        int? maxTake = null)
    {
        dialect ??= QueryDialect.SqlServer;
        if (!dataset.IsEnabled)
        {
            throw new ReportQueryException($"Dataset '{dataset.DisplayName}' is not enabled.");
        }

        if (!allRows)
        {
            var ceiling = maxTake is > 0 ? maxTake.Value : MaxTake;
            take = take <= 0 ? DefaultTake : Math.Min(take, ceiling);
            skip = Math.Max(skip, 0);
        }
        else
        {
            skip = 0;
            take = int.MaxValue;
        }

        var columnsByName = dataset.Columns
            .Where(c => !c.IsHidden)
            .ToDictionary(c => c.ColumnName, c => c, StringComparer.OrdinalIgnoreCase);

        if (layout.Columns.Count == 0)
        {
            throw new ReportQueryException("Select at least one column.");
        }

        var groupBy = layout.GroupBy
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var grouping = groupBy.Count > 0;
        var selectParts = new List<string>();
        var resultNames = new List<string>();

        var hasExpression = false;
        foreach (var column in layout.Columns)
        {
            if (!string.IsNullOrWhiteSpace(column.Expression))
            {
                hasExpression = true;
                var compiled = AnalyticsExpression.Compile(column.Expression, "rb_src", dataset, grouping);
                selectParts.Add($"({compiled})");
                var label = string.IsNullOrWhiteSpace(column.Alias) ? "Calculation" : column.Alias.Trim();
                resultNames.Add(label);
                continue;
            }

            var meta = RequireColumn(columnsByName, column.Field, "column");
            var quoted = dialect.Quote(meta.ColumnName);
            var aggregate = NormalizeAggregate(column.Aggregate);
            var inGroup = grouping && groupBy.Any(g => g.Equals(meta.ColumnName, StringComparison.OrdinalIgnoreCase));

            if (grouping)
            {
                if (aggregate is null && !inGroup)
                {
                    throw new ReportQueryException(
                        $"Column '{meta.ColumnName}' must be grouped or aggregated when grouping is used.");
                }

                if (aggregate is not null && inGroup)
                {
                    throw new ReportQueryException(
                        $"Column '{meta.ColumnName}' is in the group list and cannot also be aggregated.");
                }
            }
            else if (aggregate is not null)
            {
                throw new ReportQueryException("Aggregates require at least one group-by field.");
            }

            if (aggregate is not null)
            {
                ValidateAggregateType(aggregate, meta);
                selectParts.Add($"{aggregate}({quoted})");
            }
            else
            {
                selectParts.Add(quoted);
            }

            resultNames.Add(string.IsNullOrWhiteSpace(column.Alias) ? meta.ColumnName : column.Alias.Trim());
        }

        var hiddenResultNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AppendUrlTokenColumns(
            layout,
            dataset.Columns,
            columnsByName,
            groupBy,
            grouping,
            selectParts,
            resultNames,
            hiddenResultNames,
            dialect);

        var selectList = string.Join(", ", selectParts.Select((expr, i) =>
            $"{expr} AS {dialect.Quote(resultNames[i])}"));

        var from = FromTable(dataset, dialect);

        var (whereSql, parameters) = BuildWhere(layout.Filters, columnsByName, dialect);

        var sql = new StringBuilder();
        sql.Append("SELECT ").Append(selectList)
            .Append(" FROM ").Append(from);
        if (hasExpression)
        {
            sql.Append(" AS rb_src");
        }

        if (whereSql.Length > 0)
        {
            sql.Append(" WHERE ").Append(whereSql);
        }

        if (grouping)
        {
            var groupExprs = groupBy.Select(g =>
            {
                var meta = RequireColumn(columnsByName, g, "group-by");
                if (!meta.IsGroupable)
                {
                    throw new ReportQueryException($"Column '{meta.ColumnName}' cannot be used for grouping.");
                }

                return dialect.Quote(meta.ColumnName);
            });
            sql.Append(" GROUP BY ").AppendJoin(", ", groupExprs);
        }

        var orderSql = BuildOrderBy(dataset, layout, groupBy, columnsByName, grouping, dialect);
        if (orderSql.Length > 0)
        {
            sql.Append(" ORDER BY ").Append(orderSql);
        }
        else if (layout.Columns.Count > 0)
        {
            var first = layout.Columns[0];
            if (!string.IsNullOrWhiteSpace(first.Expression))
            {
                sql.Append(" ORDER BY ").Append(dialect.Quote(resultNames[0]));
            }
            else
            {
                var meta = RequireColumn(columnsByName, first.Field, "sort");
                sql.Append(" ORDER BY ")
                    .Append(dialect.Quote(meta.ColumnName))
                    .Append(' ')
                    .Append(SortDirections.ToSql(null, meta.DefaultSortDirection));
            }
        }
        else
        {
            sql.Append(" ORDER BY ").Append(selectParts[0]);
        }

        dialect.AppendPaging(sql, allRows);

        string countSql;
        if (grouping)
        {
            var inner = new StringBuilder();
            inner.Append("SELECT 1 AS ").Append(dialect.DummyColumn).Append(" FROM ").Append(from);
            if (whereSql.Length > 0)
            {
                inner.Append(" WHERE ").Append(whereSql);
            }

            inner.Append(" GROUP BY ").AppendJoin(", ", groupBy.Select(g =>
                dialect.Quote(RequireColumn(columnsByName, g, "group-by").ColumnName)));

            countSql = $"SELECT COUNT(1) FROM ({inner}) AS {dialect.CountAlias}";
        }
        else
        {
            countSql = whereSql.Length > 0
                ? $"SELECT COUNT(1) FROM {from} WHERE {whereSql}"
                : $"SELECT COUNT(1) FROM {from}";
        }

        return new BuiltQuery
        {
            DataSql = sql.ToString(),
            CountSql = countSql,
            FilterParameters = parameters,
            Skip = skip,
            Take = take,
            AllRows = allRows,
            HiddenResultNames = hiddenResultNames
        };
    }

    public static BuiltDistinctQuery BuildDistinctValuesQuery(
        Dataset dataset,
        string field,
        int take,
        IEnumerable<LayoutFilter>? filters = null,
        QueryDialect? dialect = null)
    {
        dialect ??= QueryDialect.SqlServer;
        if (!dataset.IsEnabled)
        {
            throw new ReportQueryException($"Dataset '{dataset.DisplayName}' is not enabled.");
        }

        take = take <= 0 ? DefaultDistinctTake : Math.Min(take, MaxDistinctTake);
        var columnsByName = dataset.Columns
            .Where(c => !c.IsHidden)
            .ToDictionary(c => c.ColumnName, c => c, StringComparer.OrdinalIgnoreCase);
        var meta = RequireColumn(columnsByName, field, "filter values");
        if (!meta.IsFilterable)
        {
            throw new ReportQueryException($"Column '{meta.ColumnName}' cannot be used as a filter.");
        }

        if (DistinctBlockedTypes.Contains(meta.SqlType))
        {
            throw new ReportQueryException($"Column '{meta.ColumnName}' cannot provide a list of values.");
        }

        var quoted = dialect.Quote(meta.ColumnName);
        var from = FromTable(dataset, dialect);

        var direction = SortDirections.ToSql(null, meta.DefaultSortDirection);
        var scoped = ScopeFiltersForDistinct(field, filters);
        var (whereSql, parameters) = BuildWhere(scoped, columnsByName, dialect);

        var sql = new StringBuilder();
        sql.Append(dialect.DistinctHead(take + 1, quoted)).Append(" FROM ").Append(from)
            .Append(" WHERE ").Append(quoted).Append(" IS NOT NULL");
        if (whereSql.Length > 0)
        {
            sql.Append(" AND (").Append(whereSql).Append(')');
        }

        sql.Append(" ORDER BY ").Append(dialect.Quote("value")).Append(' ').Append(direction);
        sql.Append(dialect.DistinctTail(take + 1));

        return new BuiltDistinctQuery
        {
            Sql = sql.ToString(),
            Parameters = parameters
        };
    }

    public static string BuildDistinctValuesSql(Dataset dataset, string field, int take)
        => BuildDistinctValuesQuery(dataset, field, take).Sql;

    public static bool HasActiveFilter(IEnumerable<LayoutFilter>? filters)
    {
        if (filters is null)
        {
            return false;
        }

        foreach (var filter in filters)
        {
            var op = (filter.Operator ?? "eq").Trim().ToLowerInvariant();
            if (op is "isnull" or "isnotnull" or "nomatch")
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(filter.Value))
            {
                return true;
            }
        }

        return false;
    }

    public static List<LayoutFilter> ScopeFiltersForDistinct(string field, IEnumerable<LayoutFilter>? filters)
    {
        if (filters is null)
        {
            return [];
        }

        var list = filters
            .Where(filter => !string.IsNullOrWhiteSpace(filter.Field))
            .ToList();

        var ancestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listed = list.FirstOrDefault(filter =>
            filter.Field.Equals(field, StringComparison.OrdinalIgnoreCase));
        var current = listed?.DependsOn;
        while (!string.IsNullOrWhiteSpace(current) && ancestors.Add(current))
        {
            current = list
                .FirstOrDefault(item => item.Field.Equals(current, StringComparison.OrdinalIgnoreCase))
                ?.DependsOn;
        }

        return list.Where(filter => ancestors.Contains(filter.Field)).ToList();
    }

    private static (string Sql, List<QueryParameter> Parameters) BuildWhere(
        IEnumerable<LayoutFilter> filters,
        IReadOnlyDictionary<string, DatasetColumn> columnsByName,
        QueryDialect dialect)
    {
        var parameters = new List<QueryParameter>();
        var index = 0;
        var fieldOrder = new List<string>();
        var byField = new Dictionary<string, List<(string Op, string? Value)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter.Field))
            {
                continue;
            }

            var meta = RequireColumn(columnsByName, filter.Field, "filter");
            if (!meta.IsFilterable)
            {
                throw new ReportQueryException($"Column '{meta.ColumnName}' cannot be used as a filter.");
            }

            var op = (filter.Operator ?? "eq").Trim().ToLowerInvariant();
            if (op is not "isnull" and not "isnotnull" && string.IsNullOrWhiteSpace(filter.Value))
            {
                continue;
            }

            if (op is "in" or "eq" && SplitListValues(filter.Value).Count == 0)
            {
                continue;
            }

            if (!byField.TryGetValue(meta.ColumnName, out var list))
            {
                list = [];
                byField[meta.ColumnName] = list;
                fieldOrder.Add(meta.ColumnName);
            }

            list.Add((op, filter.Value));
        }

        var clauses = fieldOrder
            .Select(field => BuildFieldPredicate(columnsByName[field], byField[field], parameters, ref index, dialect))
            .ToList();

        return (string.Join(" AND ", clauses), parameters);
    }

    private static string BuildFieldPredicate(
        DatasetColumn meta,
        List<(string Op, string? Value)> filters,
        List<QueryParameter> parameters,
        ref int index,
        QueryDialect dialect)
    {
        var quoted = dialect.Quote(meta.ColumnName);
        var equalValues = new List<string>();
        var parts = new List<string>();

        foreach (var (op, raw) in filters)
        {
            switch (op)
            {
                case "eq":
                case "in":
                    equalValues.AddRange(SplitListValues(raw));
                    break;
                case "isnull":
                    parts.Add($"{quoted} IS NULL");
                    break;
                case "isnotnull":
                    parts.Add($"{quoted} IS NOT NULL");
                    break;
                case "contains":
                {
                    var name = $"@p{index++}";
                    parts.Add(dialect.ContainsPredicate(quoted, name));
                    var needle = dialect.IsClickHouse
                        ? (raw ?? string.Empty)
                        : "%" + EscapeLike(raw ?? string.Empty) + "%";
                    parameters.Add(new QueryParameter(name, needle, "nvarchar"));
                    break;
                }
                case "andin":
                {
                    var keys = LookupFilterKeys.Split(raw);
                    parts.Add(keys.Count == 0
                        ? "1 = 0"
                        : BuildEqualsOrIn(quoted, meta, keys, parameters, ref index, dialect));
                    break;
                }
                case "andnin":
                {
                    var keys = LookupFilterKeys.Split(raw);
                    if (keys.Count == 0)
                    {
                        break;
                    }

                    parts.Add($"{quoted} IS NOT NULL AND NOT ({BuildEqualsOrIn(quoted, meta, keys, parameters, ref index, dialect)})");
                    break;
                }
                case "nomatch":
                    parts.Add("1 = 0");
                    break;
                case "neq":
                case "gt":
                case "gte":
                case "lt":
                case "lte":
                {
                    var name = $"@p{index++}";
                    var sqlOp = op switch
                    {
                        "neq" => "<>",
                        "gt" => ">",
                        "gte" => ">=",
                        "lt" => "<",
                        "lte" => "<=",
                        _ => "="
                    };
                    parts.Add($"{quoted} {sqlOp} {dialect.Bind(name, meta.SqlType)}");
                    parameters.Add(new QueryParameter(name, ConvertFilterValue(meta, raw), meta.SqlType));
                    break;
                }
                default:
                    throw new ReportQueryException($"Unsupported filter operator '{op}'.");
            }
        }

        if (equalValues.Count > 0)
        {
            parts.Insert(0, BuildEqualsOrIn(quoted, meta, equalValues, parameters, ref index, dialect));
        }

        return parts.Count == 1 ? parts[0] : "(" + string.Join(" AND ", parts) + ")";
    }

    private static string BuildEqualsOrIn(
        string quoted,
        DatasetColumn meta,
        IReadOnlyList<string> values,
        List<QueryParameter> parameters,
        ref int index,
        QueryDialect dialect)
    {
        var distinct = values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0)
        {
            return "1 = 1";
        }

        if (distinct.Count == 1)
        {
            var name = $"@p{index++}";
            parameters.Add(new QueryParameter(name, ConvertFilterValue(meta, distinct[0]), meta.SqlType));
            return $"{quoted} = {dialect.Bind(name, meta.SqlType)}";
        }

        var names = new List<string>(distinct.Count);
        foreach (var value in distinct)
        {
            var name = $"@p{index++}";
            names.Add(dialect.Bind(name, meta.SqlType));
            parameters.Add(new QueryParameter(name, ConvertFilterValue(meta, value), meta.SqlType));
        }

        return $"{quoted} IN ({string.Join(", ", names)})";
    }

    private static List<string> SplitListValues(string? raw) =>
        (raw ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    private static string BuildOrderBy(
        Dataset dataset,
        ReportLayout layout,
        IReadOnlyList<string> groupBy,
        IReadOnlyDictionary<string, DatasetColumn> columnsByName,
        bool grouping,
        QueryDialect dialect)
    {
        var parts = new List<string>();

        foreach (var item in layout.OrderBy)
        {
            if (string.IsNullOrWhiteSpace(item.Field))
            {
                continue;
            }

            if (LookupSortResolver.TryOrderColumn(dataset, layout, item.Field, out var fact, out var lookupText))
            {
                var direction = SortDirections.ToSql(item.Direction, fact.DefaultSortDirection);
                parts.Add(LookupOrder(dialect, dialect.Quote(fact.ColumnName), lookupText, direction));
                continue;
            }

            var meta = RequireColumn(columnsByName, item.Field, "sort");
            var plainDirection = SortDirections.ToSql(item.Direction, meta.DefaultSortDirection);

            var selectedColumn = layout.Columns.FirstOrDefault(c =>
                c.Field.Equals(meta.ColumnName, StringComparison.OrdinalIgnoreCase));
            var inGroup = groupBy.Any(g => g.Equals(meta.ColumnName, StringComparison.OrdinalIgnoreCase));
            var aggregate = selectedColumn is null ? null : NormalizeAggregate(selectedColumn.Aggregate);

            string expr;
            if (aggregate is not null)
            {
                ValidateAggregateType(aggregate, meta);
                expr = $"{aggregate}({dialect.Quote(meta.ColumnName)})";
            }
            else if (!grouping || inGroup || selectedColumn is not null)
            {
                expr = dialect.Quote(meta.ColumnName);
            }
            else
            {
                throw new ReportQueryException(
                    $"Cannot sort by '{meta.ColumnName}' because it is not selected, grouped, or aggregated.");
            }

            parts.Add($"{expr} {plainDirection}");
        }

        return string.Join(", ", parts);
    }

    private static string LookupOrder(
        QueryDialect dialect,
        string quotedFact,
        IReadOnlyDictionary<string, string>? lookupText,
        string direction)
    {
        var text = LookupCase(dialect, quotedFact, lookupText);
        var nullsLast = $"CASE WHEN {text} IS NULL THEN 1 ELSE 0 END";
        return $"{nullsLast}, {text} {direction}";
    }

    private static string LookupCase(
        QueryDialect dialect,
        string quotedFact,
        IReadOnlyDictionary<string, string>? lookupText)
    {
        if (lookupText is null || lookupText.Count == 0)
        {
            return dialect.IsClickHouse
                ? "CAST(NULL AS Nullable(String))"
                : dialect.IsPostgres
                    ? "CAST(NULL AS text)"
                    : "CAST(NULL AS nvarchar(4000))";
        }

        if (lookupText.Count > LookupSortResolver.MaxKeys)
        {
            throw new ReportQueryException(
                $"Lookup sort matches more than {LookupSortResolver.MaxKeys} keys. Narrow the report and try again.");
        }

        var source = dialect.IsClickHouse
            ? $"toString({quotedFact})"
            : dialect.IsPostgres
                ? $"({quotedFact})::text"
                : $"CONVERT(nvarchar(4000), {quotedFact})";
        var sql = new StringBuilder();
        sql.Append("CASE ").Append(source);
        foreach (var pair in lookupText)
        {
            sql.Append(" WHEN ").Append(SqlText(dialect, pair.Key))
                .Append(" THEN ").Append(SqlText(dialect, pair.Value));
        }

        sql.Append(" ELSE NULL END");
        return sql.ToString();
    }

    private static string SqlText(QueryDialect dialect, string value)
    {
        var escaped = value.Replace("'", "''", StringComparison.Ordinal);
        return dialect.IsClickHouse || dialect.IsPostgres
            ? "'" + escaped + "'"
            : "N'" + escaped + "'";
    }

    private static readonly HashSet<string> CurrentColumnTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "field", "value", "this"
    };

    private static void AppendUrlTokenColumns(
        ReportLayout layout,
        IEnumerable<DatasetColumn> datasetColumns,
        IReadOnlyDictionary<string, DatasetColumn> columnsByName,
        IReadOnlyList<string> groupBy,
        bool grouping,
        List<string> selectParts,
        List<string> resultNames,
        HashSet<string> hiddenResultNames,
        QueryDialect dialect)
    {
        var selected = new HashSet<string>(resultNames, StringComparer.OrdinalIgnoreCase);
        foreach (var column in layout.Columns)
        {
            selected.Add(column.Field);
        }

        var catalog = datasetColumns.ToList();
        foreach (var token in UrlTokens(layout))
        {
            if (CurrentColumnTokens.Contains(token))
            {
                continue;
            }

            var meta = FindCatalogColumn(columnsByName, catalog, token);
            if (meta is null || selected.Contains(meta.ColumnName) || selected.Contains(token))
            {
                continue;
            }

            if (grouping && !groupBy.Any(g => g.Equals(meta.ColumnName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            selectParts.Add(dialect.Quote(meta.ColumnName));
            resultNames.Add(meta.ColumnName);
            hiddenResultNames.Add(meta.ColumnName);
            selected.Add(meta.ColumnName);
        }
    }

    private static IEnumerable<string> UrlTokens(ReportLayout layout)
    {
        foreach (var column in layout.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.UrlTemplate))
            {
                continue;
            }

            foreach (Match match in Regex.Matches(column.UrlTemplate, @"\{([^}]+)\}"))
            {
                var token = match.Groups[1].Value.Trim();
                if (token.Length > 0)
                {
                    yield return token;
                }
            }
        }
    }

    private static DatasetColumn? FindCatalogColumn(
        IReadOnlyDictionary<string, DatasetColumn> columnsByName,
        IReadOnlyList<DatasetColumn> catalog,
        string token)
    {
        if (columnsByName.TryGetValue(token, out var meta))
        {
            return meta;
        }

        return catalog.FirstOrDefault(column =>
            column.ColumnName.Equals(token, StringComparison.OrdinalIgnoreCase)
            || column.DisplayName.Equals(token, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(column.DisplayNameArabic)
                && column.DisplayNameArabic.Equals(token, StringComparison.OrdinalIgnoreCase)));
    }

    private static DatasetColumn RequireColumn(
        IReadOnlyDictionary<string, DatasetColumn> columnsByName,
        string field,
        string usage)
    {
        if (!columnsByName.TryGetValue(field, out var meta))
        {
            throw new ReportQueryException($"Unknown or hidden {usage} field '{field}'.");
        }

        return meta;
    }

    private static string? NormalizeAggregate(string? aggregate)
    {
        if (string.IsNullOrWhiteSpace(aggregate) || aggregate.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!AllowedAggregates.Contains(aggregate))
        {
            throw new ReportQueryException($"Unsupported aggregate '{aggregate}'.");
        }

        return aggregate.ToUpperInvariant();
    }

    private static void ValidateAggregateType(string aggregate, DatasetColumn meta)
    {
        if ((aggregate is "SUM" or "AVG") && !NumericSqlTypes.Contains(meta.SqlType))
        {
            throw new ReportQueryException($"{aggregate} can only be applied to numeric columns (not '{meta.ColumnName}').");
        }
    }

    private static object ConvertFilterValue(DatasetColumn meta, string? raw)
    {
        var value = raw ?? string.Empty;
        var type = meta.SqlType.ToLowerInvariant();

        try
        {
            return type switch
            {
                "int" or "smallint" or "tinyint" => int.Parse(value, CultureInfo.InvariantCulture),
                "bigint" => long.Parse(value, CultureInfo.InvariantCulture),
                "decimal" or "numeric" or "money" or "smallmoney" => decimal.Parse(value, CultureInfo.InvariantCulture),
                "float" or "real" => double.Parse(value, CultureInfo.InvariantCulture),
                "bit" => value is "1" or "true" or "True" or "yes",
                "uniqueidentifier" => Guid.Parse(value),
                "date" or "datetime" or "datetime2" or "smalldatetime" => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                "time" => TimeSpan.Parse(value, CultureInfo.InvariantCulture),
                _ => value
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new ReportQueryException($"Filter value '{raw}' is not valid for column '{meta.ColumnName}' ({meta.SqlType}).");
        }
    }

    private static string FromTable(Dataset dataset, QueryDialect dialect)
        => DatasetSource.ReportFrom(dataset, dialect);

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    public static bool IsNumericSqlType(string sqlType) => NumericSqlTypes.Contains(sqlType);
}
