using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using StackExchange.Redis;
using System.Globalization;
using System.Text.Json;

namespace ReportBuilder.Infrastructure.Cache;

public sealed partial class RedisCacheClient
{
    private const int StreamBatch = 1_000;

    public async Task<ReportResult> QueryReportAsync(
        CacheProviderConnection connection,
        string key,
        Dataset dataset,
        ReportLayout layout,
        int skip,
        int take,
        bool allRows,
        CancellationToken cancellationToken,
        int? maxTake = null)
    {
        var plan = DocumentQueryPlanner.ForReport(dataset, layout, skip, take, allRows, maxTake);
        var (database, rowsKey, columns, rowCount) = await OpenAsync(connection, key, cancellationToken);
        return plan.Grouping
            ? await QueryGroupedAsync(database, rowsKey, columns, plan, cancellationToken)
            : await QueryUngroupedAsync(database, rowsKey, columns, rowCount, plan, cancellationToken);
    }

    public async Task<ColumnValuesResult> QueryDistinctAsync(
        CacheProviderConnection connection,
        string key,
        Dataset dataset,
        string field,
        int take,
        IEnumerable<LayoutFilter>? filters,
        CancellationToken cancellationToken)
    {
        var plan = DocumentQueryPlanner.ForDistinct(dataset, field, take, filters);
        var (database, rowsKey, columns, _) = await OpenAsync(connection, key, cancellationToken);
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var row in StreamRowsAsync(database, rowsKey, columns, cancellationToken))
        {
            if (!DocumentQueryPlanner.RowMatches(plan, row))
            {
                continue;
            }

            row.TryGetValue(plan.Selections[0].Column.ColumnName, out var cell);
            var formatted = DocumentQueryPlanner.FormatDistinct(cell);
            if (string.IsNullOrWhiteSpace(formatted))
            {
                continue;
            }

            unique.Add(formatted);
        }

        var values = unique.ToList();
        values.Sort(StringComparer.OrdinalIgnoreCase);
        if (plan.Sorts is { Count: > 0 } && plan.Sorts[0].Descending)
        {
            values.Reverse();
        }

        var truncated = values.Count > plan.Take;
        if (truncated)
        {
            values.RemoveRange(plan.Take, values.Count - plan.Take);
        }

