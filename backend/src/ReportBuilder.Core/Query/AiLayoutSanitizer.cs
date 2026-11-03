using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public static class AiLayoutSanitizer
{
    private static readonly HashSet<string> AllowedAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "COUNT", "AVG", "MIN", "MAX"
    };

    private static readonly HashSet<string> NumericSqlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "int", "bigint", "smallint", "tinyint", "decimal", "numeric", "money", "smallmoney", "float", "real"
    };

    private static readonly Dictionary<string, string> Operators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eq"] = "eq",
        ["neq"] = "neq",
        ["gt"] = "gt",
        ["gte"] = "gte",
        ["lt"] = "lt",
        ["lte"] = "lte",
        ["contains"] = "contains",
        ["in"] = "in",
        ["oneof"] = "in",
        ["isoneof"] = "in",
        ["isnull"] = "isNull",
        ["is_null"] = "isNull",
        ["isempty"] = "isNull",
        ["isnotnull"] = "isNotNull",
        ["is_not_null"] = "isNotNull",
        ["isnotempty"] = "isNotNull"
    };

    public static Dataset ResolveDataset(IReadOnlyList<Dataset> datasets, Guid? preferredId, string? hintedName)
    {
        if (datasets.Count == 0)
        {
            throw new ReportAiException("Enable at least one table or view in the catalog before asking Copilot for a report.");
        }

        if (preferredId is Guid id && id != Guid.Empty)
        {
            var byId = datasets.FirstOrDefault(d => d.Id == id);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(hintedName))
        {
            var hint = hintedName.Trim();
            var match = datasets.FirstOrDefault(d =>
                d.Id.ToString().Equals(hint, StringComparison.OrdinalIgnoreCase)
                || d.DisplayName.Equals(hint, StringComparison.OrdinalIgnoreCase)
                || d.ObjectName.Equals(hint, StringComparison.OrdinalIgnoreCase)
                || $"{d.SchemaName}.{d.ObjectName}".Equals(hint, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return datasets[0];
    }

    public static ReportLayout Sanitize(Dataset dataset, ReportLayout? raw)
    {
        var layout = raw ?? new ReportLayout();
        var columns = dataset.Columns
            .Where(c => !c.IsHidden)
            .ToDictionary(c => c.ColumnName, StringComparer.OrdinalIgnoreCase);

        DatasetColumn? Find(string? name) =>
            string.IsNullOrWhiteSpace(name) ? null : columns.GetValueOrDefault(name.Trim());

        var groupBy = layout.GroupBy
            .Select(Find)
            .Where(c => c is { IsGroupable: true })
            .Select(c => c!.ColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chart = SanitizeChart(layout.Kind, layout.Chart);
        if (chart is not null && chart.Type is "pie" or "doughnut" && groupBy.Count > 1)
        {
            groupBy = [groupBy[0]];
        }

        if (chart is not null && groupBy.Count == 0)
        {
            chart = null;
        }

        var selected = new List<LayoutColumn>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in layout.Columns)
        {
            if (!string.IsNullOrWhiteSpace(column.Expression))
            {
                if (TryCalculated(dataset, column, groupBy, seen, labels, out var calculated))
                {
                    selected.Add(calculated!);
                }

                continue;
            }

            if (LookupFields.TryParse(column.Field, out _, out _))
            {
                if (TryLookup(dataset, column, groupBy, seen, labels, out var lookupColumn))
                {
                    selected.Add(lookupColumn!);
                }

                continue;
            }

            var meta = Find(column.Field);
            if (meta is null || !seen.Add(meta.ColumnName))
            {
                continue;
            }

            selected.Add(Present(
                meta.ColumnName,
                column,
                NormalizeAggregate(column.Aggregate, meta, groupBy),
                labels));
        }

        foreach (var field in groupBy)
        {
            if (seen.Contains(field))
            {
                continue;
            }

            selected.Add(new LayoutColumn { Field = field });
            seen.Add(field);
        }

        if (selected.Count == 0)
        {
            selected.AddRange(columns.Values
                .OrderBy(c => c.Ordinal)
                .Take(6)
                .Select(c => new LayoutColumn { Field = c.ColumnName }));
        }

        if (groupBy.Count > 0)
        {
            selected = selected.Select(column =>
            {
                if (!string.IsNullOrWhiteSpace(column.Expression)
                    || LookupFields.TryParse(column.Field, out _, out _))
                {
                    return column;
                }

                var grouped = groupBy.Any(g => g.Equals(column.Field, StringComparison.OrdinalIgnoreCase));
                if (grouped)
                {
                    return new LayoutColumn
                    {
                        Field = column.Field,
                        Alias = column.Alias,
                        Format = column.Format,
                        Locale = column.Locale,
                        Align = column.Align,
                        HeaderBand = column.HeaderBand,
                        HeaderBandArabic = column.HeaderBandArabic,
                        UrlTemplate = column.UrlTemplate,
                        ConditionalFormat = column.ConditionalFormat,
                        Sticky = column.Sticky,
                        Wrap = column.Wrap
                    };
                }

                var meta = columns[column.Field];
                return new LayoutColumn
                {
                    Field = column.Field,
                    Alias = column.Alias,
                    Aggregate = NormalizeAggregate(column.Aggregate, meta, groupBy) ?? DefaultAggregate(meta),
                    Format = column.Format,
                    Locale = column.Locale,
                    Align = column.Align,
                    HeaderBand = column.HeaderBand,
                    HeaderBandArabic = column.HeaderBandArabic,
                    UrlTemplate = column.UrlTemplate,
                    ConditionalFormat = column.ConditionalFormat,
                    Sticky = column.Sticky,
                    Wrap = column.Wrap
                };
            }).ToList();
        }

        var filters = new List<LayoutFilter>();
        foreach (var filter in layout.Filters)
        {
            string? field = null;
            if (LookupColumnPlan.TryResolve(dataset, filter.Field, out var fact, out var lookupColumn))
            {
                field = LookupFields.Format(fact.Id, lookupColumn);
            }
            else
            {
                var meta = Find(filter.Field);
                if (meta is { IsFilterable: true })
                {
                    field = meta.ColumnName;
                }
            }

            if (field is null)
            {
                continue;
            }

            if (!Operators.TryGetValue((filter.Operator ?? "eq").Replace(" ", ""), out var op))
            {
                op = "eq";
            }

            var needsValue = op is not "isNull" and not "isNotNull";
            filters.Add(new LayoutFilter
            {
                Field = field,
                Operator = op,
                Value = needsValue ? filter.Value?.Trim() : null,
                IsFixed = filter.IsFixed,
                IsHidden = filter.IsHidden,
                IsRequired = filter.IsRequired,
                Alias = string.IsNullOrWhiteSpace(filter.Alias) ? null : filter.Alias.Trim(),
                AliasArabic = string.IsNullOrWhiteSpace(filter.AliasArabic) ? null : filter.AliasArabic.Trim(),
                DependsOn = CleanDependsOn(dataset, filter.DependsOn, Find)
            });
        }

        var orderBy = new List<LayoutOrderBy>();
        var seenOrder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in layout.OrderBy)
        {
            var meta = Find(item.Field);
            if (meta is null || !seenOrder.Add(meta.ColumnName))
            {
                continue;
            }

            orderBy.Add(new LayoutOrderBy
            {
                Field = meta.ColumnName,
                Direction = SortDirections.Normalize(item.Direction, meta.DefaultSortDirection)
            });
        }

        return new ReportLayout
        {
            Kind = chart is null ? "table" : "chart",
            Chart = chart,
            UserScope = SanitizeUserScope(dataset, layout.UserScope),
            Detail = layout.Detail,
            Columns = selected,
            GroupBy = groupBy,
            Filters = filters,
            FiltersPerRow = layout.FiltersPerRow,
            OrderBy = orderBy
        };
    }

    private static UserScope? SanitizeUserScope(Dataset dataset, UserScope? scope)
    {
        if (scope?.Enabled != true)
        {
            return scope?.Enabled == true
                ? new UserScope { Enabled = false }
                : null;
        }

        var columns = dataset.Columns
            .Where(column => !column.IsHidden && column.IsFilterable)
            .ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(scope.Field) || !columns.TryGetValue(scope.Field.Trim(), out var meta))
        {
            return new UserScope { Enabled = false };
        }

        var property = UserScopeProperties.Normalize(scope.Property);
        if (!UserScopeProperties.IsCompatible(property, meta.SqlType))
        {
            property = UserScopeProperties.PropertiesForSqlType(meta.SqlType).FirstOrDefault()
                ?? UserScopeProperties.Id;
        }

        return new UserScope
        {
            Enabled = true,
            Field = meta.ColumnName,
            Property = property
        };
    }

    private static readonly HashSet<string> ChartTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bar", "line", "area", "pie", "doughnut"
    };

    private static ChartConfig? SanitizeChart(string? kind, ChartConfig? chart)
    {
        if (!string.Equals(kind, "chart", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var type = (chart?.Type ?? "bar").Trim().ToLowerInvariant();
        if (type is "column" or "columns")
        {
            type = "bar";
        }
        else if (type is "donut")
        {
            type = "doughnut";
        }

        if (!ChartTypes.Contains(type))
        {
            type = "bar";
        }

        var pie = type is "pie" or "doughnut";
        return new ChartConfig
        {
            Type = type,
            Stacked = !pie && chart?.Stacked == true,
            ShowLegend = chart?.ShowLegend ?? true,
            ShowDataLabels = chart?.ShowDataLabels == true
        };
    }

    private static bool TryCalculated(
        Dataset dataset,
        LayoutColumn column,
        IReadOnlyList<string> groupBy,
        HashSet<string> seen,
        HashSet<string> labels,
        out LayoutColumn? calculated)
    {
        calculated = null;
        var expression = column.Expression?.Trim();
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 2_000)
        {
            return false;
        }

        try
        {
            AnalyticsExpression.Compile(expression, "rb_src", dataset, groupBy.Count > 0);
        }
        catch (ReportQueryException)
        {
            return false;
        }

        var field = column.Field.StartsWith("calc:", StringComparison.OrdinalIgnoreCase)
            ? column.Field.Trim()
            : "calc:" + Guid.NewGuid().ToString("D");
        if (!seen.Add(field))
        {
            return false;
        }

        calculated = new LayoutColumn
        {
            Field = field,
            Expression = expression,
            Alias = UniqueLabel(string.IsNullOrWhiteSpace(column.Alias) ? "Calculation" : column.Alias.Trim(), labels),
            Format = ColumnFormats.Normalize(column.Format),
            Locale = NormalizeLocale(column.Locale),
            Align = column.Align,
            HeaderBand = column.HeaderBand,
            HeaderBandArabic = column.HeaderBandArabic,
            UrlTemplate = NormalizeUrlTemplate(column.UrlTemplate),
            ConditionalFormat = ConditionalFormats.Sanitize(column.ConditionalFormat),
            Sticky = column.Sticky,
            Wrap = column.Wrap
        };
        return true;
    }

    private static bool TryLookup(
        Dataset dataset,
        LayoutColumn column,
        IReadOnlyList<string> groupBy,
        HashSet<string> seen,
        HashSet<string> labels,
        out LayoutColumn? lookupColumn)
    {
        lookupColumn = null;
        if (!LookupColumnPlan.TryResolve(dataset, column.Field, out var fact, out var requested))
        {
            return false;
        }

        if (groupBy.Count > 0
            && !groupBy.Any(field => field.Equals(fact.ColumnName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var field = LookupFields.Format(fact.Id, requested);
        if (!seen.Add(field))
        {
            return false;
        }

        lookupColumn = Present(field, column, null, labels);
        return true;
    }

    private static LayoutColumn Present(
        string field,
        LayoutColumn column,
        string? aggregate,
        HashSet<string> labels)
    {
        string? alias;
        if (string.IsNullOrWhiteSpace(column.Alias))
        {
            alias = labels.Add(field) ? null : UniqueLabel(field, labels);
        }
        else
        {
            alias = UniqueLabel(column.Alias.Trim(), labels);
        }
        return new LayoutColumn
        {
            Field = field,
            Alias = alias,
            Aggregate = aggregate,
            Format = ColumnFormats.Normalize(column.Format),
            Locale = NormalizeLocale(column.Locale),
            Align = column.Align,
            HeaderBand = column.HeaderBand,
            HeaderBandArabic = column.HeaderBandArabic,
            UrlTemplate = NormalizeUrlTemplate(column.UrlTemplate),
            ConditionalFormat = ConditionalFormats.Sanitize(column.ConditionalFormat),
            Sticky = column.Sticky,
            Wrap = column.Wrap
        };
    }

    private static string? NormalizeLocale(string? locale)
    {
        var value = LayoutColumnLocales.Normalize(locale);
        return value is null or LayoutColumnLocales.Default ? null : value;
    }

    private static string? CleanDependsOn(
        Dataset dataset,
        string? dependsOn,
        Func<string?, DatasetColumn?> find)
    {
        if (string.IsNullOrWhiteSpace(dependsOn))
        {
            return null;
        }

        if (LookupColumnPlan.TryResolve(dataset, dependsOn, out var fact, out var lookupColumn))
        {
            return LookupFields.Format(fact.Id, lookupColumn);
        }

        var meta = find(dependsOn);
        return meta is { IsFilterable: true } ? meta.ColumnName : null;
    }

    private static string UniqueLabel(string label, HashSet<string> labels)
    {
        var next = label;
        var n = 2;
        while (!labels.Add(next))
        {
            next = $"{label} {n++}";
        }

        return next;
    }

    private static string? NormalizeAggregate(string? aggregate, DatasetColumn meta, IReadOnlyList<string> groupBy)
    {
        if (string.IsNullOrWhiteSpace(aggregate) || aggregate.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (groupBy.Count == 0)
        {
            return null;
        }

        if (groupBy.Any(g => g.Equals(meta.ColumnName, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var value = aggregate.Trim().ToUpperInvariant();
        if (!AllowedAggregates.Contains(value))
        {
            return DefaultAggregate(meta);
        }

        if ((value is "SUM" or "AVG") && !NumericSqlTypes.Contains(meta.SqlType))
        {
            return "COUNT";
        }

        return value;
    }

    private static string DefaultAggregate(DatasetColumn meta)
        => NumericSqlTypes.Contains(meta.SqlType) ? "SUM" : "COUNT";

    private static string? NormalizeUrlTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var trimmed = template.Trim();
        if (trimmed.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("data:", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("vbscript:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }
}
