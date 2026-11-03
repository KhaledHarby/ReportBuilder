using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

public sealed class AiConnectionService : IAiConnectionService
{
    private readonly ReportBuilderDbContext _db;
    private readonly IHttpClientFactory _httpFactory;

    public AiConnectionService(ReportBuilderDbContext db, IHttpClientFactory httpFactory)
    {
        _db = db;
        _httpFactory = httpFactory;
    }

    public async Task<IReadOnlyList<AiConnectionModel>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.AiConnections
            .AsNoTracking()
            .OrderByDescending(item => item.IsEnabled)
            .ThenByDescending(item => item.IsDefault)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<AiConnectionModel> CreateAsync(AiConnectionWrite request, CancellationToken cancellationToken = default)
    {
        var connection = new AiConnection
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        Apply(connection, request, keepKey: false);
        if (connection.IsEnabled && (request.IsDefault || !await HasEnabledDefaultAsync(null, cancellationToken)))
        {
            await ClearDefaultAsync(cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            connection.IsDefault = true;
        }

        _db.AiConnections.Add(connection);
        await _db.SaveChangesAsync(cancellationToken);
        await EnsureEnabledDefaultAsync(cancellationToken);
        return ToModel(connection);
    }

    public async Task<AiConnectionModel> UpdateAsync(
        Guid id,
        AiConnectionWrite request,
        CancellationToken cancellationToken = default)
    {
        var connection = await _db.AiConnections.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ReportQueryException("AI connection was not found.");
        Apply(connection, request, keepKey: true);
        if (!connection.IsEnabled)
        {
            connection.IsDefault = false;
        }
        else if (request.IsDefault || !await HasEnabledDefaultAsync(id, cancellationToken))
        {
            await ClearDefaultAsync(cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            connection.IsDefault = true;
        }
        else if (!request.IsDefault && connection.IsDefault)
        {
            connection.IsDefault = false;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await EnsureEnabledDefaultAsync(cancellationToken);
        return ToModel(connection);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var connection = await _db.AiConnections.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ReportQueryException("AI connection was not found.");
        var wasDefault = connection.IsDefault;
        _db.AiConnections.Remove(connection);
        await _db.SaveChangesAsync(cancellationToken);
        if (wasDefault)
        {
            await EnsureEnabledDefaultAsync(cancellationToken);
        }
    }

    public async Task<string> TestAsync(AiConnectionWrite request, CancellationToken cancellationToken = default)
    {
        var baseUrl = request.BaseUrl.Trim();
        var model = request.Model.Trim();
        var apiKey = request.ApiKey?.Trim() ?? string.Empty;
        if (apiKey.Length == 0 && request.Id is Guid id)
        {
            apiKey = await _db.AiConnections.AsNoTracking()
                .Where(item => item.Id == id)
                .Select(item => item.ApiKey)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        }

        if (baseUrl.Length == 0 || model.Length == 0 || apiKey.Length == 0)
        {
            throw new ReportQueryException("Endpoint, model, and API key are required to test a connection.");
        }

        if (!TryNormalizeUrl(baseUrl, out var root))
        {
            throw new ReportQueryException("Endpoint must be an http or https URL.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            model,
            max_tokens = 80,
            messages = new[] { new { role = "user", content = "hi" } }
        });
        var client = _httpFactory.CreateClient("OpenAI");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, CombineUrl(root, "chat/completions"))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ReportQueryException("Could not reach the AI endpoint.");
        }

        if (response.IsSuccessStatusCode)
        {
            var reply = await ReadReplyAsync(response, cancellationToken);
            if (string.IsNullOrWhiteSpace(reply))
            {
                throw new ReportQueryException("The model returned an empty reply.");
            }

            return reply.Trim();
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            throw new ReportQueryException("The AI endpoint rejected the API key.");
        }

        var detail = await ReadErrorAsync(response, cancellationToken);
        throw new ReportQueryException(
            string.IsNullOrWhiteSpace(detail)
                ? "The AI endpoint did not accept this connection."
                : detail);
    }

    public async Task<AiConnectionRuntime?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var row = await _db.AiConnections.AsNoTracking()
            .Where(item => item.IsEnabled)
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null || string.IsNullOrWhiteSpace(row.ApiKey))
        {
            return null;
        }

