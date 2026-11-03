using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Infrastructure.Cache;

public sealed partial class MongoCacheClient : IDisposable
{
    internal const string MetaId = "__rb_meta__";
    internal const string NamedFormat = "named";
    private readonly ConcurrentDictionary<string, Lazy<MongoClient>> _pool = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _databaseNames = new(StringComparer.Ordinal);
    private readonly ILogger<MongoCacheClient> _logger;

    public MongoCacheClient(ILogger<MongoCacheClient> logger)
    {
        _logger = logger;
    }

    public async Task PingAsync(CacheProviderConnection connection, CancellationToken cancellationToken)
    {
        var database = await GetDatabaseAsync(connection, cancellationToken);
        try
        {
            await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ReportQueryException)
        {
            throw new ReportQueryException(
                "Could not reach MongoDB. Check the host, port, TLS, connection string, and credentials. "
                + Truncate(ex.GetBaseException().Message, 240));
        }
    }

    private const int InsertBatchRows = 2_000;

    public Task<MongoStoreResult> ReplaceSnapshotAsync(
        CacheProviderConnection connection,
        Guid datasetId,
        DatasetCacheSnapshot snapshot,
        CancellationToken cancellationToken)
        => ReplaceSnapshotAsync(
            connection,
            datasetId,
            snapshot.Columns,
            StreamSnapshot(snapshot),
            cancellationToken);

    public async Task<MongoStoreResult> ReplaceSnapshotAsync(
        CacheProviderConnection connection,
        Guid datasetId,
        IReadOnlyList<string> columns,
        IAsyncEnumerable<object?[]> rows,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? indexedColumns = null)
    {
        var database = await GetDatabaseAsync(connection, cancellationToken);
        var final = DatasetCacheRouting.MongoCollectionNameFor(datasetId);
        var staging = DatasetCacheRouting.MongoStagingCollectionNameFor(datasetId);

        await database.DropCollectionAsync(staging, cancellationToken);
        var collection = database.GetCollection<BsonDocument>(staging);
        var columnNames = new BsonArray(columns.Select(BsonValue.Create));
        await collection.InsertOneAsync(
            new BsonDocument
            {
                { "_id", MetaId },
                { "format", NamedFormat },
                { "columns", columnNames },
                { "rowCount", 0L }
            },
            cancellationToken: cancellationToken);

        var docs = new List<BsonDocument>(InsertBatchRows);
        var rowCount = 0L;
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            var doc = new BsonDocument();
            for (var i = 0; i < columns.Count; i++)
            {
                doc[FieldName(i)] = ToBson(i < row.Length ? row[i] : null);
            }

            docs.Add(doc);
            rowCount++;
            if (docs.Count < InsertBatchRows)
            {
                continue;
            }

            await collection.InsertManyAsync(docs, new InsertManyOptions { IsOrdered = false }, cancellationToken);
            docs.Clear();
            if (rowCount % 250_000 == 0)
            {
                _logger.LogInformation(
                    "Mongo dataset cache {Collection} wrote {Rows} rows.",
                    staging,
                    rowCount);
            }
        }

        if (docs.Count > 0)
        {
            await collection.InsertManyAsync(docs, new InsertManyOptions { IsOrdered = false }, cancellationToken);
        }

