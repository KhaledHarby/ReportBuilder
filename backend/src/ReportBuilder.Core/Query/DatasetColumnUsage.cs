using System.Text.RegularExpressions;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public static partial class DatasetColumnUsage
{
    public static void Collect(ReportLayout layout, ISet<string> fields, ISet<Guid> lookupColumns)
    {
        foreach (var column in layout.Columns)
        {
            Add(column.Field, fields, lookupColumns);
            CollectBrackets(column.Expression, fields);
        }

        foreach (var field in layout.GroupBy)
        {
            Add(field, fields, lookupColumns);
        }

        foreach (var filter in layout.Filters)
        {
            Add(filter.Field, fields, lookupColumns);
        }

        foreach (var order in layout.OrderBy)
        {
            Add(order.Field, fields, lookupColumns);
        }

        Add(layout.UserScope?.Field, fields, lookupColumns);
    }

    public static void Collect(AnalyticsDashboardConfig config, Guid datasetId, ISet<string> fields)
    {
        var sources = config.Sources
            .Where(source => source.DatasetId == datasetId)
            .Select(source => source.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (sources.Count == 0)
        {
            return;
        }

        foreach (var link in config.Links)
        {
            if (sources.Contains(link.LeftSourceId))
            {
                foreach (var key in link.LeftKeys)
                {
                    Add(key, fields, lookupColumns: null);
                }
            }

            if (sources.Contains(link.RightSourceId))
            {
                foreach (var key in link.RightKeys)
                {
                    Add(key, fields, lookupColumns: null);
                }
            }
        }

        foreach (var dimension in config.Dimensions)
        {
            if (!sources.Contains(dimension.SourceId))
            {
                continue;
            }

            Add(dimension.Field, fields, lookupColumns: null);
            CollectBrackets(dimension.Expression, fields);
        }

        foreach (var measure in config.Measures)
        {
            if (!sources.Contains(measure.SourceId))
            {
                continue;
            }

            Add(measure.Field, fields, lookupColumns: null);
            CollectBrackets(measure.Expression, fields);
        }

        foreach (var column in config.DetailColumns)
        {
            if (sources.Contains(column.SourceId))
            {
                Add(column.Field, fields, lookupColumns: null);
            }
        }

        var map = config.Map;
        if (map is not null && sources.Contains(map.SourceId))
        {
            Add(map.LatField, fields, lookupColumns: null);
            Add(map.LngField, fields, lookupColumns: null);
            Add(map.LabelField, fields, lookupColumns: null);
        }
    }

    private static void Add(string? field, ISet<string> fields, ISet<Guid>? lookupColumns)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return;
        }

        if (lookupColumns is not null && LookupFields.TryParse(field, out var columnId, out _))
        {
            lookupColumns.Add(columnId);
            return;
        }

        fields.Add(field.Trim());
    }

    private static void CollectBrackets(string? expression, ISet<string> fields)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return;
        }

        foreach (Match match in Bracket().Matches(expression))
        {
            var name = match.Groups[1].Value.Trim();
            if (name.Length > 0)
            {
                fields.Add(name);
            }
        }
    }

    [GeneratedRegex(@"\[([^\[\]]+)\]")]
    private static partial Regex Bracket();
}
