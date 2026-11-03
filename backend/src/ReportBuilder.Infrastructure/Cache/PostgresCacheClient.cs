using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Npgsql;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Infrastructure.Cache;

public sealed class PostgresCacheClient : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<NpgsqlDataSource>> _pool = new(StringComparer.Ordinal);
    private readonly ILogger<PostgresCacheClient> _logger;

    public PostgresCacheClient(ILogger<PostgresCacheClient> logger)
    {
        _logger = logger;
    }

    public async Task PingAsync(CacheProviderConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteScalarAsync(connection, "SELECT 1", cancellationToken);
    }

    public async Task ExecuteAsync(
        CacheProviderConnection connection,
        string sql,
        CancellationToken cancellationToken,
        IReadOnlyList<QueryParameter>? parameters = null)
    {
        await using var db = await OpenConnectionAsync(connection, cancellationToken);
        await using var command = CreateCommand(db, sql, parameters);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ReportQueryException)
        {
            throw Wrap(ex, "Could not run a command on PostgreSQL.");
        }
    }

    public async Task<long> ExecuteScalarLongAsync(
        CacheProviderConnection connection,
        string sql,
        CancellationToken cancellationToken,
        IReadOnlyList<QueryParameter>? parameters = null)
    {
        var value = await ExecuteScalarAsync(connection, sql, cancellationToken, parameters);
        return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public async Task<object?> ExecuteScalarAsync(
        CacheProviderConnection connection,
        string sql,
        CancellationToken cancellationToken,
        IReadOnlyList<QueryParameter>? parameters = null)
    {
        await using var db = await OpenConnectionAsync(connection, cancellationToken);
        await using var command = CreateCommand(db, sql, parameters);
        try
        {
            return await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ReportQueryException)
        {
            throw Wrap(ex, "Could not query PostgreSQL.");
        }
    }

    public async Task<PostgresQueryResult> QueryAsync(
        CacheProviderConnection connection,
        string sql,
        CancellationToken cancellationToken,
        IReadOnlyList<QueryParameter>? parameters = null,
        int? skip = null,
        int? take = null)
    {
        await using var db = await OpenConnectionAsync(connection, cancellationToken);
        await using var command = CreateCommand(db, sql, parameters);
        if (skip is int skipValue && !HasParameter(command, "skip"))
        {
            command.Parameters.AddWithValue("skip", skipValue);
        }

        if (take is int takeValue && !HasParameter(command, "take"))
        {
            command.Parameters.AddWithValue("take", takeValue);
        }

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var columns = new List<string>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            var rows = new List<IReadOnlyDictionary<string, object?>>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                rows.Add(row);
            }

            return new PostgresQueryResult
            {
                Columns = columns,
                Rows = rows
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ReportQueryException)
        {
            throw Wrap(ex, "Could not query PostgreSQL.");
        }
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync(
        CacheProviderConnection connection,
        CancellationToken cancellationToken)
    {
        var source = GetDataSource(connection);
        try
        {
            return await source.OpenConnectionAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ReportQueryException)
        {
            throw Wrap(ex, "Could not reach PostgreSQL. Check host, port, SSL, database, and credentials.");
        }
    }

    public static CacheProviderConnection Normalize(CacheProviderConnection connection)
    {
        var copy = new CacheProviderConnection
        {
            Host = connection.Host?.Trim() ?? string.Empty,
            Port = connection.Port,
            Database = connection.Database?.Trim() ?? string.Empty,
            Username = connection.Username?.Trim() ?? string.Empty,
            Password = connection.Password ?? string.Empty,
            Secure = connection.Secure,
            ConnectionString = connection.ConnectionString?.Trim() ?? string.Empty,
            HasPassword = connection.HasPassword
        };

        var raw = LooksLikePostgresUrl(copy.ConnectionString)
            ? copy.ConnectionString
            : LooksLikePostgresUrl(copy.Host) || LooksLikeKeywordConnection(copy.Host)
                ? copy.Host
                : null;

        if (raw is not null)
        {
            try
            {
                var parsed = new NpgsqlConnectionStringBuilder(raw);
                if (!string.IsNullOrWhiteSpace(parsed.Host))
                {
                    copy.Host = parsed.Host;
                }

                if (parsed.Port > 0)
                {
                    copy.Port = parsed.Port;
                }

                if (!string.IsNullOrWhiteSpace(parsed.Database) && string.IsNullOrWhiteSpace(copy.Database))
                {
                    copy.Database = parsed.Database;
                }

                if (!string.IsNullOrWhiteSpace(parsed.Username) && string.IsNullOrWhiteSpace(copy.Username))
                {
                    copy.Username = parsed.Username;
                }

                if (!string.IsNullOrEmpty(parsed.Password) && string.IsNullOrEmpty(copy.Password))
                {
                    copy.Password = parsed.Password;
                }

                if (parsed.SslMode is SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull)
                {
                    copy.Secure = true;
                }

                if (LooksLikePostgresUrl(copy.Host) || LooksLikeKeywordConnection(copy.Host))
                {
                    copy.ConnectionString = LooksLikePostgresUrl(copy.ConnectionString) || LooksLikeKeywordConnection(copy.ConnectionString)
                        ? copy.ConnectionString
                        : raw;
                    if (LooksLikePostgresUrl(copy.Host) || LooksLikeKeywordConnection(copy.Host))
                    {
                        copy.Host = parsed.Host ?? string.Empty;
                    }
                }
            }
            catch (Exception)
            {
                /* keep the typed fields */
            }
        }
        else
        {
            var colon = copy.Host.LastIndexOf(':');
            if (colon > 0
                && copy.Host.IndexOf(':') == colon
                && int.TryParse(copy.Host[(colon + 1)..], out var parsedPort)
                && parsedPort is > 0 and < 65536)
            {
                copy.Host = copy.Host[..colon];
                copy.Port = parsedPort;
            }
        }

        copy.Host = copy.Host.Trim();
        if (LooksLikeCloud(copy.Host))
        {
            copy.Secure = true;
        }

        if (copy.Port <= 0)
        {
            copy.Port = 5432;
        }

        if (string.IsNullOrWhiteSpace(copy.Database))
        {
            copy.Database = "reportbuilder";
        }

        copy.HasPassword = !string.IsNullOrEmpty(copy.Password);
        return copy;
    }

    public static void Validate(CacheProviderConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Host)
            && !LooksLikePostgresUrl(connection.ConnectionString)
            && !LooksLikeKeywordConnection(connection.ConnectionString))
        {
            throw new ReportQueryException(
                "PostgreSQL host is required. Paste the hostname, host:port, or a postgres:// connection string.");
        }
    }

    public void Dispose()
    {
        foreach (var entry in _pool.Values)
        {
            if (!entry.IsValueCreated)
            {
                continue;
            }

            try
            {
                entry.Value.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not dispose a PostgreSQL data source.");
            }
        }

        _pool.Clear();
    }

    private NpgsqlDataSource GetDataSource(CacheProviderConnection connection)
    {
        var normalized = Normalize(connection);
        Validate(normalized);
        var key = Fingerprint(normalized);
        var lazy = _pool.GetOrAdd(key, _ => new Lazy<NpgsqlDataSource>(
            () => CreateDataSource(normalized),
            LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return lazy.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _pool.TryRemove(key, out _);
            if (ex is ReportQueryException)
            {
                throw;
            }

            throw Wrap(ex, "PostgreSQL connection settings are invalid.");
        }
    }

    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection db,
        string sql,
        IReadOnlyList<QueryParameter>? parameters)
    {
        var command = db.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 600;
        if (parameters is not null)
        {
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(QueryDialect.ParamName(parameter.Name), parameter.Value ?? DBNull.Value);
            }
        }

        return command;
    }

    private static bool HasParameter(NpgsqlCommand command, string name)
        => command.Parameters.Any(parameter =>
            string.Equals(QueryDialect.ParamName(parameter.ParameterName), name, StringComparison.OrdinalIgnoreCase));

    private static NpgsqlDataSource CreateDataSource(CacheProviderConnection connection)
    {
        try
        {
            return new NpgsqlDataSourceBuilder(BuildConnectionString(connection)).Build();
        }
        catch (Exception ex) when (ex is not ReportQueryException)
        {
            throw Wrap(ex, "PostgreSQL connection settings are invalid.");
        }
    }

    private static string BuildConnectionString(CacheProviderConnection connection)
    {
        NpgsqlConnectionStringBuilder builder;
        if (LooksLikePostgresUrl(connection.ConnectionString) || LooksLikeKeywordConnection(connection.ConnectionString))
        {
            builder = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        }
        else
        {
            builder = new NpgsqlConnectionStringBuilder
            {
                Host = connection.Host.Trim(),
                Port = connection.Port <= 0 ? 5432 : connection.Port,
                Database = string.IsNullOrWhiteSpace(connection.Database) ? "reportbuilder" : connection.Database.Trim(),
                SslMode = connection.Secure ? SslMode.Require : SslMode.Disable,
                Timeout = 15,
                CommandTimeout = 600,
                ApplicationName = "ReportBuilder"
            };
        }

        if (!string.IsNullOrWhiteSpace(connection.Host))
        {
            builder.Host = connection.Host.Trim();
        }

        if (connection.Port > 0)
        {
            builder.Port = connection.Port;
        }

        if (!string.IsNullOrWhiteSpace(connection.Database))
        {
            builder.Database = connection.Database.Trim();
        }

        if (!string.IsNullOrWhiteSpace(connection.Username))
        {
            builder.Username = connection.Username.Trim();
        }

        if (!string.IsNullOrEmpty(connection.Password))
        {
            builder.Password = connection.Password;
        }

        builder.SslMode = connection.Secure ? SslMode.Require : SslMode.Disable;
        if (builder.Timeout <= 0)
        {
            builder.Timeout = 15;
        }

        builder.CommandTimeout = Math.Max(builder.CommandTimeout, 600);
        if (string.IsNullOrWhiteSpace(builder.ApplicationName))
        {
            builder.ApplicationName = "ReportBuilder";
        }

        return builder.ToString();
    }

    private static string Fingerprint(CacheProviderConnection connection)
        => string.Join('|',
            connection.Host.Trim().ToLowerInvariant(),
            connection.Port,
            connection.Username.Trim(),
            connection.Database.Trim(),
            connection.Secure ? "s" : "p",
            connection.ConnectionString.Trim(),
            connection.Password);

    private static bool LooksLikePostgresUrl(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeKeywordConnection(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Contains('=', StringComparison.Ordinal)
            && (value.Contains("Host=", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Server=", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Username=", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeCloud(string host)
        => host.Contains("neon.tech", StringComparison.OrdinalIgnoreCase)
            || host.Contains("supabase.co", StringComparison.OrdinalIgnoreCase)
            || host.Contains("timescale.cloud", StringComparison.OrdinalIgnoreCase)
            || host.Contains("azure.com", StringComparison.OrdinalIgnoreCase)
            || host.Contains("rds.amazonaws.com", StringComparison.OrdinalIgnoreCase);

    private static ReportQueryException Wrap(Exception ex, string message)
        => new(message + " " + Truncate(ex.GetBaseException().Message, 240));

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max].TrimEnd() + "…";
}

public sealed class PostgresQueryResult
{
    public IReadOnlyList<string> Columns { get; init; } = [];
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];
}
