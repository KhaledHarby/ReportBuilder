using System.Globalization;
using System.Text.RegularExpressions;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public sealed class DocumentQueryPlan
{
    public required IReadOnlyDictionary<string, DatasetColumn> ColumnsByName { get; init; }
    public required IReadOnlyList<PlanSelection> Selections { get; init; }
    public required IReadOnlyList<PlanFieldFilter> Filters { get; init; }
    public required IReadOnlyList<PlanSort> Sorts { get; init; }
    public required IReadOnlyList<string> GroupBy { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
    public bool AllRows { get; init; }
    public bool ExplicitSort { get; init; }
    public bool Grouping => GroupBy.Count > 0;
}

public sealed class PlanSelection
{
    public required DatasetColumn Column { get; init; }
    public string? Aggregate { get; init; }
    public required string ResultName { get; init; }
    public string? Alias { get; init; }
    public bool Hidden { get; init; }
}

public sealed class PlanFieldFilter
{
    public required DatasetColumn Column { get; init; }
    public required IReadOnlyList<PlanPredicate> Predicates { get; init; }
}

public sealed class PlanPredicate
{
    public required string Operator { get; init; }
    public string? Value { get; init; }
}

public sealed class PlanSort
{
    public required PlanSelection Selection { get; init; }
    public bool Descending { get; init; }
    public IReadOnlyDictionary<string, string>? LookupText { get; init; }
}

public static class DocumentQueryPlanner
{
    private static readonly HashSet<string> CurrentColumnTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "field", "value", "this"
    };

    public static DocumentQueryPlan ForReport(
        Dataset dataset,
        ReportLayout layout,
        int skip,
        int take,
        bool allRows = false,
        int? maxTake = null)
    {
        EnsureEnabled(dataset);
        if (layout.Columns.Count == 0)
        {
            throw new ReportQueryException("Select at least one column.");
        }

        if (!allRows)
        {
            var ceiling = maxTake ?? ReportQueryBuilder.MaxTake;
            take = take <= 0 ? ReportQueryBuilder.DefaultTake : Math.Min(take, ceiling);
            skip = Math.Max(skip, 0);
        }
        else
        {
            skip = 0;
            take = int.MaxValue;
        }

        var columnsByName = VisibleColumns(dataset);
        var groupBy = layout.GroupBy
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var grouping = groupBy.Count > 0;
        var selections = BuildSelections(dataset, layout, columnsByName, groupBy, grouping);
        var explicitSort = layout.OrderBy.Any(item => !string.IsNullOrWhiteSpace(item.Field));
        return new DocumentQueryPlan
        {
            ColumnsByName = columnsByName,
            Selections = selections,
            Filters = BuildFilters(layout.Filters, columnsByName),
            Sorts = BuildSorts(dataset, layout, selections, columnsByName, grouping),
            GroupBy = groupBy,
            Skip = skip,
            Take = take,
            AllRows = allRows,
            ExplicitSort = explicitSort
        };
    }

    public static DocumentQueryPlan ForDistinct(
        Dataset dataset,
        string field,
        int take,
        IEnumerable<LayoutFilter>? filters = null)
    {
        EnsureEnabled(dataset);
        take = take <= 0 ? ReportQueryBuilder.DefaultDistinctTake : Math.Min(take, ReportQueryBuilder.MaxDistinctTake);
        var columnsByName = VisibleColumns(dataset);
        var meta = Require(columnsByName, field, "filter values");
        if (!meta.IsFilterable)
        {
            throw new ReportQueryException($"Column '{meta.ColumnName}' cannot be used as a filter.");
        }

        var scoped = ScopeFiltersForDistinct(field, filters);
        return new DocumentQueryPlan
        {
            ColumnsByName = columnsByName,
            Selections = [new PlanSelection { Column = meta, ResultName = meta.ColumnName }],
            Filters = BuildFilters(scoped, columnsByName),
            Sorts =
            [
                new PlanSort
                {
                    Selection = new PlanSelection { Column = meta, ResultName = meta.ColumnName },
                    Descending = SortDirections.Normalize(null, meta.DefaultSortDirection) == SortDirections.Desc
                }
            ],
            GroupBy = [],
            Skip = 0,
            Take = take,
            AllRows = false,
            ExplicitSort = true
        };
    }

    public static object ConvertFilterValue(DatasetColumn meta, string? raw)
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
                "date" or "datetime" or "datetime2" or "smalldatetime"
                    => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                "time" => TimeSpan.Parse(value, CultureInfo.InvariantCulture),
                _ => value
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new ReportQueryException($"Filter value '{raw}' is not valid for column '{meta.ColumnName}' ({meta.SqlType}).");
        }
    }

    public static IReadOnlyList<string> SplitList(string? raw)
        => (raw ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public static bool RowMatches(DocumentQueryPlan plan, IReadOnlyDictionary<string, object?> row)
    {
        foreach (var field in plan.Filters)
        {
            row.TryGetValue(field.Column.ColumnName, out var cell);
            var equalValues = new List<string>();
            foreach (var predicate in field.Predicates)
            {
                switch (predicate.Operator)
                {
                    case "eq":
                    case "in":
                        equalValues.AddRange(SplitList(predicate.Value));
                        break;
                    case "isnull":
                        if (cell is not null)
                        {
                            return false;
                        }

                        break;
                    case "isnotnull":
                        if (cell is null)
                        {
                            return false;
                        }

                        break;
                    case "contains":
                        if (cell is null
                            || FormatDistinct(cell).IndexOf(predicate.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            return false;
                        }

                        break;
                    case "andin":
                    {
                        var keys = LookupFilterKeys.Split(predicate.Value);
                        var match = keys.Any(value => CompareValues(cell, ConvertFilterValue(field.Column, value)) == 0);
                        if (!match)
                        {
                            return false;
                        }

                        break;
                    }
                    case "andnin":
                    {
                        if (cell is null)
                        {
                            return false;
                        }

                        var keys = LookupFilterKeys.Split(predicate.Value);
                        if (keys.Any(value => CompareValues(cell, ConvertFilterValue(field.Column, value)) == 0))
                        {
                            return false;
                        }

                        break;
                    }
                    case "nomatch":
                        return false;
                    case "neq":
                    case "gt":
                    case "gte":
                    case "lt":
                    case "lte":
                    {
                        var expected = ConvertFilterValue(field.Column, predicate.Value);
                        var compared = CompareValues(cell, expected);
                        var ok = predicate.Operator switch
                        {
                            "neq" => compared != 0,
                            "gt" => compared > 0,
                            "gte" => compared >= 0,
                            "lt" => compared < 0,
                            "lte" => compared <= 0,
                            _ => false
                        };
                        if (!ok)
                        {
                            return false;
                        }

                        break;
                    }
                    default:
                        throw new ReportQueryException($"Unsupported filter operator '{predicate.Operator}'.");
                }
            }

            if (equalValues.Count > 0)
            {
                var match = equalValues.Any(value => CompareValues(cell, ConvertFilterValue(field.Column, value)) == 0);
                if (!match)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static int CompareProjected(
        DocumentQueryPlan plan,
        IReadOnlyDictionary<string, object?> left,
        IReadOnlyDictionary<string, object?> right)
    {
        foreach (var sort in plan.Sorts)
        {
            left.TryGetValue(sort.Selection.ResultName, out var leftValue);
            right.TryGetValue(sort.Selection.ResultName, out var rightValue);
            var compared = CompareValues(
                sort.LookupText is null ? leftValue : LookupSortResolver.Text(sort.LookupText, leftValue),
                sort.LookupText is null ? rightValue : LookupSortResolver.Text(sort.LookupText, rightValue));
            if (compared != 0)
            {
                return compared * (sort.Descending ? -1 : 1);
            }
        }

        return 0;
    }

    public static Dictionary<string, object?> ProjectRow(
        IReadOnlyDictionary<string, object?> row,
        DocumentQueryPlan plan)
    {
        var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in plan.Selections)
        {
            row.TryGetValue(selection.Column.ColumnName, out var value);
            projected[selection.ResultName] = value;
        }

        return projected;
    }

    public static string FormatDistinct(object? value)
        => value switch
        {
            null => string.Empty,
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

    public static int CompareValues(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (IsNumeric(left) && IsNumeric(right)
            && TryToDecimal(left, out var leftNumber)
            && TryToDecimal(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (IsDate(left) && IsDate(right)
            && TryToDate(left, out var leftDate)
            && TryToDate(right, out var rightDate))
        {
            return leftDate.CompareTo(rightDate);
        }

        return string.Compare(FormatDistinct(left), FormatDistinct(right), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNumericSqlType(string sqlType)
        => ReportQueryBuilder.IsNumericSqlType(sqlType);

    public static bool IsStringSqlType(string sqlType)
    {
        var type = sqlType.ToLowerInvariant();
        return type is "nvarchar" or "varchar" or "nchar" or "char" or "ntext" or "text" or "xml";
    }

    private static void EnsureEnabled(Dataset dataset)
    {
        if (!dataset.IsEnabled)
        {
            throw new ReportQueryException($"Dataset '{dataset.DisplayName}' is not enabled.");
        }
    }

    private static List<PlanFieldFilter> BuildFilters(
        IEnumerable<LayoutFilter> filters,
        IReadOnlyDictionary<string, DatasetColumn> columnsByName)
    {
        var byField = new Dictionary<string, List<PlanPredicate>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter.Field))
            {
                continue;
            }

            var meta = Require(columnsByName, filter.Field, "filter");
            if (!meta.IsFilterable)
            {
                throw new ReportQueryException($"Column '{meta.ColumnName}' cannot be used as a filter.");
            }

            var op = (filter.Operator ?? "eq").Trim().ToLowerInvariant();
            if (op is not "isnull" and not "isnotnull" && string.IsNullOrWhiteSpace(filter.Value))
            {
                continue;
            }

            if (op is "in" or "eq" && SplitList(filter.Value).Count == 0)
            {
                continue;
            }

            if (!byField.TryGetValue(meta.ColumnName, out var list))
            {
                list = [];
                byField[meta.ColumnName] = list;
                order.Add(meta.ColumnName);
            }

            list.Add(new PlanPredicate { Operator = op, Value = filter.Value });
        }

        return order.Select(field => new PlanFieldFilter
        {
            Column = columnsByName[field],
            Predicates = byField[field]
        }).ToList();
    }

    private static List<PlanSort> BuildSorts(
        Dataset dataset,
        ReportLayout layout,
        IReadOnlyList<PlanSelection> selections,
        IReadOnlyDictionary<string, DatasetColumn> columnsByName,
        bool grouping)
    {
        var sorts = new List<PlanSort>();
        var orders = layout.OrderBy.Where(item => !string.IsNullOrWhiteSpace(item.Field)).ToList();
        if (orders.Count == 0 && selections.Count > 0)
        {
            var first = selections[0];
            sorts.Add(new PlanSort
            {
                Selection = first,
                Descending = SortDirections.Normalize(null, first.Column.DefaultSortDirection) == SortDirections.Desc
            });
            return sorts;
        }

        foreach (var order in orders)
        {
            IReadOnlyDictionary<string, string>? lookupText = null;
            var meta = LookupSortResolver.TryOrderColumn(dataset, layout, order.Field, out var fact, out lookupText)
                ? fact
                : Require(columnsByName, order.Field, "sort");
            var selection = selections.FirstOrDefault(item =>
                item.Column.ColumnName.Equals(meta.ColumnName, StringComparison.OrdinalIgnoreCase)
                || item.ResultName.Equals(order.Field, StringComparison.OrdinalIgnoreCase));
            if (selection is null)
            {
                var inGroup = grouping && layout.GroupBy.Any(g =>
                    g.Equals(meta.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (!inGroup)
                {
                    throw new ReportQueryException(
                        $"Cannot sort by '{meta.ColumnName}' because it is not selected, grouped, or aggregated.");
                }

                selection = new PlanSelection { Column = meta, ResultName = meta.ColumnName, Hidden = true };
            }

            sorts.Add(new PlanSort
            {
                Selection = selection,
                Descending = SortDirections.Normalize(order.Direction, meta.DefaultSortDirection) == SortDirections.Desc,
                LookupText = lookupText
            });
        }

        return sorts;
    }

    private static List<PlanSelection> BuildSelections(
        Dataset dataset,
        ReportLayout layout,
        IReadOnlyDictionary<string, DatasetColumn> columnsByName,
        IReadOnlyList<string> groupBy,
        bool grouping)
    {
        var selections = new List<PlanSelection>();
        foreach (var column in layout.Columns)
        {
            var meta = Require(columnsByName, column.Field, "column");
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

            if (aggregate is "SUM" or "AVG" && !ReportQueryBuilder.IsNumericSqlType(meta.SqlType))
            {
                throw new ReportQueryException($"{aggregate} can only be applied to numeric columns (not '{meta.ColumnName}').");
            }

            var resultName = string.IsNullOrWhiteSpace(column.Alias) ? meta.ColumnName : column.Alias.Trim();
            selections.Add(new PlanSelection
            {
                Column = meta,
                Aggregate = aggregate,
                ResultName = resultName,
                Alias = column.Alias,
                Hidden = false
            });
        }

        var selected = new HashSet<string>(selections.Select(item => item.ResultName), StringComparer.OrdinalIgnoreCase);
        foreach (var column in layout.Columns)
        {
            selected.Add(column.Field);
        }

        var list = dataset.Columns.ToList();
        foreach (var token in UrlTokens(layout))
        {
            if (CurrentColumnTokens.Contains(token))
            {
                continue;
            }

            var meta = columnsByName.TryGetValue(token, out var found)
                ? found
                : list.FirstOrDefault(column =>
                    column.ColumnName.Equals(token, StringComparison.OrdinalIgnoreCase)
                    || column.DisplayName.Equals(token, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(column.DisplayNameArabic)
                        && column.DisplayNameArabic.Equals(token, StringComparison.OrdinalIgnoreCase)));
            if (meta is null || selected.Contains(meta.ColumnName) || selected.Contains(token))
            {
                continue;
            }

            if (grouping && !groupBy.Any(g => g.Equals(meta.ColumnName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            selections.Add(new PlanSelection
            {
                Column = meta,
                ResultName = meta.ColumnName,
                Hidden = true
            });
            selected.Add(meta.ColumnName);
        }

        foreach (var order in layout.OrderBy)
        {
            if (string.IsNullOrWhiteSpace(order.Field))
            {
                continue;
            }

            DatasetColumn meta;
            if (LookupSortResolver.TryOrderColumn(dataset, layout, order.Field, out var fact, out _))
            {
                meta = fact;
            }
            else
            {
                meta = Require(columnsByName, order.Field, "sort");
            }

            if (selected.Contains(meta.ColumnName) || selected.Contains(order.Field))
            {
                continue;
            }

            var inGroup = grouping && groupBy.Any(g => g.Equals(meta.ColumnName, StringComparison.OrdinalIgnoreCase));
            if (grouping && !inGroup)
            {
                throw new ReportQueryException(
                    $"Cannot sort by '{meta.ColumnName}' because it is not selected, grouped, or aggregated.");
            }

            selections.Add(new PlanSelection
            {
                Column = meta,
                ResultName = meta.ColumnName,
                Hidden = true
            });
            selected.Add(meta.ColumnName);
        }

        foreach (var field in groupBy)
        {
            var meta = Require(columnsByName, field, "group-by");
            if (!meta.IsGroupable)
            {
                throw new ReportQueryException($"Column '{meta.ColumnName}' cannot be used for grouping.");
            }
        }

        return selections;
    }

    private static List<LayoutFilter> ScopeFiltersForDistinct(string field, IEnumerable<LayoutFilter>? filters)
    {
        if (filters is null)
        {
            return [];
        }

        var list = filters.Where(filter => !string.IsNullOrWhiteSpace(filter.Field)).ToList();
        var ancestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listed = list.FirstOrDefault(filter => filter.Field.Equals(field, StringComparison.OrdinalIgnoreCase));
        var current = listed?.DependsOn;
        while (!string.IsNullOrWhiteSpace(current) && ancestors.Add(current))
        {
            current = list.FirstOrDefault(item => item.Field.Equals(current, StringComparison.OrdinalIgnoreCase))
                ?.DependsOn;
        }

        return list.Where(filter => ancestors.Contains(filter.Field)).ToList();
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

    private static string? NormalizeAggregate(string? aggregate)
    {
        if (string.IsNullOrWhiteSpace(aggregate) || aggregate.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = aggregate.Trim().ToUpperInvariant();
        if (value is not "SUM" and not "COUNT" and not "AVG" and not "MIN" and not "MAX")
        {
            throw new ReportQueryException($"Unsupported aggregate '{aggregate}'.");
        }

        return value;
    }

    private static Dictionary<string, DatasetColumn> VisibleColumns(Dataset dataset)
        => dataset.Columns
            .Where(column => !column.IsHidden)
            .ToDictionary(column => column.ColumnName, column => column, StringComparer.OrdinalIgnoreCase);

    private static DatasetColumn Require(
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

    private static bool IsNumeric(object value)
        => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool IsDate(object value)
        => value is DateTime or DateTimeOffset or DateOnly;

    private static bool TryToDecimal(object? value, out decimal number)
    {
        switch (value)
        {
            case decimal m:
                number = m;
                return true;
            case IConvertible convertible when value is not string and not DateTime and not bool:
                try
                {
                    number = convertible.ToDecimal(CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    break;
                }
        }

        return decimal.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static bool TryToDate(object? value, out DateTime date)
    {
        switch (value)
        {
            case DateTime dt:
                date = dt;
                return true;
            case DateTimeOffset dto:
                date = dto.UtcDateTime;
                return true;
            case DateOnly d:
                date = d.ToDateTime(TimeOnly.MinValue);
                return true;
        }

        return DateTime.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out date);
    }
}
