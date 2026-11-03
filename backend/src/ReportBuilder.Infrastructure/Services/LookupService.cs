using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

public sealed partial class LookupService : ILookupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ReportBuilderDbContext _db;

    public LookupService(ReportBuilderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Lookup>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.Lookups.AsNoTracking()
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LookupObjectInfo>> ListObjectsAsync(
        string schema,
        CancellationToken cancellationToken = default)
    {
        var schemaName = schema.Trim();
        if (schemaName.Length == 0)
        {
            return [];
        }

        const string sql = """
            SELECT s.name, o.name, CASE WHEN o.type = 'V' THEN 'View' ELSE 'Table' END
            FROM sys.objects o
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('U', 'V')
              AND o.is_ms_shipped = 0
              AND s.name = @schema
              AND o.name NOT IN (N'__EFMigrationsHistory')
            ORDER BY o.name
            """;

        var results = new List<LookupObjectInfo>();
        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new SqlParameter("@schema", schemaName));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new LookupObjectInfo
                {
                    SchemaName = reader.GetString(0),
                    ObjectName = reader.GetString(1),
                    ObjectType = reader.GetString(2)
                });
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<LookupColumnInfo>> PreviewColumnsAsync(
        string schema,
        string objectName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(objectName))
        {
            return [];
        }

        var probed = await ProbeAsync(new LookupWriteRequest
        {
            SourceKind = LookupSourceKind.Table,
            SchemaName = schema.Trim(),
            ObjectName = objectName.Trim()
        }, cancellationToken);
        foreach (var column in probed.Columns)
        {
            column.Included = false;
            column.ShowInDesigner = false;
        }

        return probed.Columns;
    }

    public async Task<IReadOnlyList<LookupColumnInfo>> PreviewQueryColumnsAsync(
        string sql,
        CancellationToken cancellationToken = default)
    {
        var probed = await ProbeAsync(new LookupWriteRequest
        {
            SourceKind = LookupSourceKind.Query,
            SqlText = sql
        }, cancellationToken);
        foreach (var column in probed.Columns)
        {
            column.Included = false;
            column.ShowInDesigner = false;
        }

        return probed.Columns;
    }

    public async Task<Lookup> SaveAsync(LookupWriteRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length < 2)
        {
            throw new ReportQueryException("Lookup name is required.");
        }

        var key = request.KeyColumn?.Trim() ?? string.Empty;
        var label = request.LabelColumn?.Trim() ?? string.Empty;
        if (key.Length == 0 || label.Length == 0)
        {
            throw new ReportQueryException("Map a key column and a label column.");
        }

        var probed = await ProbeAsync(request, cancellationToken);
        var columns = probed.Columns;
        RequireMapped(columns, key, "key");
        RequireMapped(columns, label, "label");
        var sort = string.IsNullOrWhiteSpace(request.SortColumn) ? label : request.SortColumn.Trim();
        RequireMapped(columns, sort, "sort");

        var now = DateTimeOffset.UtcNow;
        var lookup = request.Id is Guid id
            ? await _db.Lookups.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new ReportQueryException("Lookup was not found.")
            : new Lookup { Id = Guid.NewGuid(), CreatedAt = now };

        if (request.Id is null)
        {
            _db.Lookups.Add(lookup);
        }

        lookup.Name = name;
        lookup.NameArabic = string.IsNullOrWhiteSpace(request.NameArabic) ? null : request.NameArabic.Trim();
        lookup.SourceKind = request.SourceKind;
        lookup.SchemaName = probed.SchemaName;
        lookup.ObjectName = probed.ObjectName;
        lookup.SqlText = probed.SqlText;
        lookup.KeyColumn = MatchName(columns, key);
        lookup.LabelColumn = MatchName(columns, label);
        lookup.SortColumn = MatchName(columns, sort);
        lookup.SortDirection = SortDirections.Normalize(request.SortDirection, SortDirections.Asc);
        lookup.IsValid = true;
        lookup.ValidationError = null;
        ApplyColumnSettings(columns, request.ColumnLocales, lookup.ColumnsJson, key, sort);
        lookup.ColumnsJson = JsonSerializer.Serialize(columns, JsonOptions);
        lookup.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return lookup;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var lookup = await _db.Lookups.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ReportQueryException("Lookup was not found.");
        var used = await _db.DatasetColumns.AsNoTracking()
            .Where(column => column.LookupId == id)
            .Select(column => new
            {
                DatasetName = column.Dataset.DisplayName,
                DatasetNameArabic = column.Dataset.DisplayNameArabic,
                ColumnLabel = column.DisplayName,
                column.ColumnName
            })
            .ToListAsync(cancellationToken);
        if (used.Count > 0)
        {
            var datasets = used
                .GroupBy(column => column.DatasetName + "\u001f" + (column.DatasetNameArabic ?? string.Empty))
                .Select(group =>
                {
                    var sample = group.First();
                    var arabic = sample.DatasetNameArabic?.Trim();
                    var name = string.IsNullOrWhiteSpace(arabic)
                        || arabic.Equals(sample.DatasetName, StringComparison.OrdinalIgnoreCase)
                        ? sample.DatasetName
                        : sample.DatasetName + " / " + arabic;
                    var columns = string.Join(
                        ", ",
                        group.Select(column =>
                            string.IsNullOrWhiteSpace(column.ColumnLabel) ? column.ColumnName : column.ColumnLabel)
                            .Distinct(StringComparer.OrdinalIgnoreCase));
                    return name + " (" + columns + ")";
                })
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase);
            throw new ReportQueryException("LOOKUP_IN_USE|" + string.Join("; ", datasets));
        }

        _db.Lookups.Remove(lookup);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ColumnValuesResult?> TryListValuesAsync(
        Guid lookupId,
        int take,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? restrictToKeys = null,
        bool sourceTruncated = false)
    {
        var lookup = await _db.Lookups.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == lookupId, cancellationToken);
        if (lookup is null || !lookup.IsValid)
        {
            return null;
        }

        return await ReadLookupChoicesAsync(
            lookup,
            lookup.KeyColumn,
            lookup.LabelColumn,
            take,
            restrictToKeys,
            sourceTruncated,
            cancellationToken);
    }

    public async Task<IReadOnlyList<LayoutFilter>> RewriteFiltersAsync(
        Dataset dataset,
        IReadOnlyList<LayoutFilter> filters,
        CancellationToken cancellationToken = default)
    {
        if (filters.Count == 0 || !filters.Any(filter => LookupFields.TryParse(filter.Field, out _, out _)))
        {
            return filters;
        }

        var plain = new List<LayoutFilter>();
        var groups = new Dictionary<string, LookupFilterGroup>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var filter in filters)
        {
            if (!LookupColumnPlan.TryResolve(dataset, filter.Field, out var fact, out var lookupColumn))
            {
                if (LookupFields.TryParse(filter.Field, out _, out _))
                {
                    throw new ReportQueryException($"Unknown or hidden field '{filter.Field}'.");
                }

                plain.Add(filter);
                continue;
            }

            if (!fact.IsFilterable)
            {
                throw new ReportQueryException($"Column '{fact.ColumnName}' cannot be used as a filter.");
            }

            var key = fact.Id.ToString("D") + ":" + lookupColumn;
            if (!groups.TryGetValue(key, out var group))
            {
                group = new LookupFilterGroup(fact, fact.Lookup!, lookupColumn);
                groups[key] = group;
                order.Add(key);
            }

            group.Filters.Add(filter);
        }

        foreach (var key in order)
        {
            plain.AddRange(await RewriteGroupAsync(groups[key], cancellationToken));
        }

        return plain;
    }

    public async Task AttachSortsAsync(
        Dataset dataset,
        ReportLayout layout,
        CancellationToken cancellationToken = default)
    {
        layout.LookupSorts = [];
        foreach (var order in layout.OrderBy)
        {
            if (string.IsNullOrWhiteSpace(order.Field) || !LookupFields.TryParse(order.Field, out _, out _))
            {
                continue;
            }

            if (!LookupColumnPlan.TryResolve(dataset, order.Field, out var fact, out var lookupColumn))
            {
                throw new ReportQueryException($"Unknown or hidden sort field '{order.Field}'.");
            }

            layout.LookupSorts.Add(new LookupSort
            {
                Field = order.Field,
                FactColumn = fact.ColumnName,
                TextByKey = await LoadSortTextAsync(fact.Lookup!, lookupColumn, cancellationToken)
            });
        }
    }

    public async Task<ColumnValuesResult?> TryListLookupColumnValuesAsync(
        Dataset dataset,
        string field,
        int take,
        string? lang = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? restrictToKeys = null,
        bool sourceTruncated = false)
    {
        if (!LookupColumnPlan.TryResolve(dataset, field, out var fact, out var lookupColumn))
        {
            return null;
        }

        var lookup = fact.Lookup!;
        return await ReadLookupChoicesAsync(
            lookup,
            field,
            LabelColumn(lookup, lookupColumn, lang),
            take,
            restrictToKeys,
            sourceTruncated,
            cancellationToken);
    }

    private async Task<ColumnValuesResult> ReadLookupChoicesAsync(
        Lookup lookup,
        string field,
        string labelName,
        int take,
        IReadOnlyList<string>? restrictToKeys,
        bool sourceTruncated,
        CancellationToken cancellationToken)
    {
        take = take <= 0 ? ReportQueryBuilder.DefaultDistinctTake : Math.Min(take, ReportQueryBuilder.MaxDistinctTake);
        if (restrictToKeys is { Count: 0 })
        {
            return new ColumnValuesResult
            {
                Field = field,
                Values = [],
                Choices = [],
                Truncated = sourceTruncated
            };
        }

        var key = SqlIdentifiers.Quote(lookup.KeyColumn);
        var label = SqlIdentifiers.Quote(labelName);
        var sortName = string.IsNullOrWhiteSpace(lookup.SortColumn) ? lookup.LabelColumn : lookup.SortColumn;
        var sort = SqlIdentifiers.Quote(sortName);
        var direction = SortDirections.ToSql(lookup.SortDirection, SortDirections.Asc);
        var keyText = $"CONVERT(nvarchar(4000), {key})";
        var restriction = "1 = 1";
        var parameters = new List<SqlParameter>();
        if (restrictToKeys is { Count: > 0 })
        {
            var names = new string[restrictToKeys.Count];
            for (var index = 0; index < restrictToKeys.Count; index++)
            {
                var name = "@lk" + index;
                names[index] = name;
                parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, 4000) { Value = restrictToKeys[index] });
            }

            restriction = $"{keyText} IN ({string.Join(", ", names)})";
        }

        var sql = $"""
            SELECT DISTINCT TOP ({take + 1})
                {keyText} AS [value],
                CONVERT(nvarchar(4000), {label}) AS [label],
                CONVERT(nvarchar(4000), {sort}) AS [sortKey]
            FROM {FromSql(lookup)}
            WHERE {key} IS NOT NULL AND {keyText} <> N'' AND ({restriction})
            ORDER BY [sortKey] {direction}, [label] ASC
            """;

        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 30;
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var choices = new List<ColumnValueChoice>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var value = reader.GetString(0).Trim();
                if (value.Length == 0 || !seen.Add(value))
                {
                    continue;
                }

                var text = reader.IsDBNull(1) ? value : reader.GetString(1).Trim();
                choices.Add(new ColumnValueChoice
                {
                    Value = value,
                    Label = text.Length == 0 ? value : text
                });
            }

            var truncated = sourceTruncated || choices.Count > take;
            if (choices.Count > take)
            {
                choices.RemoveRange(take, choices.Count - take);
            }

            return new ColumnValuesResult
            {
                Field = field,
                Values = choices.Select(item => item.Value).ToList(),
                Choices = choices,
                Truncated = truncated
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

    private static string LabelColumn(Lookup lookup, string placedColumn, string? lang)
    {
        var locale = LayoutColumnLocales.NormalizeLang(lang);
        if (locale is null)
        {
            return LookupFields.IsControl(placedColumn)
                ? FirstLocaleColumn(lookup, LayoutColumnLocales.English)
                    ?? FirstLocaleColumn(lookup, LayoutColumnLocales.Arabic)
                    ?? FallbackTextColumn(lookup)
                : placedColumn;
        }

        return FirstLocaleColumn(lookup, locale)
            ?? (LookupFields.IsControl(placedColumn) ? FallbackTextColumn(lookup) : placedColumn);
    }

    private static string? FirstLocaleColumn(Lookup lookup, string locale)
    {
        foreach (var column in ReadStoredColumns(lookup.ColumnsJson).Values)
        {
            if (column.Included == false || string.IsNullOrWhiteSpace(column.Name))
            {
                continue;
            }

            if (LayoutColumnLocales.Normalize(column.Locale) == locale)
            {
                return column.Name;
            }
        }

        return null;
    }

    private static string FallbackTextColumn(Lookup lookup)
        => string.IsNullOrWhiteSpace(lookup.LabelColumn) ? lookup.KeyColumn : lookup.LabelColumn;

    private async Task<IReadOnlyList<LayoutFilter>> RewriteGroupAsync(
        LookupFilterGroup group,
        CancellationToken cancellationToken)
    {
        var equals = new List<string>();
        var extras = new List<LayoutFilter>();
        foreach (var filter in group.Filters)
        {
            var op = (filter.Operator ?? "eq").Trim().ToLowerInvariant();
            if (op is "eq" or "in")
            {
                equals.AddRange(DocumentQueryPlanner.SplitList(filter.Value));
            }
            else if (op is "isnull" or "isnotnull" || !string.IsNullOrWhiteSpace(filter.Value))
            {
                extras.Add(filter);
            }
        }

        var rewritten = new List<LayoutFilter>();
        if (equals.Count > 0)
        {
            var keys = await SelectKeysAsync(group.Lookup, group.LookupColumn, "in", equals, cancellationToken, matchKey: true);
            rewritten.Add(KeyFilter(group.Fact, keys, exclude: false));
        }

        foreach (var filter in extras)
        {
            var op = (filter.Operator ?? "eq").Trim().ToLowerInvariant();
            if (op == "neq")
            {
                var keys = await SelectKeysAsync(
                    group.Lookup,
                    group.LookupColumn,
                    "in",
                    DocumentQueryPlanner.SplitList(filter.Value),
                    cancellationToken,
                    matchKey: true);
                if (keys.Count > 0)
                {
                    rewritten.Add(KeyFilter(group.Fact, keys, exclude: true));
                }

                continue;
            }

            var matched = await SelectKeysAsync(
                group.Lookup,
                group.LookupColumn,
                op,
                [filter.Value ?? string.Empty],
                cancellationToken);
            rewritten.Add(KeyFilter(group.Fact, matched, exclude: false));
        }

        return rewritten;
    }

    private static LayoutFilter KeyFilter(DatasetColumn fact, IReadOnlyList<string> keys, bool exclude)
    {
        if (!exclude && keys.Count == 0)
        {
            return new LayoutFilter
            {
                Field = fact.ColumnName,
                Operator = LookupFilterKeys.NoMatch,
                Value = "1"
            };
        }

        return new LayoutFilter
        {
            Field = fact.ColumnName,
            Operator = exclude ? LookupFilterKeys.AndNotIn : LookupFilterKeys.AndIn,
            Value = LookupFilterKeys.Join(keys)
        };
    }

    private async Task<List<string>> SelectKeysAsync(
        Lookup lookup,
        string lookupColumn,
        string op,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken,
        bool matchKey = false)
    {
        if (LookupFields.IsControl(lookupColumn))
        {
            lookupColumn = FallbackTextColumn(lookup);
        }

        var column = SqlIdentifiers.Quote(lookupColumn);
        var text = $"CONVERT(nvarchar(4000), {column})";
        var key = SqlIdentifiers.Quote(lookup.KeyColumn);
        var keyText = $"CONVERT(nvarchar(4000), {key})";
        var parameters = new List<SqlParameter>();
        string where = op switch
        {
            "isnull" => $"{column} IS NULL OR {text} = N''",
            "isnotnull" => $"{column} IS NOT NULL AND {text} <> N''",
            "contains" => ContainsLocalized(lookup, lookupColumn, values, parameters),
            "gt" or "gte" or "lt" or "lte" => Compare(column, op, values, parameters),
            _ => matchKey ? KeyOrText(keyText, text, lookup, values, parameters) : InList(text, values, parameters)
        };

        var sql = $"""
            SELECT DISTINCT TOP ({LookupFilterKeys.MaxKeys + 1}) CONVERT(nvarchar(4000), {key}) AS [value]
            FROM {FromSql(lookup)}
            WHERE {key} IS NOT NULL AND CONVERT(nvarchar(4000), {key}) <> N'' AND ({where})
            """;
        var keys = (await ReadStringsAsync(sql, parameters, cancellationToken))
            .Select(NormalizeKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keys.Count > LookupFilterKeys.MaxKeys)
        {
            throw new ReportQueryException(
                $"Lookup filter on '{lookupColumn}' matches more than {LookupFilterKeys.MaxKeys} keys. Narrow the filter and try again.");
        }

        return keys;
    }

    private static string KeyOrText(
        string keyText,
        string placedText,
        Lookup lookup,
        IReadOnlyList<string> values,
        List<SqlParameter> parameters)
    {
        if (values.Count == 0)
        {
            return "1 = 0";
        }

        var names = new List<string>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var name = "@v" + index;
            names.Add(name);
            parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, 4000) { Value = values[index] });
        }

        var list = string.Join(", ", names);
        var clauses = new List<string> { $"{keyText} IN ({list})", $"{placedText} IN ({list})" };
        foreach (var columnName in LocaleColumns(lookup))
        {
            var columnText = $"CONVERT(nvarchar(4000), {SqlIdentifiers.Quote(columnName)})";
            if (!columnText.Equals(placedText, StringComparison.OrdinalIgnoreCase)
                && !columnText.Equals(keyText, StringComparison.OrdinalIgnoreCase))
            {
                clauses.Add($"{columnText} IN ({list})");
            }
        }

        return string.Join(" OR ", clauses);
    }

    private static string ContainsLocalized(
        Lookup lookup,
        string placedColumn,
        IReadOnlyList<string> values,
        List<SqlParameter> parameters)
    {
        var needle = values.Count == 0 ? string.Empty : values[0];
        parameters.Add(new SqlParameter("@v0", SqlDbType.NVarChar, 4000)
        {
            Value = "%" + needle.Replace("[", "[[]", StringComparison.Ordinal)
                .Replace("%", "[%]", StringComparison.Ordinal)
                .Replace("_", "[_]", StringComparison.Ordinal) + "%"
        });
        parameters.Add(new SqlParameter("@k0", SqlDbType.NVarChar, 4000) { Value = needle });

        var names = new List<string> { placedColumn };
        names.AddRange(LocaleColumns(lookup));
        var clauses = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => $"CONVERT(nvarchar(4000), {SqlIdentifiers.Quote(name)}) LIKE @v0")
            .ToList();
        var key = SqlIdentifiers.Quote(lookup.KeyColumn);
        clauses.Add($"CONVERT(nvarchar(4000), {key}) = @k0");
        return string.Join(" OR ", clauses);
    }

    private static IEnumerable<string> LocaleColumns(Lookup lookup)
    {
        foreach (var column in ReadStoredColumns(lookup.ColumnsJson).Values)
        {
            if (column.Included == false || string.IsNullOrWhiteSpace(column.Name))
            {
                continue;
            }

            var locale = LayoutColumnLocales.Normalize(column.Locale);
            if (locale is LayoutColumnLocales.English or LayoutColumnLocales.Arabic)
            {
                yield return column.Name;
            }
        }
    }

    private static string InList(string text, IReadOnlyList<string> values, List<SqlParameter> parameters)
    {
        if (values.Count == 0)
        {
            return "1 = 0";
        }

        var names = new List<string>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var name = "@v" + index;
            names.Add(name);
            parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, 4000) { Value = values[index] });
        }

        return $"{text} IN ({string.Join(", ", names)})";
    }

    private static string Compare(string column, string op, IReadOnlyList<string> values, List<SqlParameter> parameters)
    {
        var sqlOp = op switch
        {
            "gt" => ">",
            "gte" => ">=",
            "lt" => "<",
            "lte" => "<=",
            _ => "="
        };
        parameters.Add(new SqlParameter("@v0", SqlDbType.NVarChar, 4000)
        {
            Value = values.Count == 0 ? string.Empty : values[0]
        });
        var typed = $"TRY_CONVERT(float, {column})";
        var text = $"CONVERT(nvarchar(4000), {column})";
        return $"({typed} IS NOT NULL AND {typed} {sqlOp} TRY_CONVERT(float, @v0)) OR ({typed} IS NULL AND {text} {sqlOp} @v0)";
    }

    private async Task<Dictionary<string, string>> LoadSortTextAsync(
        Lookup lookup,
        string lookupColumn,
        CancellationToken cancellationToken)
    {
        var key = SqlIdentifiers.Quote(lookup.KeyColumn);
        var sortName = string.IsNullOrWhiteSpace(lookup.SortColumn) ? lookupColumn : lookup.SortColumn.Trim();
        var column = SqlIdentifiers.Quote(sortName);
        var sql = $"""
            SELECT DISTINCT
                CONVERT(nvarchar(4000), {key}) AS [k],
                CONVERT(nvarchar(4000), {column}) AS [v]
            FROM {FromSql(lookup)}
            WHERE {key} IS NOT NULL AND CONVERT(nvarchar(4000), {key}) <> N''
            """;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 30;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var sortKey = NormalizeKey(reader.GetValue(0));
                if (sortKey.Length == 0)
                {
                    continue;
                }

                var text = reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty;
                if (!map.ContainsKey(sortKey))
                {
                    map[sortKey] = text;
                }

                if (map.Count > LookupSortResolver.MaxKeys)
                {
                    throw new ReportQueryException(
                        $"Lookup sort on '{lookupColumn}' has more than {LookupSortResolver.MaxKeys} keys.");
                }
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        LookupSortResolver.UseSortableValues(map);
        return map;
    }

    private async Task<List<string>> ReadStringsAsync(
        string sql,
        IReadOnlyList<SqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        var values = new List<string>();
        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 30;
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var value = reader.GetString(0).Trim();
                if (value.Length > 0)
                {
                    values.Add(value);
                }
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        return values;
    }

    private sealed class LookupFilterGroup(DatasetColumn fact, Lookup lookup, string lookupColumn)
    {
        public DatasetColumn Fact { get; } = fact;
        public Lookup Lookup { get; } = lookup;
        public string LookupColumn { get; } = lookupColumn;
        public List<LayoutFilter> Filters { get; } = [];
    }

    public async Task<ReportResult> ApplyReportColumnsAsync(
        ReportResult result,
        LookupColumnPlan plan,
        string? lang = null,
        CancellationToken cancellationToken = default)
    {
        if (!plan.HasLookups)
        {
            return result;
        }

        var maps = new Dictionary<Guid, Dictionary<string, Dictionary<string, string?>>>();
        foreach (var group in plan.Bindings.GroupBy(binding => binding.Fact.LookupId!.Value))
        {
            var lookup = group.First().Fact.Lookup
                ?? throw new ReportQueryException("Lookup is no longer available.");
            var lookupColumnNames = group
                .SelectMany(binding => ColumnsForBinding(lookup, binding.LookupColumn))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in group)
            {
                var keyName = FactResultName(result, plan, binding.Fact.ColumnName);
                if (keyName is null)
                {
                    continue;
                }

                foreach (var row in result.Rows)
                {
                    if (!row.TryGetValue(keyName, out var raw) || raw is null)
                    {
                        continue;
                    }

                    var key = NormalizeKey(raw);
                    if (key.Length > 0)
                    {
                        keys.Add(key);
                    }
                }
            }

            maps[group.Key] = await ReadMappedAsync(lookup, lookupColumnNames, keys, cancellationToken);
        }

        var rebuilt = new ReportResultColumn[plan.SourceLayout.Columns.Count];
        for (var engineIndex = 0; engineIndex < plan.EngineOrigin.Count && engineIndex < result.Columns.Count; engineIndex++)
        {
            var origin = plan.EngineOrigin[engineIndex];
            if (origin >= 0)
            {
                rebuilt[origin] = result.Columns[engineIndex];
            }
        }

        foreach (var binding in plan.Bindings)
        {
            var factLabel = ColumnText(
                binding.Fact.DisplayName,
                binding.Fact.DisplayNameArabic,
                binding.Fact.ColumnName,
                lang);
            var lookupLabel = ColumnText(
                binding.LookupDisplayName,
                binding.LookupDisplayNameArabic,
                binding.LookupColumn,
                lang);
            var label = string.IsNullOrWhiteSpace(binding.Alias)
                ? LookupFields.IsControl(binding.LookupColumn) || LookupFields.IsControl(lookupLabel)
                    ? factLabel
                    : $"{factLabel} · {lookupLabel}"
                : binding.Alias.Trim();
            rebuilt[binding.LayoutIndex] = new ReportResultColumn
            {
                Name = binding.Field,
                Label = label
            };
        }

        var resultColumns = new List<ReportResultColumn>();
        foreach (var column in rebuilt)
        {
            resultColumns.Add(column ?? new ReportResultColumn { Name = "lookup", Hidden = true });
        }

        for (var engineIndex = 0; engineIndex < plan.EngineOrigin.Count && engineIndex < result.Columns.Count; engineIndex++)
        {
            if (plan.EngineOrigin[engineIndex] < 0)
            {
                var hidden = result.Columns[engineIndex];
                resultColumns.Add(new ReportResultColumn
                {
                    Name = hidden.Name,
                    Label = hidden.Label,
                    Hidden = true
                });
            }
        }

        for (var extra = plan.EngineOrigin.Count; extra < result.Columns.Count; extra++)
        {
            resultColumns.Add(result.Columns[extra]);
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>(result.Rows.Count);
        foreach (var row in result.Rows)
        {
            var copy = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);
            foreach (var binding in plan.Bindings)
            {
                string? value = null;
                var keyName = FactResultName(result, plan, binding.Fact.ColumnName);
                if (keyName is not null
                    && row.TryGetValue(keyName, out var raw)
                    && raw is not null
                    && maps.TryGetValue(binding.Fact.LookupId!.Value, out var map)
                    && map.TryGetValue(NormalizeKey(raw), out var fields)
                    && fields.TryGetValue(DisplayColumn(binding, lang), out var text))
                {
                    value = text;
                }

                copy[binding.Field] = value;
            }

            rows.Add(copy);
        }

        return new ReportResult
        {
            Columns = resultColumns,
            Rows = rows,
            TotalCount = result.TotalCount,
            Skip = result.Skip,
            Take = result.Take
        };
    }

    private static IEnumerable<string> ColumnsForBinding(Lookup lookup, string lookupColumn)
    {
        if (!LookupFields.IsControl(lookupColumn))
        {
            yield return lookupColumn;
            yield break;
        }

        foreach (var name in LocaleColumns(lookup))
        {
            yield return name;
        }

        yield return FallbackTextColumn(lookup);
    }

    private static string DisplayColumn(LookupColumnBinding binding, string? lang)
        => LookupFields.IsControl(binding.LookupColumn)
            ? LabelColumn(binding.Fact.Lookup!, binding.LookupColumn, lang)
            : binding.LookupColumn;

    private static string? FactResultName(ReportResult result, LookupColumnPlan plan, string factColumn)
    {
        for (var index = 0; index < plan.EngineLayout.Columns.Count && index < result.Columns.Count; index++)
        {
            if (plan.EngineLayout.Columns[index].Field.Equals(factColumn, StringComparison.OrdinalIgnoreCase))
            {
                return result.Columns[index].Name;
            }
        }

        return null;
    }

    private static string NormalizeKey(object value)
    {
        if (value is Guid guid)
        {
            return guid.ToString("D");
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        return Guid.TryParse(text, out var parsed) ? parsed.ToString("D") : text;
    }

    private async Task<Dictionary<string, Dictionary<string, string?>>> ReadMappedAsync(
        Lookup lookup,
        IReadOnlyList<string> columns,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, Dictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);
        if (keys.Count == 0 || columns.Count == 0)
        {
            return map;
        }

        var keySql = SqlIdentifiers.Quote(lookup.KeyColumn);
        var select = new StringBuilder();
        select.Append("CONVERT(nvarchar(4000), ").Append(keySql).Append(") AS [k]");
        for (var index = 0; index < columns.Count; index++)
        {
            select.Append(", CONVERT(nvarchar(4000), ")
                .Append(SqlIdentifiers.Quote(columns[index]))
                .Append(") AS [c")
                .Append(index)
                .Append(']');
        }

        var from = FromSql(lookup);
        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var pending = keys.ToList();
            while (pending.Count > 0)
            {
                var batch = pending.Take(400).ToList();
                pending.RemoveRange(0, batch.Count);
                await using var command = connection.CreateCommand();
                var parameters = new string[batch.Count];
                for (var index = 0; index < batch.Count; index++)
                {
                    var parameterName = "@k" + index;
                    parameters[index] = parameterName;
                    command.Parameters.Add(new SqlParameter(parameterName, SqlDbType.NVarChar, 4000) { Value = batch[index] });
                }

                command.CommandText = $"""
                    SELECT {select}
                    FROM {from}
                    WHERE CONVERT(nvarchar(4000), {keySql}) IN ({string.Join(", ", parameters)})
                    """;
                command.CommandTimeout = 30;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (reader.IsDBNull(0))
                    {
                        continue;
                    }

                    var key = NormalizeKey(reader.GetString(0));
                    if (key.Length == 0 || map.ContainsKey(key))
                    {
                        continue;
                    }

                    var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    for (var index = 0; index < columns.Count; index++)
                    {
                        fields[columns[index]] = reader.IsDBNull(index + 1)
                            ? null
                            : reader.GetString(index + 1);
                    }

                    map[key] = fields;
                }
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        return map;
    }

    private async Task<ProbeResult> ProbeAsync(LookupWriteRequest request, CancellationToken cancellationToken)
    {
        string from;
        string schema = string.Empty;
        string objectName = string.Empty;
        string? sqlText = null;
        if (request.SourceKind == LookupSourceKind.Query)
        {
            sqlText = NormalizeQuery(request.SqlText);
            from = "(" + sqlText + ") AS rb_lookup";
        }
        else
        {
            schema = request.SchemaName?.Trim() ?? string.Empty;
            objectName = request.ObjectName?.Trim() ?? string.Empty;
            if (schema.Length == 0 || objectName.Length == 0)
            {
                throw new ReportQueryException("Schema and object name are required.");
            }

            from = SqlIdentifiers.Qualify(schema, objectName);
        }

        var columns = new List<LookupColumnInfo>();
        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT TOP (0) * FROM " + from;
            command.CommandTimeout = 30;
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                if (string.IsNullOrWhiteSpace(columnName) || !seen.Add(columnName))
                {
                    continue;
                }

                columns.Add(new LookupColumnInfo
                {
                    Name = columnName,
                    SqlType = reader.GetDataTypeName(i)
                });
            }
        }
        catch (SqlException ex)
        {
            throw new ReportQueryException("Lookup source is not valid. " + Truncate(ex.Message, 240));
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        if (columns.Count == 0)
        {
            throw new ReportQueryException("Lookup source returned no columns.");
        }

        return new ProbeResult(schema, objectName, sqlText, columns);
    }

    private static string NormalizeQuery(string? sql)
    {
        var text = (sql ?? string.Empty).Trim().TrimEnd(';').Trim();
        if (text.Length == 0)
        {
            throw new ReportQueryException("Lookup query is required.");
        }

        if (text.Contains(';'))
        {
            throw new ReportQueryException("Lookup query must be a single SELECT.");
        }

        if (!QueryStart().IsMatch(text) || Forbidden().IsMatch(text))
        {
            throw new ReportQueryException("Lookup query must be a single SELECT.");
        }

        return text;
    }

    private static void ApplyColumnSettings(
        List<LookupColumnInfo> columns,
        IReadOnlyList<LookupColumnLocale>? requested,
        string? previousJson,
        string keyColumn,
        string sortColumn)
    {
        var previous = ReadStoredColumns(previousJson);
        foreach (var column in columns)
        {
            previous.TryGetValue(column.Name, out var stored);
            var chosen = requested?.FirstOrDefault(item =>
                item.Name.Equals(column.Name, StringComparison.OrdinalIgnoreCase));
            var raw = chosen?.Locale ?? stored?.Locale;
            var locale = LayoutColumnLocales.Normalize(raw);
            column.Locale = locale is null || locale == LayoutColumnLocales.Default ? null : locale;
            column.DisplayName = BlankToNull(chosen is not null ? chosen.DisplayName : stored?.DisplayName);
            column.DisplayNameArabic = BlankToNull(chosen is not null ? chosen.DisplayNameArabic : stored?.DisplayNameArabic);
            var forced = column.Name.Equals(keyColumn, StringComparison.OrdinalIgnoreCase)
                || column.Name.Equals(sortColumn, StringComparison.OrdinalIgnoreCase);
            bool included;
            if (forced)
            {
                included = true;
            }
            else if (chosen?.Included is bool requestedIncluded)
            {
                included = requestedIncluded;
            }
            else if (stored?.Included is bool storedIncluded)
            {
                included = storedIncluded;
            }
            else
            {
                included = stored is not null;
            }

            column.Included = included;
            column.ShowInDesigner = included && (chosen?.ShowInDesigner ?? stored?.ShowInDesigner ?? false);
        }
    }

    private static Dictionary<string, LookupColumnInfo> ReadStoredColumns(string? json)
    {
        var map = new Dictionary<string, LookupColumnInfo>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return map;
        }

        try
        {
            var columns = JsonSerializer.Deserialize<List<LookupColumnInfo>>(json, JsonOptions);
            if (columns is null)
            {
                return map;
            }

            foreach (var column in columns)
            {
                if (!string.IsNullOrWhiteSpace(column.Name))
                {
                    map[column.Name] = column;
                }
            }
        }
        catch (JsonException)
        {
            return map;
        }

        return map;
    }

    private static string ColumnText(string? english, string? arabic, string fallback, string? lang)
    {
        var en = english?.Trim() ?? string.Empty;
        var ar = arabic?.Trim() ?? string.Empty;
        if (lang is not null
            && lang.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
            && ar.Length > 0)
        {
            return ar;
        }

        if (en.Length > 0)
        {
            return en;
        }

        return ar.Length > 0 ? ar : fallback;
    }

    private static string? BlankToNull(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static void RequireMapped(IReadOnlyList<LookupColumnInfo> columns, string name, string role)
    {
        if (!columns.Any(column => column.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ReportQueryException($"Lookup {role} column '{name}' is not in the source.");
        }
    }

    private static string MatchName(IReadOnlyList<LookupColumnInfo> columns, string name)
        => columns.First(column => column.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Name;

    private static string FromSql(Lookup lookup)
        => lookup.SourceKind == LookupSourceKind.Query
            ? "(" + NormalizeQuery(lookup.SqlText) + ") AS rb_lookup"
            : SqlIdentifiers.Qualify(lookup.SchemaName, lookup.ObjectName);

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private sealed record ProbeResult(
        string SchemaName,
        string ObjectName,
        string? SqlText,
        List<LookupColumnInfo> Columns);

    [GeneratedRegex(@"^(select|with)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QueryStart();

    [GeneratedRegex(@"\b(insert|update|delete|drop|alter|create|exec|execute|merge|truncate|grant|revoke|openrowset|opendatasource)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Forbidden();
}