        return new ColumnValuesResult
        {
            Field = field,
            Values = values,
            Truncated = truncated
        };
    }

    public async Task<ReportExportTable> QueryExportAsync(
        CacheProviderConnection connection,
        string key,
        Dataset dataset,
        ReportLayout layout,
        string reportName,
        ReportExportFormat format,
        string? exportedBy,
        string? lang,
        CancellationToken cancellationToken)
    {
        var allRows = format == ReportExportFormat.Xlsx;
        var cap = format == ReportExportFormat.Csv ? 100_000 : ReportExportLimits.PrintableRowCap;
        var plan = DocumentQueryPlanner.ForReport(
            dataset,
            layout,
            skip: 0,
            take: allRows ? 0 : cap,
            allRows: allRows,
            maxTake: allRows ? null : cap);
        var (database, rowsKey, columns, rowCount) = await OpenAsync(connection, key, cancellationToken);
        var result = plan.Grouping
            ? await QueryGroupedAsync(database, rowsKey, columns, plan, cancellationToken)
            : await QueryUngroupedAsync(database, rowsKey, columns, rowCount, plan, cancellationToken);
        var table = SnapshotQueryExecutor.ToExportTable(result, layout, reportName, format, exportedBy, lang);
        if (!table.Truncated && result.TotalCount > result.Rows.Count)
        {
            return new ReportExportTable
            {
                Title = table.Title,
                Columns = table.Columns,
                ColumnStyles = table.ColumnStyles,
                Rows = table.Rows,
                Truncated = true,
                ExportedBy = table.ExportedBy
            };
        }

        return table;
    }

    private async Task<ReportResult> QueryUngroupedAsync(
        IDatabase database,
        RedisKey rowsKey,
        IReadOnlyList<string> columns,
        long cachedRowCount,
        DocumentQueryPlan plan,
        CancellationToken cancellationToken)
    {
        if (plan.Filters.Count == 0 && !plan.ExplicitSort)
        {
            return await QueryListSliceAsync(database, rowsKey, columns, cachedRowCount, plan, cancellationToken);
        }

        if (!plan.ExplicitSort)
        {
            return await QueryFilteredInOrderAsync(database, rowsKey, columns, plan, cancellationToken);
        }

        return await QuerySortedWindowAsync(database, rowsKey, columns, plan, cancellationToken);
    }

    private async Task<ReportResult> QueryListSliceAsync(
        IDatabase database,
        RedisKey rowsKey,
        IReadOnlyList<string> columns,
        long cachedRowCount,
        DocumentQueryPlan plan,
        CancellationToken cancellationToken)
    {
        var total = await ResolveRowCountAsync(database, rowsKey, cachedRowCount);
        if (plan.AllRows)
        {
            var all = new List<Dictionary<string, object?>>();
            await foreach (var row in StreamRowsAsync(database, rowsKey, columns, cancellationToken))
            {
                all.Add(DocumentQueryPlanner.ProjectRow(row, plan));
            }

            return ToResult(plan, all, total);
        }

        var start = (long)Math.Max(plan.Skip, 0);
        var stop = start + Math.Max(plan.Take, 0) - 1;
        RedisValue[] chunk = stop < start
            ? []
            : await database.ListRangeAsync(rowsKey, start, stop);
        cancellationToken.ThrowIfCancellationRequested();

        var page = new List<Dictionary<string, object?>>(chunk.Length);
        foreach (var value in chunk)
        {
            if (value.IsNullOrEmpty)
            {
                continue;
            }

            page.Add(DocumentQueryPlanner.ProjectRow(ParseRow((string)value!, columns), plan));
        }

        return ToResult(plan, page, total);
    }

    private async Task<ReportResult> QueryFilteredInOrderAsync(
        IDatabase database,
        RedisKey rowsKey,
        IReadOnlyList<string> columns,
        DocumentQueryPlan plan,
        CancellationToken cancellationToken)
    {
        var page = new List<Dictionary<string, object?>>();
        var total = 0;
        var start = plan.AllRows ? 0 : Math.Max(plan.Skip, 0);
        var end = plan.AllRows ? int.MaxValue : start + Math.Max(plan.Take, 0);
        await foreach (var row in StreamRowsAsync(database, rowsKey, columns, cancellationToken))
        {
            if (!DocumentQueryPlanner.RowMatches(plan, row))
            {
                continue;
            }

            if (total >= start && total < end)
            {
                page.Add(DocumentQueryPlanner.ProjectRow(row, plan));
            }

            total++;
        }

        return ToResult(plan, page, total);
    }

    private async Task<ReportResult> QuerySortedWindowAsync(
        IDatabase database,
        RedisKey rowsKey,
        IReadOnlyList<string> columns,
        DocumentQueryPlan plan,
        CancellationToken cancellationToken)
    {
        var window = plan.AllRows ? int.MaxValue : plan.Skip + plan.Take;
        if (window < 0)
        {
            window = plan.Take;
        }

        var best = new List<Dictionary<string, object?>>();
        var total = 0;
        var comparer = Comparer<Dictionary<string, object?>>.Create(
            (left, right) => DocumentQueryPlanner.CompareProjected(plan, left, right));

        await foreach (var row in StreamRowsAsync(database, rowsKey, columns, cancellationToken))
        {
            if (!DocumentQueryPlanner.RowMatches(plan, row))
            {
                continue;
            }

            total++;
            var projected = DocumentQueryPlanner.ProjectRow(row, plan);
            var index = best.BinarySearch(projected, comparer);
            if (index < 0)
            {
                index = ~index;
            }

            best.Insert(index, projected);
            if (best.Count > window)
            {
                best.RemoveAt(best.Count - 1);
            }
        }

        IEnumerable<Dictionary<string, object?>> page = best;
        if (!plan.AllRows)
        {
            page = best.Skip(plan.Skip).Take(plan.Take);
        }

        return ToResult(plan, page.ToList(), total);
    }

    private static async Task<int> ResolveRowCountAsync(IDatabase database, RedisKey rowsKey, long cachedRowCount)
    {
        var count = cachedRowCount > 0 ? cachedRowCount : await database.ListLengthAsync(rowsKey);
        return (int)Math.Min(int.MaxValue, Math.Max(0, count));
    }

    private async Task<ReportResult> QueryGroupedAsync(
        IDatabase database,
        RedisKey rowsKey,
        IReadOnlyList<string> columns,
        DocumentQueryPlan plan,
        CancellationToken cancellationToken)
    {
        var groups = new Dictionary<string, GroupAcc>(StringComparer.Ordinal);
        await foreach (var row in StreamRowsAsync(database, rowsKey, columns, cancellationToken))
        {
            if (!DocumentQueryPlanner.RowMatches(plan, row))
            {
                continue;
            }

            var key = string.Join('\u001f', plan.GroupBy.Select(field =>
            {
                row.TryGetValue(field, out var value);
                return value is null ? "\u0000" : DocumentQueryPlanner.FormatDistinct(value);
            }));
            if (!groups.TryGetValue(key, out var acc))
            {
                acc = GroupAcc.Start(row, plan);
                groups[key] = acc;
            }
            else
            {
                acc.Add(row, plan);
            }
        }

        var projected = groups.Values.Select(acc => acc.Finish(plan)).ToList();
        projected.Sort((left, right) => DocumentQueryPlanner.CompareProjected(plan, left, right));
        var total = projected.Count;
        IEnumerable<Dictionary<string, object?>> page = projected;
        if (!plan.AllRows)
        {
            page = projected.Skip(plan.Skip).Take(plan.Take);
        }

        return ToResult(plan, page.ToList(), total);
    }

    private async Task<(IDatabase Database, RedisKey RowsKey, IReadOnlyList<string> Columns, long RowCount)> OpenAsync(
        CacheProviderConnection connection,
        string key,
        CancellationToken cancellationToken)
    {
        var database = await GetDatabaseAsync(connection, cancellationToken);
        var metaJson = await database.StringGetAsync(key);
        if (metaJson.IsNullOrEmpty)
        {
            throw new ReportQueryException("The Redis dataset cache was not found. Refresh the cache and try again.");
        }

        SnapshotMeta? meta;
        try
        {
            meta = JsonSerializer.Deserialize<SnapshotMeta>((string)metaJson!, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ReportQueryException($"Redis cache metadata is invalid: {ex.Message}");
        }

        return (database, RowsKey(key), meta?.Columns ?? [], meta?.RowCount ?? 0);
    }

    private async IAsyncEnumerable<Dictionary<string, object?>> StreamRowsAsync(
        IDatabase database,
        RedisKey rowsKey,
        IReadOnlyList<string> columns,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var start = 0L;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = await database.ListRangeAsync(rowsKey, start, start + StreamBatch - 1);
            if (chunk.Length == 0)
            {
                yield break;
            }

            foreach (var value in chunk)
            {
                if (value.IsNullOrEmpty)
                {
                    continue;
                }

                yield return ParseRow((string)value!, columns);
            }

            start += chunk.Length;
            if (chunk.Length < StreamBatch)
            {
                yield break;
            }
        }
    }

    private Dictionary<string, object?> ParseRow(string json, IReadOnlyList<string> columns)
    {
        Dictionary<string, JsonElement>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ReportQueryException($"Redis cache row is invalid: {ex.Message}");
        }

        var mapped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (parsed is null)
        {
            return mapped;
        }

        foreach (var column in columns.Count > 0 ? columns : parsed.Keys.ToList())
        {
            mapped[column] = parsed.TryGetValue(column, out var element)
                ? Normalize(element)
                : null;
        }

        return mapped;
    }

    private static ReportResult ToResult(
        DocumentQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        int total)
        => new()
        {
            Columns = plan.Selections.Select(selection => new ReportResultColumn
            {
                Name = selection.ResultName,
                Label = string.IsNullOrWhiteSpace(selection.Alias) ? selection.ResultName : selection.Alias,
                Hidden = selection.Hidden
            }).ToList(),
            Rows = rows.Select(row => (IReadOnlyDictionary<string, object?>)row).ToList(),
            TotalCount = total,
            Skip = plan.Skip,
            Take = plan.AllRows ? total : plan.Take
        };

    private sealed class GroupAcc
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (decimal Sum, long Count, object? Min, object? Max)> _stats = new(StringComparer.OrdinalIgnoreCase);

        public static GroupAcc Start(IReadOnlyDictionary<string, object?> row, DocumentQueryPlan plan)
        {
            var acc = new GroupAcc();
            acc.Add(row, plan, first: true);
            return acc;
        }

        public void Add(IReadOnlyDictionary<string, object?> row, DocumentQueryPlan plan)
            => Add(row, plan, first: false);

        private void Add(IReadOnlyDictionary<string, object?> row, DocumentQueryPlan plan, bool first)
        {
            foreach (var selection in plan.Selections)
            {
                row.TryGetValue(selection.Column.ColumnName, out var cell);
                if (selection.Aggregate is null)
                {
                    if (first)
                    {
                        _values[selection.ResultName] = cell;
                    }

                    continue;
                }

                _stats.TryGetValue(selection.ResultName, out var stat);
                if (cell is null)
                {
                    if (first)
                    {
                        _stats[selection.ResultName] = stat;
                    }

                    continue;
                }

                var number = ToDecimal(cell);
                stat.Count++;
                stat.Sum += number;
                stat.Min = stat.Min is null || DocumentQueryPlanner.CompareValues(cell, stat.Min) < 0 ? cell : stat.Min;
                stat.Max = stat.Max is null || DocumentQueryPlanner.CompareValues(cell, stat.Max) > 0 ? cell : stat.Max;
                _stats[selection.ResultName] = stat;
            }
        }

        public Dictionary<string, object?> Finish(DocumentQueryPlan plan)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var selection in plan.Selections)
            {
                if (selection.Aggregate is null)
                {
                    row[selection.ResultName] = _values.GetValueOrDefault(selection.ResultName);
                    continue;
                }

                _stats.TryGetValue(selection.ResultName, out var stat);
                row[selection.ResultName] = selection.Aggregate switch
                {
                    "COUNT" => (object)stat.Count,
                    "SUM" => stat.Sum,
                    "AVG" => stat.Count == 0 ? null : stat.Sum / stat.Count,
                    "MIN" => stat.Min,
                    "MAX" => stat.Max,
                    _ => null
                };
            }

            return row;
        }

        private static decimal ToDecimal(object value)
            => value is IConvertible convertible && value is not bool
                ? Convert.ToDecimal(convertible, CultureInfo.InvariantCulture)
                : 0;
    }
}
