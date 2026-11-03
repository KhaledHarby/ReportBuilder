using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Infrastructure.Cache;

public sealed class ClickHouseHttpClient
{
    public const string HttpClientName = "ClickHouse";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _http;
    private readonly ILogger<ClickHouseHttpClient> _logger;

    public ClickHouseHttpClient(IHttpClientFactory http, ILogger<ClickHouseHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task PingAsync(CacheProviderConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, "SELECT 1", cancellationToken);
    }

    public async Task ExecuteAsync(
        CacheProviderConnection connection,
        string sql,
        CancellationToken cancellationToken,
        IReadOnlyList<QueryParameter>? parameters = null)
    {
        using var response = await SendAsync(
            connection,
            sql,
            parameters,
            body: null,
            cancellationToken,
            includeDatabase: false);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<long> ExecuteScalarLongAsync(
        CacheProviderConnection connection,
        string sql,
        CancellationToken cancellationToken,
        IReadOnlyList<QueryParameter>? parameters = null)
    {
        var result = await QueryAsync(connection, sql, cancellationToken, parameters);
        if (result.Rows.Count == 0 || result.Columns.Count == 0)
        {
            return 0;
        }

        var value = result.Rows[0].GetValueOrDefault(result.Columns[0]);
        return value is null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public async Task InsertTsvAsync(
        CacheProviderConnection connection,
        string insertSql,
        string tsv,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            connection,
            insertSql,
            parameters: null,
            body: tsv,
            cancellationToken,
            includeDatabase: true);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<ClickHouseQueryResult> QueryAsync(
        CacheProviderConnection connection,
        string sql,
        CancellationToken cancellationToken,
        IReadOnlyList<QueryParameter>? parameters = null,
        int? skip = null,
        int? take = null)
    {
        var query = sql.Contains("FORMAT", StringComparison.OrdinalIgnoreCase)
            ? sql
            : sql.TrimEnd().TrimEnd(';') + " FORMAT JSON";

        using var response = await SendAsync(
            connection,
            query,
            parameters,
            body: null,
            cancellationToken,
            skip,
            take,
            includeDatabase: true);
        var payload = await EnsureSuccessAsync(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new ClickHouseQueryResult();
        }

        ClickHouseJsonPayload? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ClickHouseJsonPayload>(payload, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ReportQueryException($"ClickHouse returned an unexpected response: {ex.Message}");
        }

        var columns = parsed?.Meta?.Select(column => column.Name ?? string.Empty).ToList() ?? [];
        var rows = new List<Dictionary<string, object?>>();
        foreach (var row in parsed?.Data ?? [])
        {
            var mapped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns)
            {
                mapped[column] = row.TryGetValue(column, out var element)
                    ? Normalize(element)
                    : null;
            }

            rows.Add(mapped);
        }

        return new ClickHouseQueryResult
        {
            Columns = columns,
            Rows = rows
        };
    }

    private async Task<HttpResponseMessage> SendAsync(
        CacheProviderConnection connection,
        string sql,
        IReadOnlyList<QueryParameter>? parameters,
        string? body,
        CancellationToken cancellationToken,
        int? skip = null,
        int? take = null,
        bool includeDatabase = true)
    {
        var normalized = Normalize(connection);
        Validate(normalized);
        var client = _http.CreateClient(HttpClientName);
        var uri = BuildUri(normalized, parameters, skip, take, includeDatabase: includeDatabase);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        var username = string.IsNullOrWhiteSpace(normalized.Username) ? "default" : normalized.Username.Trim();
        request.Headers.TryAddWithoutValidation("X-ClickHouse-User", username);
        if (!string.IsNullOrEmpty(normalized.Password))
        {
            request.Headers.TryAddWithoutValidation("X-ClickHouse-Key", normalized.Password);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{normalized.Password}")));
        }

        if (includeDatabase && !string.IsNullOrWhiteSpace(normalized.Database))
        {
            request.Headers.TryAddWithoutValidation("X-ClickHouse-Database", normalized.Database.Trim());
        }

        request.Headers.ExpectContinue = false;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        request.Content = new StringContent(body ?? sql, Encoding.UTF8, "text/plain");
        if (body is not null)
        {
            request.RequestUri = BuildUri(
                normalized,
                parameters,
                skip,
                take,
                queryOverride: sql,
                includeDatabase: includeDatabase);
        }

        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReportQueryException("ClickHouse connection timed out.");
        }
        catch (Exception ex) when (ex is not ReportQueryException and not OperationCanceledException)
        {
            throw new ReportQueryException(
                "Could not reach ClickHouse. For ClickHouse Cloud paste the hostname only, set port 8443, and enable HTTPS. "
                + Truncate(ex.GetBaseException().Message, 240));
        }
    }

    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(5),
            UseProxy = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12
            },
            ConnectCallback = ConnectIPv4Async
        };
    }

    private static async ValueTask<Stream> ConnectIPv4Async(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken);
        }
        catch (SocketException)
        {
            addresses = [];
        }

        if (addresses.Length == 0)
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }

        var address = addresses.FirstOrDefault()
            ?? throw new HttpRequestException($"Could not resolve {host}.");
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        try
        {
            await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static CacheProviderConnection Normalize(CacheProviderConnection connection)
    {
        var copy = new CacheProviderConnection
        {
            Host = connection.Host ?? string.Empty,
            Port = connection.Port,
            Database = connection.Database ?? string.Empty,
            Username = connection.Username ?? string.Empty,
            Password = connection.Password ?? string.Empty,
            Secure = connection.Secure,
            ConnectionString = connection.ConnectionString ?? string.Empty,
            RedisDatabase = connection.RedisDatabase,
            HasPassword = connection.HasPassword
        };

        var raw = copy.Host.Trim();
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            copy.Secure = uri.Scheme == Uri.UriSchemeHttps;
            copy.Host = uri.IdnHost;
            if (!uri.IsDefaultPort)
            {
                copy.Port = uri.Port;
            }
        }
        else
        {
            raw = raw.TrimEnd('/');
            var colon = raw.LastIndexOf(':');
            if (colon > 0
                && raw.IndexOf(':') == colon
                && int.TryParse(raw[(colon + 1)..], out var parsedPort)
                && parsedPort is > 0 and < 65536)
            {
                copy.Host = raw[..colon];
                copy.Port = parsedPort;
            }
            else
            {
                copy.Host = raw;
            }
        }

        copy.Host = copy.Host.Trim();
        var cloud = copy.Host.Contains("clickhouse.cloud", StringComparison.OrdinalIgnoreCase);
        if (cloud)
        {
            copy.Secure = true;
            if (copy.Port is <= 0 or 8123)
            {
                copy.Port = 8443;
            }
        }

        if (copy.Port <= 0)
        {
            copy.Port = copy.Secure ? 8443 : 8123;
        }

        if (string.IsNullOrWhiteSpace(copy.Username))
        {
            copy.Username = "default";
        }

        if (string.IsNullOrWhiteSpace(copy.Database))
        {
            copy.Database = cloud ? "default" : "reportbuilder";
        }

        return copy;
    }

    private static Uri BuildUri(
        CacheProviderConnection connection,
        IReadOnlyList<QueryParameter>? parameters,
        int? skip,
        int? take,
        string? queryOverride = null,
        bool includeDatabase = true)
    {
        var builder = new UriBuilder
        {
            Scheme = connection.Secure ? "https" : "http",
            Host = connection.Host.Trim(),
            Port = connection.Port <= 0 ? (connection.Secure ? 8443 : 8123) : connection.Port,
            Path = "/"
        };

        var query = new StringBuilder("wait_end_of_query=1");
        if (includeDatabase && !string.IsNullOrWhiteSpace(connection.Database))
        {
            query.Append("&database=").Append(Uri.EscapeDataString(connection.Database.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(queryOverride))
        {
            query.Append("&query=").Append(Uri.EscapeDataString(queryOverride));
        }

        void AddParam(string name, object? value)
        {
            query.Append("&param_").Append(Uri.EscapeDataString(QueryDialect.ParamName(name)))
                .Append('=').Append(Uri.EscapeDataString(FormatParam(value)));
        }

        if (parameters is not null)
        {
            foreach (var parameter in parameters)
            {
                AddParam(parameter.Name, parameter.Value);
            }
        }

        if (skip is not null)
        {
            AddParam("skip", skip.Value);
        }

        if (take is not null)
        {
            AddParam("take", take.Value);
        }

        builder.Query = query.ToString();
        return builder.Uri;
    }

    private async Task<string> EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return payload;
        }

        var detail = string.IsNullOrWhiteSpace(payload)
            ? response.ReasonPhrase ?? response.StatusCode.ToString()
            : Truncate(payload.ReplaceLineEndings(" ").Trim(), 800);
        _logger.LogWarning("ClickHouse request failed ({Status}): {Detail}", (int)response.StatusCode, detail);
        throw new ReportQueryException($"ClickHouse request failed: {detail}");
    }

    public static void Validate(CacheProviderConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Host))
        {
            throw new ReportQueryException(
                "ClickHouse host is required. Paste the Cloud hostname only, for example your-service.region.azure.clickhouse.cloud");
        }
    }

    public static string FormatParam(object? value)
        => value switch
        {
            null or DBNull => "",
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            bool flag => flag ? "1" : "0",
            Guid guid => guid.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

    private static object? Normalize(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l
                : element.TryGetDecimal(out var m) ? m
                : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array or JsonValueKind.Object => element.GetRawText(),
            _ => element.ToString()
        };

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private sealed class ClickHouseJsonPayload
    {
        public List<ClickHouseJsonColumn>? Meta { get; set; }
        public List<Dictionary<string, JsonElement>>? Data { get; set; }
    }

    private sealed class ClickHouseJsonColumn
    {
        public string? Name { get; set; }
    }
}

public sealed class ClickHouseQueryResult
{
    public IReadOnlyList<string> Columns { get; init; } = [];
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];
}
