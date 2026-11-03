using System.Text.Json;
using System.Text.Json.Serialization;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Models;

public static class LayoutJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ReportLayout Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ReportLayout();
        }

        return JsonSerializer.Deserialize<ReportLayout>(json, Options) ?? new ReportLayout();
    }

    public static string Serialize(ReportLayout layout)
        => JsonSerializer.Serialize(layout, Options);

    public static string KindOf(string? json)
    {
        var layout = Parse(json);
        return layout.IsChart ? "chart" : "table";
    }
}
