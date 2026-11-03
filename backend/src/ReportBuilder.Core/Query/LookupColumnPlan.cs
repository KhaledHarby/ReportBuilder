using System.Text.Json;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public sealed class LookupColumnBinding
{
    public int LayoutIndex { get; init; }
    public string Field { get; init; } = string.Empty;
    public string? Alias { get; init; }
    public DatasetColumn Fact { get; init; } = null!;
    public string LookupColumn { get; init; } = string.Empty;
    public string? LookupDisplayName { get; init; }
    public string? LookupDisplayNameArabic { get; init; }
}

public sealed class LookupColumnPlan
{
    public ReportLayout SourceLayout { get; init; } = new();
    public ReportLayout EngineLayout { get; init; } = new();
    public IReadOnlyList<LookupColumnBinding> Bindings { get; init; } = [];
    public IReadOnlyList<int> EngineOrigin { get; init; } = [];
    public bool HasLookups => Bindings.Count > 0;

    public static bool IsLookupColumn(Dataset dataset, string field)
        => TryResolve(dataset, field, out _, out _);

    public static bool TryResolve(Dataset dataset, string field, out DatasetColumn fact, out string lookupColumn)
    {
        fact = null!;
        lookupColumn = string.Empty;
        if (!LookupFields.TryParse(field, out var columnId, out var requested))
        {
            return false;
        }

        var match = dataset.Columns.FirstOrDefault(column => column.Id == columnId && !column.IsHidden);
        if (match?.Lookup is not { IsValid: true } lookup || !AllowedColumn(lookup, requested))
        {
            return false;
        }

        fact = match;
        lookupColumn = MatchColumn(lookup, requested);
        return true;
    }

    public static LookupColumnPlan Prepare(Dataset dataset, ReportLayout layout)
    {
        var visible = dataset.Columns.Where(column => !column.IsHidden).ToList();
        var bindings = new List<LookupColumnBinding>();
        var engineColumns = new List<LayoutColumn>();
        var origin = new List<int>();

        for (var index = 0; index < layout.Columns.Count; index++)
        {
            var column = layout.Columns[index];
            if (!LookupFields.TryParse(column.Field, out var columnId, out var lookupColumn))
            {
                engineColumns.Add(column);
                origin.Add(index);
                continue;
            }

            var fact = visible.FirstOrDefault(item => item.Id == columnId);
            if (fact?.LookupId is null || fact.Lookup is not { IsValid: true } lookup)
            {
                throw new ReportQueryException(
                    $"Column '{lookupColumn}' comes from a lookup that is no longer available.");
            }

            if (!AllowedColumn(lookup, lookupColumn))
            {
                throw new ReportQueryException(
                    $"Column '{lookupColumn}' is not in the lookup '{lookup.Name}'.");
            }

            var stored = FindStored(lookup, lookupColumn);
            bindings.Add(new LookupColumnBinding
            {
                LayoutIndex = index,
                Field = column.Field,
                Alias = column.Alias,
                Fact = fact,
                LookupColumn = MatchColumn(lookup, lookupColumn),
                LookupDisplayName = BlankToNull(stored?.DisplayName),
                LookupDisplayNameArabic = BlankToNull(stored?.DisplayNameArabic)
            });
        }

        if (bindings.Count == 0)
        {
            return new LookupColumnPlan
            {
                SourceLayout = layout,
                EngineLayout = layout
            };
        }

        var grouping = layout.GroupBy.Any(field => !string.IsNullOrWhiteSpace(field));
        var groupBy = layout.GroupBy.ToList();
        foreach (var binding in bindings)
        {
            var selected = layout.Columns.FirstOrDefault(column =>
                column.Field.Equals(binding.Fact.ColumnName, StringComparison.OrdinalIgnoreCase));
            if (selected is not null && !string.IsNullOrWhiteSpace(selected.Aggregate))
            {
                throw new ReportQueryException(
                    $"Column '{binding.Fact.DisplayName}' is aggregated, so its lookup columns cannot be shown.");
            }

            if (engineColumns.Any(column =>
                    column.Field.Equals(binding.Fact.ColumnName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            engineColumns.Add(new LayoutColumn { Field = binding.Fact.ColumnName });
            origin.Add(-1);
            if (grouping && !groupBy.Any(field =>
                    field.Equals(binding.Fact.ColumnName, StringComparison.OrdinalIgnoreCase)))
            {
                groupBy.Add(binding.Fact.ColumnName);
            }
        }

        if (engineColumns.Count == 0)
        {
            throw new ReportQueryException("Select at least one column.");
        }

        return new LookupColumnPlan
        {
            SourceLayout = layout,
            EngineLayout = new ReportLayout
            {
                Kind = layout.Kind,
                Columns = engineColumns,
                GroupBy = groupBy,
                Filters = layout.Filters,
                OrderBy = layout.OrderBy,
                LookupSorts = layout.LookupSorts,
                Chart = layout.Chart,
                UserScope = layout.UserScope,
                Detail = layout.Detail
            },
            Bindings = bindings,
            EngineOrigin = origin
        };
    }

    private static bool AllowedColumn(Lookup lookup, string name)
    {
        if (LookupFields.IsControl(name)
            || lookup.KeyColumn.Equals(name, StringComparison.OrdinalIgnoreCase)
            || lookup.LabelColumn.Equals(name, StringComparison.OrdinalIgnoreCase)
            || (lookup.SortColumn?.Equals(name, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return true;
        }

        var stored = FindStored(lookup, name);
        return stored is not null && stored.Included != false;
    }

    private static string MatchColumn(Lookup lookup, string name)
    {
        if (LookupFields.IsControl(name))
        {
            return LookupFields.Control;
        }

        var match = ReadColumns(lookup)
            .FirstOrDefault(column => column.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        if (lookup.KeyColumn.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return lookup.KeyColumn;
        }

        if (lookup.LabelColumn.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return lookup.LabelColumn;
        }

        return lookup.SortColumn ?? name;
    }

    private static IEnumerable<string> ReadColumns(Lookup lookup)
        => ReadStored(lookup).Select(column => column.Name);

    private static StoredLookupColumn? FindStored(Lookup lookup, string name)
        => ReadStored(lookup).FirstOrDefault(column => column.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static List<StoredLookupColumn> ReadStored(Lookup lookup)
    {
        if (string.IsNullOrWhiteSpace(lookup.ColumnsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<StoredLookupColumn>>(
                    lookup.ColumnsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?.Where(column => !string.IsNullOrWhiteSpace(column.Name))
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? BlankToNull(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private sealed class StoredLookupColumn
    {
        public string Name { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? DisplayNameArabic { get; set; }
        public bool? Included { get; set; }
    }
}

public static class LookupFields
{
    public const string Prefix = "lookup:";
    public const string Control = "control";

    public static bool IsControl(string? name)
        => name?.Equals(Control, StringComparison.OrdinalIgnoreCase) == true;

    public static string Format(Guid columnId, string lookupColumn)
        => Prefix + columnId.ToString("D") + ":" + lookupColumn;

    public static bool TryParse(string? field, out Guid columnId, out string lookupColumn)
    {
        columnId = Guid.Empty;
        lookupColumn = string.Empty;
        if (string.IsNullOrWhiteSpace(field)
            || !field.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = field[Prefix.Length..];
        var split = rest.IndexOf(':');
        if (split <= 0 || !Guid.TryParse(rest[..split], out columnId))
        {
            return false;
        }

        lookupColumn = rest[(split + 1)..].Trim();
        return lookupColumn.Length > 0;
    }
}
