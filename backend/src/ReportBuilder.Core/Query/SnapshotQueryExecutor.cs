using System.Globalization;
using System.Text.RegularExpressions;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public static class SnapshotQueryExecutor
{
    private static readonly HashSet<string> CurrentColumnTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "field", "value", "this"
    };

    public static ReportResult Execute(
        DatasetCacheSnapshot snapshot,
        Dataset dataset,
        ReportLayout layout,
        int skip,
        int take,
        bool allRows = false)
    {
        if (!dataset.IsEnabled)
        {
            throw new ReportQueryException($"Dataset '{dataset.DisplayName}' is not enabled.");
        }

        if (layout.Columns.Count == 0)
        {
            throw new ReportQueryException("Select at least one column.");
        }

        if (!allRows)
        {
            take = take <= 0 ? ReportQueryBuilder.DefaultTake : Math.Min(take, ReportQueryBuilder.MaxTake);
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
        var filtered = snapshot.Rows.Where(row => MatchesFilters(row, layout.Filters, columnsByName)).ToList();
        var projected = grouping
            ? GroupRows(filtered, selections, groupBy)
            : filtered.Select(row => ProjectRow(row, selections)).ToList();

        projected.Sort((left, right) => CompareRows(dataset, left, right, layout, selections, columnsByName, grouping));

        var total = projected.Count;
        IEnumerable<Dictionary<string, object?>> page = projected;
        if (!allRows)
        {
            page = projected.Skip(skip).Take(take);
        }

        var columns = selections.Select(item => new ReportResultColumn
        {
            Name = item.ResultName,
            Label = string.IsNullOrWhiteSpace(item.Alias) ? item.ResultName : item.Alias,
            Hidden = item.Hidden
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = page.Select(row => (IReadOnlyDictionary<string, object?>)row).ToList(),
            TotalCount = total,
            Skip = skip,
            Take = allRows ? total : take
        };
    }

    public static ColumnValuesResult DistinctValues(
        DatasetCacheSnapshot snapshot,
        Dataset dataset,
        string field,
        int take,
        IEnumerable<LayoutFilter>? filters = null)
    {
        take = take <= 0 ? ReportQueryBuilder.DefaultDistinctTake : Math.Min(take, ReportQueryBuilder.MaxDistinctTake);
        var columnsByName = VisibleColumns(dataset);
        if (!columnsByName.TryGetValue(field, out var meta))
        {
            throw new ReportQueryException($"Unknown or hidden filter values field '{field}'.");
        }

        if (!meta.IsFilterable)
        {
            throw new ReportQueryException($"Column '{meta.ColumnName}' cannot be used as a filter.");
        }

        var scoped = ScopeFiltersForDistinct(field, filters);
        var values = new List<string>();
        foreach (var row in snapshot.Rows)
        {
            if (!MatchesFilters(row, scoped, columnsByName))
            {
                continue;
            }

            var cell = GetCell(row, meta.ColumnName);
            if (cell is null)
            {
                continue;
            }

            var formatted = FormatDistinct(cell);
            if (string.IsNullOrWhiteSpace(formatted)
                || values.Any(existing => existing.Equals(formatted, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            values.Add(formatted);
        }

        var desc = SortDirections.Normalize(null, meta.DefaultSortDirection) == SortDirections.Desc;
        values.Sort(StringComparer.OrdinalIgnoreCase);
        if (desc)
        {
            values.Reverse();
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

    public static ReportExportTable ToExportTable(
        ReportResult result,
        ReportLayout layout,
        string reportName,
        ReportExportFormat format,
        string? exportedBy,
        string? lang = null)
    {
        var include = result.Columns
            .Select((column, index) => (column, index))
            .Where(item => !item.column.Hidden && LayoutColumnLocales.ShouldInclude(item.index, layout, lang))
            .ToList();
        var rowCap = ReportExportLimits.RowCapFor(format);
        var rows = new List<IReadOnlyList<string>>();
        var truncated = false;
        foreach (var row in result.Rows)
        {
            if (rowCap is int cap && rows.Count >= cap)
            {
                truncated = true;
                break;
            }

            var cells = new string[include.Count];
            for (var i = 0; i < include.Count; i++)
            {
                var name = include[i].column.Name;
                var layoutColumn = include[i].index < layout.Columns.Count ? layout.Columns[include[i].index] : null;
                cells[i] = ColumnValueFormatter.FormatLayout(row.GetValueOrDefault(name), layoutColumn?.Format, layoutColumn, name);
            }

            rows.Add(cells);
        }

        return new ReportExportTable
        {
            Title = string.IsNullOrWhiteSpace(reportName) ? "Report" : reportName.Trim(),
            Columns = include.Select(item => item.column.Name).ToArray(),
            ColumnStyles = ExportColumnStyleBuilder.For(include.Select(item => item.index).ToArray(), layout, lang),
            Rows = rows,
            Truncated = truncated,
            ExportedBy = exportedBy
        };
    }

    private static List<Selection> BuildSelections(
        Dataset dataset,
        ReportLayout layout,
        IReadOnlyDictionary<string, DatasetColumn> columnsByName,
        IReadOnlyList<string> groupBy,
        bool grouping)
    {
        var selections = new List<Selection>();
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
            selections.Add(new Selection(meta, aggregate, resultName, column.Alias, Hidden: false));
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

            selections.Add(new Selection(meta, null, meta.ColumnName, null, Hidden: true));
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

            selections.Add(new Selection(meta, null, meta.ColumnName, null, Hidden: true));
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

    private static List<Dictionary<string, object?>> GroupRows(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<Selection> selections,
        IReadOnlyList<string> groupBy)
    {
        var groups = new Dictionary<string, List<IReadOnlyDictionary<string, object?>>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = string.Join('\u001f', groupBy.Select(field => FormatKey(GetCell(row, field))));
            if (!groups.TryGetValue(key, out var bucket))
            {
                bucket = [];
                groups[key] = bucket;
            }

            bucket.Add(row);
        }

        var result = new List<Dictionary<string, object?>>(groups.Count);
        foreach (var bucket in groups.Values)
        {
            var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var selection in selections)
            {
                projected[selection.ResultName] = selection.Aggregate is null
                    ? GetCell(bucket[0], selection.Column.ColumnName)
                    : Aggregate(bucket, selection);
            }

            result.Add(projected);
        }

        return result;
    }

    private static Dictionary<string, object?> ProjectRow(
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyList<Selection> selections)
    {
        var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in selections)
        {
            projected[selection.ResultName] = GetCell(row, selection.Column.ColumnName);
        }

        return projected;
    }

    private static object? Aggregate(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, Selection selection)
    {
        var values = rows.Select(row => GetCell(row, selection.Column.ColumnName)).Where(value => value is not null).ToList();
        return selection.Aggregate switch
        {
            "COUNT" => values.Count,
            "SUM" => values.Select(ToDecimal).Sum(),
            "AVG" => values.Count == 0 ? null : values.Select(ToDecimal).Average(),
            "MIN" => values.Count == 0 ? null : Min(values),
            "MAX" => values.Count == 0 ? null : Max(values),
            _ => null
        };
    }

    private static object? Min(List<object?> values)
    {
        var best = values[0];
        for (var i = 1; i < values.Count; i++)
        {
            if (CompareValues(values[i], best) < 0)
            {
                best = values[i];
            }
        }

        return best;
    }

    private static object? Max(List<object?> values)
    {
        var best = values[0];
        for (var i = 1; i < values.Count; i++)
        {
            if (CompareValues(values[i], best) > 0)
            {
                best = values[i];
            }
        }

        return best;
    }

    private static int CompareRows(
        Dataset dataset,
        Dictionary<string, object?> left,
        Dictionary<string, object?> right,
        ReportLayout layout,
        IReadOnlyList<Selection> selections,
        IReadOnlyDictionary<string, DatasetColumn> columnsByName,
        bool grouping)
    {
        var orders = layout.OrderBy.Where(item => !string.IsNullOrWhiteSpace(item.Field)).ToList();
        if (orders.Count == 0 && selections.Count > 0)
        {
            var first = selections[0];
            var desc = SortDirections.Normalize(null, first.Column.DefaultSortDirection) == SortDirections.Desc;
            return CompareValues(left.GetValueOrDefault(first.ResultName), right.GetValueOrDefault(first.ResultName))
                * (desc ? -1 : 1);
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
            if (selection.Column is null)
            {
                var inGroup = grouping && layout.GroupBy.Any(g =>
                    g.Equals(meta.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (!inGroup)
                {
                    throw new ReportQueryException(
                        $"Cannot sort by '{meta.ColumnName}' because it is not selected, grouped, or aggregated.");
                }

                selection = new Selection(meta, null, meta.ColumnName, null, Hidden: true);
            }

            var desc = SortDirections.Normalize(order.Direction, meta.DefaultSortDirection) == SortDirections.Desc;
            var leftValue = left.GetValueOrDefault(selection.ResultName);
            var rightValue = right.GetValueOrDefault(selection.ResultName);
            var compared = CompareValues(
                lookupText is null ? leftValue : LookupSortResolver.Text(lookupText, leftValue),
                lookupText is null ? rightValue : LookupSortResolver.Text(lookupText, rightValue));
            if (compared != 0)
            {
                return compared * (desc ? -1 : 1);
            }
        }

        return 0;
    }

    private static bool MatchesFilters(
        IReadOnlyDictionary<string, object?> row,
        IEnumerable<LayoutFilter> filters,
        IReadOnlyDictionary<string, DatasetColumn> columnsByName)
    {
        var byField = new Dictionary<string, List<(string Op, string? Value)>>(StringComparer.OrdinalIgnoreCase);
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
            }

            list.Add((op, filter.Value));
        }

        foreach (var (field, ops) in byField)
        {
            var meta = columnsByName[field];
            var cell = GetCell(row, field);
            var equalValues = new List<string>();
            foreach (var (op, raw) in ops)
            {
                switch (op)
                {
                    case "eq":
                    case "in":
                        equalValues.AddRange(SplitList(raw));
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
                            || FormatDistinct(cell).IndexOf(raw ?? string.Empty, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            return false;
                        }

                        break;
                    case "andin":
                    {
                        var keys = LookupFilterKeys.Split(raw);
                        if (!keys.Any(value => CompareValues(cell, ConvertFilterValue(meta, value)) == 0))
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

                        var keys = LookupFilterKeys.Split(raw);
                        if (keys.Any(value => CompareValues(cell, ConvertFilterValue(meta, value)) == 0))
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
                        var expected = ConvertFilterValue(meta, raw);
                        var compared = CompareValues(cell, expected);
                        var ok = op switch
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
                        throw new ReportQueryException($"Unsupported filter operator '{op}'.");
                }
            }

            if (equalValues.Count > 0)
            {
                var match = equalValues.Any(value => CompareValues(cell, ConvertFilterValue(meta, value)) == 0);
                if (!match)
                {
                    return false;
                }
            }
        }

        return true;
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

    private static object? GetCell(IReadOnlyDictionary<string, object?> row, string field)
        => row.TryGetValue(field, out var value) ? value : null;

    private static List<string> SplitList(string? raw)
        => (raw ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

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

    private static int CompareValues(object? left, object? right)
    {
        try
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
        catch (Exception)
        {
            return string.Compare(
                Convert.ToString(left, CultureInfo.InvariantCulture),
                Convert.ToString(right, CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool IsNumeric(object value)
        => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool IsDate(object value)
        => value is DateTime or DateTimeOffset or DateOnly;

    private static decimal ToDecimal(object? value)
        => TryToDecimal(value, out var number) ? number : 0;

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

        return decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out number);
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

        return DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date);
    }

    private static string FormatKey(object? value)
        => value is null ? "\u0000" : FormatDistinct(value);

    private static string FormatDistinct(object value) =>
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

    private readonly record struct Selection(
        DatasetColumn Column,
        string? Aggregate,
        string ResultName,
        string? Alias,
        bool Hidden);
}

public sealed class DatasetCacheSnapshot
{
    public IReadOnlyList<string> Columns { get; init; } = [];
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];
}
