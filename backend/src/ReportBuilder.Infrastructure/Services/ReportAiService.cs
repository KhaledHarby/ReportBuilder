using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Infrastructure.Services;

public sealed class ReportAiService : IReportAiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDatasetCatalogService _catalog;
    private readonly ICurrentUser _currentUser;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IAiConnectionService _connections;
    private readonly IActivityLog _log;

    public ReportAiService(
        IDatasetCatalogService catalog,
        ICurrentUser currentUser,
        IHttpClientFactory httpFactory,
        IAiConnectionService connections,
        IActivityLog log)
    {
        _catalog = catalog;
        _currentUser = currentUser;
        _httpFactory = httpFactory;
        _connections = connections;
        _log = log;
    }

    public async Task<ReportAiStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetDefaultAsync(cancellationToken);
        return new ReportAiStatus
        {
            Configured = connection is not null,
            Model = connection?.Model ?? string.Empty
        };
    }

    public async Task<ReportAiAssistResult> AssistAsync(ReportAiAssistRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetDefaultAsync(cancellationToken);
        if (connection is null)
        {
            await WriteAiFailureAsync(
                "Copilot is not configured. Add an AI connection in Settings and choose a default.",
                0,
                StatusCodes503,
                cancellationToken);
            throw new ReportAiException(
                "Copilot is not configured. Add an AI connection in Settings and choose a default.",
                StatusCodes503);
        }

        var prompt = (request.Prompt ?? string.Empty).Trim();
        if (prompt.Length == 0)
        {
            await WriteAiFailureAsync("Describe the report you want.", 0, 400, cancellationToken);
            throw new ReportAiException("Describe the report you want.");
        }

        if (prompt.Length > 4000)
        {
            await WriteAiFailureAsync("Keep the request under 4,000 characters.", prompt.Length, 400, cancellationToken);
            throw new ReportAiException("Keep the request under 4,000 characters.");
        }

        try
        {
            return await AssistCoreAsync(request, prompt, connection, cancellationToken);
        }
        catch (ReportAiException ex)
        {
            await WriteAiFailureAsync(ex.Message, prompt.Length, ex.StatusCode, cancellationToken);
            throw;
        }
    }

    private async Task<ReportAiAssistResult> AssistCoreAsync(
        ReportAiAssistRequest request,
        string prompt,
        AiConnectionRuntime connection,
        CancellationToken cancellationToken)
    {
        var datasets = (await _catalog.ListAsync(enabledOnly: true, cancellationToken))
            .Where(d => d.IsEnabled && DatasetAccessRules.CanUseInDesigner(d, _currentUser))
            .ToList();

        var preferred = datasets.FirstOrDefault(d => d.Id == request.DatasetId);
        var catalogJson = JsonSerializer.Serialize(datasets.Select(ToCatalogShape), JsonOptions);
        var currentJson = JsonSerializer.Serialize(new
        {
            name = request.Name,
            nameArabic = request.NameArabic,
            description = request.Description,
            descriptionArabic = request.DescriptionArabic,
            datasetId = preferred?.Id,
            datasetName = preferred?.DisplayName,
            layout = request.Layout
        }, JsonOptions);

        var payload = new
        {
            model = connection.Model,
            temperature = 0.2,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new
                {
                    role = "user",
                    content =
                        $"Enabled catalog:\n{catalogJson}\n\nCurrent report (may be empty):\n{currentJson}\n\nUser request:\n{prompt}"
                }
            }
        };

        var client = _httpFactory.CreateClient("OpenAI");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, AiConnectionService.CombineUrl(connection.BaseUrl, "chat/completions"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey.Trim());

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ReportAiException("Could not reach OpenAI. Check network access and try again.", 502);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ReportAiException(ReadOpenAiError(body, response.StatusCode), 502);
        }

        var content = ReadMessageContent(body);
        if (!AiReplyParser.TryParse(content, out var reply) || reply is null)
        {
            throw new ReportAiException("OpenAI returned a layout that could not be read. Try rephrasing the request.", 502);
        }

        var parsedId = Guid.TryParse(reply.DatasetId, out var guid) ? guid : (Guid?)null;
        var dataset = AiLayoutSanitizer.ResolveDataset(
            datasets,
            parsedId ?? request.DatasetId,
            reply.DatasetName ?? reply.ObjectName ?? reply.DatasetId);

        var layout = AiLayoutSanitizer.Sanitize(dataset, reply.Layout);
        if (layout.Columns.Count == 0)
        {
            throw new ReportAiException("The suggestion did not include any allowed columns. Enable sources in the catalog and try again.");
        }

        var name = TrimText(reply.Name, 120) ?? "Untitled report";
        var nameArabic = TrimText(reply.NameArabic, 120) ?? TrimText(request.NameArabic, 120);
        var description = TrimText(reply.Description, 400) ?? TrimText(prompt, 400) ?? prompt;
        var descriptionArabic = TrimText(reply.DescriptionArabic, 400) ?? TrimText(request.DescriptionArabic, 400);

        var explanation = string.IsNullOrWhiteSpace(reply.Explanation)
            ? "Drafted a layout from the enabled catalog. Review fields before saving."
            : reply.Explanation.Trim();

        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Ai,
                Action = "ai.assist",
                Message = $"Copilot drafted report {name}.",
                EntityType = "Dataset",
                EntityId = dataset.Id,
                EntityName = dataset.DisplayName,
                Details = $"{{\"promptLength\":{prompt.Length},\"model\":\"{connection.Model}\"}}"
            },
            cancellationToken);

        return new ReportAiAssistResult
        {
            Name = name,
            NameArabic = nameArabic,
            Description = description,
            DescriptionArabic = descriptionArabic,
            DatasetId = dataset.Id,
            DatasetName = dataset.DisplayName,
            Layout = layout,
            Explanation = explanation
        };
    }

    private static object ToCatalogShape(Dataset dataset) => new
    {
        id = dataset.Id,
        name = dataset.DisplayName,
        schema = dataset.SchemaName,
        objectName = dataset.ObjectName,
        type = dataset.ObjectType.ToString(),
        columns = dataset.Columns
            .Where(c => !c.IsHidden)
            .OrderBy(c => c.Ordinal)
            .Select(c => new
            {
                field = c.ColumnName,
                name = c.DisplayName,
                nameArabic = c.DisplayNameArabic,
                type = c.SqlType,
                filterable = c.IsFilterable,
                groupable = c.IsGroupable,
                defaultSortDirection = SortDirections.Normalize(c.DefaultSortDirection),
                lookup = c.Lookup is { IsValid: true } lookup
                    ? new
                    {
                        field = LookupFields.Format(c.Id, LookupFields.Control),
                        name = lookup.Name,
                        nameArabic = lookup.NameArabic
                    }
                    : null
            })
    };

    private Task WriteAiFailureAsync(
        string message,
        int promptLength,
        int status,
        CancellationToken cancellationToken) =>
        _log.WriteAsync(
            new ActivityEvent
            {
                Level = ActivityLevels.Error,
                Category = ActivityCategories.Ai,
                Action = "ai.failed",
                Message = message,
                StatusCode = status,
                Details = $"{{\"promptLength\":{promptLength},\"status\":{status}}}"
            },
            cancellationToken);

    private static string ReadMessageContent(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ReportAiException("OpenAI returned an empty response.", 502);
            }

            return content;
        }
        catch (Exception ex) when (ex is not ReportAiException)
        {
            throw new ReportAiException("OpenAI returned an unexpected response.", 502);
        }
    }

    private static string ReadOpenAiError(string body, System.Net.HttpStatusCode status)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                var text = message.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to the generic message.
        }

        return status == System.Net.HttpStatusCode.Unauthorized
            ? "The AI endpoint rejected the API key. Check the default connection in Settings."
            : "OpenAI could not draft this report. Try again in a moment.";
    }

    private static string? TrimText(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        return text.Length > max ? text[..max] : text;
    }

    private const int StatusCodes503 = 503;

    private const string SystemPrompt = """
        You are Copilot, a report layout assistant. You never query data. You only propose a report template
        the designer can apply.

        Rules:
        - Use only catalog objects and column field names exactly as given. Never invent tables or columns.
        - type "Query" is a stored SQL dataset. Choose it by id and name. Ignore its objectName.
        - Prefer the current dataset when the request still fits it.
        - If a requested field exists only on one catalog object, choose that object.
        - name and nameArabic are each at least 5 characters. descriptionArabic is the Arabic description.
        - layout.kind is "table" or "chart". A chart needs groupBy and layout.chart.
        - chart.type is bar, line, area, pie, or doughnut. pie and doughnut use one groupBy field and no stacked series.
        - chart.stacked, chart.showLegend, and chart.showDataLabels are booleans. showLegend defaults to true.
        - grouping requests include that field in groupBy and in columns, with aggregates on the measures.
        - columns[].field is a catalog field. alias is the column title. aggregate is SUM, COUNT, AVG, MIN, MAX, or null.
        - SUM and AVG only on numeric types. Aggregates require groupBy. Grouped fields must not be aggregated.
        - A calculated column has expression and alias, and no field from the catalog. Reference columns as [ColumnName].
          Allowed: arithmetic, YEAR, MONTH, DAY, UPPER, LOWER, LEN, LEFT, RIGHT, ISNULL, COALESCE, CONCAT, ABS, ROUND, CAST, TRY_CONVERT, DATEPART, DATENAME.
          In a grouped report the expression must aggregate, or use only grouped columns. COUNT, SUM, AVG, MIN, MAX are allowed only then.
        - When a column has lookup.field, that value is the lookup column. Use it to show the lookup label. It can be a column or a filter. Do not group or sort by it.
        - locale is null, "en", or "ar". "en" shows only in the English view. "ar" shows only in the Arabic view.
        - format is date, datetime, time, integer, or decimal.
        - urlTemplate is an https link. Placeholders use {FieldName}. No javascript: or data: URLs.
        - conditionalFormat rules use the filter operators. backgroundColor and color are #RRGGBB. bold is a boolean.
        - groupBy only on groupable columns. filters only on filterable columns or a lookup.field.
        - filter operators: eq, in, neq, gt, gte, lt, lte, contains, isNull, isNotNull.
        - Repeated equals on the same field means ANY of those values (OR / IN), not all at once.
        - Filter value is one string. Several values use operator "in" and value "First, Second, Special". Never a JSON array.
        - isFixed locks the filter. isHidden applies it without showing it. alias and aliasArabic label it.
        - dependsOn is another filter field. The value list narrows by that filter.
        - userScope limits each person to their own rows. enabled, field (a filterable catalog column), property: id, userName, email, or displayName. Use id for uniqueidentifier columns.
        - Do not include hidden or unknown fields. Do not return SQL.
        - explanation is 1-2 sentences for the designer.

        Reply with JSON only:
        {
          "name": "string",
          "nameArabic": "string",
          "description": "string",
          "descriptionArabic": "string",
          "datasetId": "guid from catalog",
          "datasetName": "catalog name",
          "explanation": "string",
          "layout": {
            "kind": "table",
            "chart": { "type": "bar", "stacked": false, "showLegend": true, "showDataLabels": false },
            "columns": [{ "field": "", "alias": null, "aggregate": null, "expression": null, "locale": null, "format": null, "urlTemplate": null }],
            "groupBy": [],
            "filters": [{ "field": "", "operator": "in", "value": "First, Second, Special", "alias": null, "aliasArabic": null, "isFixed": false, "isHidden": false, "dependsOn": null }],
            "orderBy": [{ "field": "", "direction": "desc" }],
            "userScope": { "enabled": false, "field": "", "property": "id" }
          }
        }
        """;
}