        await collection.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", MetaId),
            new BsonDocument
            {
                { "_id", MetaId },
                { "format", NamedFormat },
                { "columns", columnNames },
                { "rowCount", rowCount }
            },
            cancellationToken: cancellationToken);

        await CreateIndexesAsync(collection, columns, indexedColumns, cancellationToken);

        await database.DropCollectionAsync(final, cancellationToken);
        await database.RenameCollectionAsync(staging, final, cancellationToken: cancellationToken);

        long? sizeBytes = null;
        try
        {
            var stats = await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("collStats", final),
                cancellationToken: cancellationToken);
            if (stats.TryGetValue("size", out var size) && size.IsNumeric)
            {
                sizeBytes = size.ToInt64();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation(ex, "Could not estimate MongoDB size for {Collection}.", final);
        }

        return new MongoStoreResult(final, rowCount, sizeBytes);
    }

    private static async IAsyncEnumerable<object?[]> StreamSnapshot(DatasetCacheSnapshot snapshot)
    {
        foreach (var row in snapshot.Rows)
        {
            var values = new object?[snapshot.Columns.Count];
            for (var i = 0; i < snapshot.Columns.Count; i++)
            {
                values[i] = row.TryGetValue(snapshot.Columns[i], out var value) ? value : null;
            }

            yield return values;
        }

        await Task.CompletedTask;
    }

    public async Task<DatasetCacheSnapshot> GetSnapshotAsync(
        CacheProviderConnection connection,
        string collectionName,
        CancellationToken cancellationToken)
    {
        var database = await GetDatabaseAsync(connection, cancellationToken);
        var collection = database.GetCollection<BsonDocument>(collectionName);
        var meta = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", MetaId))
            .FirstOrDefaultAsync(cancellationToken);
        if (meta is null)
        {
            throw new ReportQueryException("The MongoDB dataset cache was not found. Refresh the cache and try again.");
        }

        var columns = meta.GetValue("columns", new BsonArray())
            .AsBsonArray
            .Select(item => item.AsString)
            .ToList();
        var format = meta.GetValue("format", "").IsString ? meta["format"].AsString : string.Empty;
        var named = string.Equals(format, NamedFormat, StringComparison.OrdinalIgnoreCase);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        using var cursor = await collection.Find(Builders<BsonDocument>.Filter.Ne("_id", MetaId))
            .ToCursorAsync(cancellationToken);
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var document in cursor.Current)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                if (named)
                {
                    for (var i = 0; i < columns.Count; i++)
                    {
                        row[columns[i]] = document.TryGetValue(FieldName(i), out var cell)
                            ? FromBson(cell)
                            : null;
                    }
                }
                else
                {
                    var values = document.GetValue("v", new BsonArray()).AsBsonArray;
                    for (var i = 0; i < columns.Count; i++)
                    {
                        row[columns[i]] = i < values.Count ? FromBson(values[i]) : null;
                    }
                }

                rows.Add(row);
            }
        }

        return new DatasetCacheSnapshot
        {
            Columns = columns,
            Rows = rows
        };
    }

    public async Task DropDatasetCollectionsAsync(
        CacheProviderConnection connection,
        Guid datasetId,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(connection);
        if (string.IsNullOrWhiteSpace(normalized.Host)
            && string.IsNullOrWhiteSpace(normalized.ConnectionString))
        {
            return;
        }

        var database = await GetDatabaseAsync(normalized, cancellationToken);
        await database.DropCollectionAsync(DatasetCacheRouting.MongoCollectionNameFor(datasetId), cancellationToken);
        await database.DropCollectionAsync(DatasetCacheRouting.MongoStagingCollectionNameFor(datasetId), cancellationToken);
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

        var raw = copy.ConnectionString.Trim();
        if (raw.Length == 0)
        {
            raw = copy.Host.Trim();
        }

        if (LooksLikeMongoUrl(raw))
        {
            try
            {
                var url = new MongoUrl(raw);
                var server = url.Servers.FirstOrDefault();
                if (server is not null)
                {
                    copy.Host = server.Host;
                    if (server.Port > 0)
                    {
                        copy.Port = server.Port;
                    }
                }

                if (!string.IsNullOrWhiteSpace(url.DatabaseName))
                {
                    // Keep the Settings database field when it only differs by case.
                    // A URI path of /reportbuilder would otherwise recreate [reportbuilder]
                    // next to the existing Windows database [ReportBuilder] (error 13297).
                    if (string.IsNullOrWhiteSpace(copy.Database)
                        || !string.Equals(copy.Database, url.DatabaseName, StringComparison.OrdinalIgnoreCase))
                    {
                        copy.Database = url.DatabaseName;
                    }
                }

                if (!string.IsNullOrWhiteSpace(url.Username))
                {
                    copy.Username = url.Username;
                }

                if (!string.IsNullOrEmpty(url.Password))
                {
                    copy.Password = url.Password;
                }

                copy.Secure = url.UseTls
                    || raw.StartsWith("mongodb+srv", StringComparison.OrdinalIgnoreCase);
                copy.ConnectionString = StripPassword(url.ToString());
            }
            catch (Exception)
            {
                copy.Host = copy.Host.Trim();
            }
        }
        else
        {
            var host = copy.Host.Trim().TrimEnd('/');
            var colon = host.LastIndexOf(':');
            if (colon > 0
                && host.IndexOf(':') == colon
                && int.TryParse(host[(colon + 1)..], out var parsedPort)
                && parsedPort is > 0 and < 65536)
            {
                copy.Host = host[..colon];
                copy.Port = parsedPort;
            }
            else
            {
                copy.Host = host;
            }
        }

        copy.Host = copy.Host.Trim();
        var atlas = copy.Host.Contains("mongodb.net", StringComparison.OrdinalIgnoreCase)
            || LooksLikeMongoUrl(copy.ConnectionString)
                && copy.ConnectionString.Contains("mongodb+srv", StringComparison.OrdinalIgnoreCase);
        if (atlas)
        {
            copy.Secure = true;
        }

        if (copy.Port <= 0 && !atlas)
        {
            copy.Port = 27017;
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
            && !LooksLikeMongoUrl(connection.ConnectionString))
        {
            throw new ReportQueryException(
                "MongoDB host is required. Paste the hostname, host:port, or a mongodb:// / mongodb+srv:// connection string.");
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
                _logger.LogDebug(ex, "Could not dispose a MongoDB client.");
            }
        }

        _pool.Clear();
        _databaseNames.Clear();
    }

    private async Task<IMongoDatabase> GetDatabaseAsync(
        CacheProviderConnection connection,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(connection);
        Validate(normalized);
        var key = Fingerprint(normalized);
        var lazy = _pool.GetOrAdd(key, _ => new Lazy<MongoClient>(
            () => CreateClient(normalized),
            LazyThreadSafetyMode.ExecutionAndPublication));

        MongoClient client;
        try
        {
            client = lazy.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _pool.TryRemove(key, out _);
            if (ex is ReportQueryException)
            {
                throw;
            }

            throw new ReportQueryException(
                "Could not reach MongoDB. Check the host, port, TLS, connection string, and credentials. "
                + Truncate(ex.GetBaseException().Message, 240));
        }

        var databaseName = await ResolveDatabaseNameAsync(client, key, normalized.Database, cancellationToken);
        return client.GetDatabase(databaseName);
    }

    private async Task<string> ResolveDatabaseNameAsync(
        IMongoClient client,
        string fingerprint,
        string requested,
        CancellationToken cancellationToken)
    {
        requested = requested.Trim();
        if (_databaseNames.TryGetValue(fingerprint, out var cached)
            && string.Equals(cached, requested, StringComparison.OrdinalIgnoreCase))
        {
            return cached;
        }

        try
        {
            var names = await (await client.ListDatabaseNamesAsync(cancellationToken: cancellationToken))
                .ToListAsync(cancellationToken);
            var match = names.FirstOrDefault(name =>
                string.Equals(name, requested, StringComparison.OrdinalIgnoreCase));
            var resolved = string.IsNullOrWhiteSpace(match) ? requested : match;
            _databaseNames[fingerprint] = resolved;
            if (!string.Equals(resolved, requested, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Using existing MongoDB database {Existing} instead of {Requested}.",
                    resolved,
                    requested);
            }

            return resolved;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not resolve MongoDB database name case for {Database}.", requested);
            return requested;
        }
    }

    private static MongoClient CreateClient(CacheProviderConnection connection)
    {
        try
        {
            return new MongoClient(BuildClientUrl(connection));
        }
        catch (Exception ex) when (ex is not ReportQueryException)
        {
            throw new ReportQueryException(
                "MongoDB connection settings are invalid. "
                + Truncate(ex.GetBaseException().Message, 240));
        }
    }

    private static string BuildClientUrl(CacheProviderConnection connection)
    {
        if (LooksLikeMongoUrl(connection.ConnectionString))
        {
            var builder = new MongoUrlBuilder(connection.ConnectionString);
            if (!string.IsNullOrWhiteSpace(connection.Username))
            {
                builder.Username = connection.Username.Trim();
            }

            if (!string.IsNullOrEmpty(connection.Password))
            {
                builder.Password = connection.Password;
            }

            if (!string.IsNullOrWhiteSpace(connection.Database))
            {
                builder.DatabaseName = connection.Database.Trim();
            }

            builder.UseTls = connection.Secure || IsSrv(connection);
            builder.ConnectTimeout = TimeSpan.FromSeconds(15);
            builder.ServerSelectionTimeout = TimeSpan.FromSeconds(15);
            return builder.ToString();
        }

        var scheme = IsSrv(connection) ? "mongodb+srv" : "mongodb";
        var auth = string.Empty;
        if (!string.IsNullOrWhiteSpace(connection.Username))
        {
            auth = Uri.EscapeDataString(connection.Username.Trim());
            if (!string.IsNullOrEmpty(connection.Password))
            {
                auth += ":" + Uri.EscapeDataString(connection.Password);
            }

            auth += "@";
        }

        var host = IsSrv(connection) || connection.Port <= 0
            ? connection.Host.Trim()
            : $"{connection.Host.Trim()}:{connection.Port}";
        var database = string.IsNullOrWhiteSpace(connection.Database)
            ? "reportbuilder"
            : Uri.EscapeDataString(connection.Database.Trim());
        var query = connection.Secure && !IsSrv(connection) ? "?tls=true" : string.Empty;
        return $"{scheme}://{auth}{host}/{database}{query}";
    }

    private static string StripPassword(string url)
    {
        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        var at = url.IndexOf('@');
        if (scheme < 0 || at < 0 || at < scheme)
        {
            return url;
        }

        var userInfo = url[(scheme + 3)..at];
        var colon = userInfo.IndexOf(':');
        if (colon < 0)
        {
            return url;
        }

        return url[..(scheme + 3)] + userInfo[..colon] + url[at..];
    }

    private static bool IsSrv(CacheProviderConnection connection)
        => connection.Host.Contains("mongodb.net", StringComparison.OrdinalIgnoreCase)
            || connection.ConnectionString.Contains("mongodb+srv", StringComparison.OrdinalIgnoreCase)
            || connection.Port <= 0;

    private static bool LooksLikeMongoUrl(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && (value.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase));

    private static string Fingerprint(CacheProviderConnection connection)
        => string.Join('|',
            connection.Host.Trim().ToLowerInvariant(),
            connection.Port,
            connection.Username.Trim(),
            connection.Database.Trim(),
            connection.Secure ? "s" : "p",
            connection.ConnectionString.Trim(),
            connection.Password);

    internal static string FieldName(int index) => "c" + index.ToString(CultureInfo.InvariantCulture);

    private async Task CreateIndexesAsync(
        IMongoCollection<BsonDocument> collection,
        IReadOnlyList<string> columns,
        IReadOnlyList<string>? indexedColumns,
        CancellationToken cancellationToken)
    {
        if (indexedColumns is null || indexedColumns.Count == 0)
        {
            return;
        }

        var lookup = columns
            .Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        var created = 0;
        foreach (var column in indexedColumns)
        {
            if (created >= 32 || !lookup.TryGetValue(column, out var index))
            {
                continue;
            }

            var field = FieldName(index);
            try
            {
                await collection.Indexes.CreateOneAsync(
                    new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys.Ascending(field),
                        new CreateIndexOptions { Name = "ix_" + field }),
                    cancellationToken: cancellationToken);
                created++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation(ex, "Could not create Mongo index {Field} on cache collection.", field);
            }
        }
    }

    private static BsonValue ToBson(object? value)
        => value switch
        {
            null or DBNull => BsonNull.Value,
            string text => text,
            bool flag => flag,
            int i => i,
            long l => l,
            short s => s,
            byte b => b,
            decimal m => (Decimal128)m,
            double d => d,
            float f => f,
            DateTime dt => dt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : dt.ToUniversalTime(),
            DateTimeOffset dto => dto.UtcDateTime,
            Guid guid => guid.ToString(),
            byte[] bytes => new BsonBinaryData(bytes),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) is { } formatted
                ? (BsonValue)formatted
                : BsonNull.Value,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) is { } text
                ? (BsonValue)text
                : BsonNull.Value
        };

    private static object? FromBson(BsonValue value)
        => value.BsonType switch
        {
            BsonType.Null or BsonType.Undefined => null,
            BsonType.String => value.AsString,
            BsonType.Boolean => value.AsBoolean,
            BsonType.Int32 => value.AsInt32,
            BsonType.Int64 => value.AsInt64,
            BsonType.Double => value.AsDouble,
            BsonType.Decimal128 => (decimal)value.AsDecimal128,
            BsonType.DateTime => value.ToUniversalTime(),
            BsonType.Binary => Convert.ToHexString(value.AsByteArray),
            BsonType.ObjectId => value.AsObjectId.ToString(),
            _ => value.ToString()
        };

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}

public readonly record struct MongoStoreResult(string CollectionName, long RowCount, long? SizeBytes);
