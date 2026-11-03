using System.Text.Json;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public static class AiReplyParser
{
    public sealed class Draft
    {
        public string? Name { get; init; }
        public string? NameArabic { get; init; }
        public string? Description { get; init; }
        public string? DescriptionArabic { get; init; }
        public string? DatasetId { get; init; }
        public string? DatasetName { get; init; }
        public string? ObjectName { get; init; }
        public string? Explanation { get; init; }
        public ReportLayout Layout { get; init; } = new();
    }

    public static bool TryParse(string? content, out Draft? draft)
    {
        draft = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var json = ExtractJsonObject(content);
        if (json is null)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var layoutElement = Get(root, "layout");
            draft = new Draft
            {
                Name = ReadString(root, "name"),
                NameArabic = ReadString(root, "nameArabic", "name_arabic"),
                Description = ReadString(root, "description"),
                DescriptionArabic = ReadString(root, "descriptionArabic", "description_arabic"),
                DatasetId = ReadString(root, "datasetId", "dataset_id"),
                DatasetName = ReadString(root, "datasetName", "dataset_name"),
                ObjectName = ReadString(root, "objectName", "object_name"),
                Explanation = ReadString(root, "explanation"),
                Layout = ReadLayout(layoutElement)
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ReportLayout ReadLayout(JsonElement? element)
    {
        var layout = new ReportLayout();
        if (element is not { ValueKind: JsonValueKind.Object } root)
        {
            return layout;
        }

        if (Get(root, "columns") is { ValueKind: JsonValueKind.Array } columns)
        {
            foreach (var item in columns.EnumerateArray())
            {
                var field = ReadField(item);
                var expression = ReadString(item, "expression");
                if (string.IsNullOrWhiteSpace(field) && string.IsNullOrWhiteSpace(expression))
                {
                    continue;
                }

                layout.Columns.Add(new LayoutColumn
                {
                    Field = string.IsNullOrWhiteSpace(field) ? "calc:" + Guid.NewGuid().ToString("D") : field,
                    Expression = expression,
                    Alias = ReadString(item, "alias"),
                    Aggregate = ReadString(item, "aggregate"),
                    Format = ColumnFormats.Normalize(ReadString(item, "format")),
                    Locale = ReadString(item, "locale"),
                    UrlTemplate = ReadString(item, "urlTemplate", "url_template", "link"),
                    ConditionalFormat = ReadConditionalFormat(item)
                });
            }
        }

        if (Get(root, "groupBy", "group_by") is { ValueKind: JsonValueKind.Array } groupBy)
        {
            foreach (var item in groupBy.EnumerateArray())
            {
                var field = ReadField(item);
                if (!string.IsNullOrWhiteSpace(field))
                {
                    layout.GroupBy.Add(field);
                }
            }
        }

        if (Get(root, "filters") is { ValueKind: JsonValueKind.Array } filters)
        {
            foreach (var item in filters.EnumerateArray())
            {
                var field = ReadField(item);
                if (string.IsNullOrWhiteSpace(field))
                {
                    continue;
                }

                layout.Filters.Add(new LayoutFilter
                {
                    Field = field,
                    Operator = ReadString(item, "operator") ?? "eq",
                    Value = ReadFilterValue(item),
                    IsFixed = ReadFlag(item, "isFixed", "fixed"),
                    IsHidden = ReadFlag(item, "isHidden", "hidden"),
                    Alias = ReadString(item, "alias"),
                    AliasArabic = ReadString(item, "aliasArabic", "alias_arabic"),
                    DependsOn = ReadString(item, "dependsOn", "depends_on", "cascadeFrom")
                });
            }
        }

        if (Get(root, "orderBy", "order_by", "sort") is { ValueKind: JsonValueKind.Array } orderBy)
        {
            foreach (var item in orderBy.EnumerateArray())
            {
                var field = ReadField(item);
                if (string.IsNullOrWhiteSpace(field))
                {
                    continue;
                }

                layout.OrderBy.Add(new LayoutOrderBy
                {
                    Field = field,
                    Direction = ReadString(item, "direction") ?? string.Empty
                });
            }
        }

        var kind = ReadString(root, "kind");
        var chartElement = Get(root, "chart");
        var chartType = chartElement is { ValueKind: JsonValueKind.Object } chartObject
            ? ReadString(chartObject, "type")
            : null;
        var wantsChart = string.Equals(kind, "chart", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(kind) && !string.IsNullOrWhiteSpace(chartType));
        if (wantsChart && chartElement is { ValueKind: JsonValueKind.Object } chart)
        {
            layout.Kind = "chart";
            layout.Chart = new ChartConfig
            {
                Type = chartType ?? "bar",
                Stacked = ReadFlag(chart, "stacked"),
                ShowLegend = Get(chart, "showLegend", "show_legend") is null || ReadFlag(chart, "showLegend", "show_legend"),
                ShowDataLabels = ReadFlag(chart, "showDataLabels", "show_data_labels", "dataLabels")
            };
        }
        else if (wantsChart)
        {
            layout.Kind = "chart";
            layout.Chart = new ChartConfig();
        }
        else if (!string.IsNullOrWhiteSpace(kind))
        {
            layout.Kind = kind.Trim();
        }

        if (Get(root, "userScope", "user_scope") is { ValueKind: JsonValueKind.Object } scope)
        {
            layout.UserScope = new UserScope
            {
                Enabled = ReadFlag(scope, "enabled"),
                Field = ReadString(scope, "field", "column") ?? string.Empty,
                Property = ReadString(scope, "property") ?? UserScopeProperties.Id
            };
        }

        return layout;
    }

    private static List<ConditionalFormatRule>? ReadConditionalFormat(JsonElement column)
    {
        var element = Get(column, "conditionalFormat", "conditional_format", "conditionalFormats");
        if (element is not { ValueKind: JsonValueKind.Array })
        {
            return null;
        }

        var rules = new List<ConditionalFormatRule>();
        foreach (var item in element.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            rules.Add(new ConditionalFormatRule
            {
                Operator = ReadString(item, "operator") ?? "eq",
                Value = ReadFilterValue(item),
                BackgroundColor = ReadString(item, "backgroundColor", "background_color", "background"),
                Color = ReadString(item, "color", "textColor", "text_color"),
                Bold = ReadFlag(item, "bold")
                    || string.Equals(ReadString(item, "fontWeight", "font_weight"), "bold", StringComparison.OrdinalIgnoreCase)
            });
        }

        return ConditionalFormats.Sanitize(rules);
    }

    private static string? ReadField(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(element, "field", "column", "columnName", "name");
    }

    private static string? ReadFilterValue(JsonElement element)
    {
        var value = Get(element, "value", "values");
        if (value is null)
        {
            return null;
        }

        return FlattenValue(value.Value);
    }

    private static bool ReadFlag(JsonElement element, params string[] names)
    {
        var match = Get(element, names);
        if (match is null)
        {
            return false;
        }

        return match.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => match.Value.GetString() is "true" or "1" or "yes",
            JsonValueKind.Number => match.Value.TryGetInt32(out var number) && number != 0,
            _ => false
        };
    }

    private static string? FlattenValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => string.Join(
                ", ",
                value.EnumerateArray()
                    .Select(FlattenValue)
                    .Where(part => !string.IsNullOrWhiteSpace(part))),
            _ => null
        };

    private static string? ReadString(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var match = Get(element, names);
        if (match is null)
        {
            return null;
        }

        return match.Value.ValueKind switch
        {
            JsonValueKind.String => match.Value.GetString(),
            JsonValueKind.Number => match.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static JsonElement? Get(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value;
                }
            }
        }

        return null;
    }

    private static string? ExtractJsonObject(string content)
    {
        var text = content.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = text.IndexOf('\n');
            if (firstBreak > 0)
            {
                text = text[(firstBreak + 1)..];
            }

            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                text = text[..fence];
            }

            text = text.Trim();
        }

        if (text.StartsWith('{') && text.EndsWith('}'))
        {
            return text;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return text[start..(end + 1)];
    }
}