        return new AiConnectionRuntime
        {
            Name = row.Name,
            BaseUrl = row.BaseUrl,
            Model = row.Model,
            ApiKey = row.ApiKey
        };
    }

    private async Task ClearDefaultAsync(CancellationToken cancellationToken)
    {
        var current = await _db.AiConnections.Where(item => item.IsDefault).ToListAsync(cancellationToken);
        foreach (var item in current)
        {
            item.IsDefault = false;
        }
    }

    private static void Apply(AiConnection connection, AiConnectionWrite request, bool keepKey)
    {
        var name = request.Name.Trim();
        var model = request.Model.Trim();
        var baseUrl = request.BaseUrl.Trim();
        if (name.Length is 0 or > 200 || model.Length is 0 or > 100 || baseUrl.Length == 0)
        {
            throw new ReportQueryException("Name, endpoint, and model are required.");
        }

        if (!TryNormalizeUrl(baseUrl, out var root))
        {
            throw new ReportQueryException("Endpoint must be an http or https URL.");
        }

        var apiKey = request.ApiKey?.Trim() ?? string.Empty;
        if (!keepKey && apiKey.Length == 0)
        {
            throw new ReportQueryException("API key is required.");
        }

        if (apiKey.Length > 2000)
        {
            throw new ReportQueryException("API key is too long.");
        }

        connection.Name = name;
        connection.BaseUrl = root;
        connection.Model = model;
        if (!keepKey || apiKey.Length > 0)
        {
            connection.ApiKey = apiKey;
        }

        connection.IsEnabled = request.IsEnabled;
        if (!connection.IsEnabled)
        {
            connection.IsDefault = false;
        }

        connection.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<bool> HasEnabledDefaultAsync(Guid? exceptId, CancellationToken cancellationToken)
        => await _db.AiConnections.AnyAsync(
            item => item.IsEnabled && item.IsDefault && (exceptId == null || item.Id != exceptId),
            cancellationToken);

    private async Task EnsureEnabledDefaultAsync(CancellationToken cancellationToken)
    {
        var stale = await _db.AiConnections
            .Where(item => item.IsDefault && !item.IsEnabled)
            .ToListAsync(cancellationToken);
        foreach (var item in stale)
        {
            item.IsDefault = false;
        }

        var enabled = await _db.AiConnections
            .Where(item => item.IsEnabled)
            .ToListAsync(cancellationToken);
        if (enabled.Count > 0 && enabled.All(item => !item.IsDefault))
        {
            var next = enabled.OrderBy(item => item.Name).First();
            next.IsDefault = true;
            next.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (stale.Count > 0 || enabled.Any(item => _db.Entry(item).State == EntityState.Modified))
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool TryNormalizeUrl(string value, out string root)
    {
        root = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        root = uri.ToString();
        return true;
    }

    internal static string CombineUrl(string? baseUrl, string path)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.openai.com/v1/" : baseUrl.Trim();
        if (!root.EndsWith('/'))
        {
            root += "/";
        }

        return new Uri(new Uri(root), path).ToString();
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length == 0 || body.Length > 2000)
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                var text = message.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Length > 240 ? text[..240] : text;
                }
            }
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static async Task<string> ReadReplyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content");
            var text = content.ValueKind == JsonValueKind.String ? content.GetString() : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            text = text.Trim();
            return text.Length > 1000 ? text[..1000] : text;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static AiConnectionModel ToModel(AiConnection connection)
        => new()
        {
            Id = connection.Id,
            Name = connection.Name,
            BaseUrl = connection.BaseUrl,
            Model = connection.Model,
            HasApiKey = !string.IsNullOrEmpty(connection.ApiKey),
            IsEnabled = connection.IsEnabled,
            IsDefault = connection.IsDefault
        };
}
