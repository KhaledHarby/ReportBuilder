using System.Text;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public sealed class QueryDialect
{
    public static QueryDialect SqlServer { get; } = new(CacheProviders.SqlServer);

    public static QueryDialect ClickHouse(string database, string table)
        => new(CacheProviders.ClickHouse)
        {
            Database = database,
            Table = table
        };

    public static QueryDialect Postgres(string schema, string table)
        => new(CacheProviders.Postgres)
        {
            Database = schema,
            Table = table
        };

    private QueryDialect(string provider)
    {
        Provider = provider;
    }

    public string Provider { get; }
    public string? Database { get; private init; }
    public string? Table { get; private init; }

    public bool IsClickHouse =>
        string.Equals(Provider, CacheProviders.ClickHouse, StringComparison.OrdinalIgnoreCase);

    public bool IsPostgres =>
        string.Equals(Provider, CacheProviders.Postgres, StringComparison.OrdinalIgnoreCase);

    public bool UsesExternalTable =>
        (IsClickHouse || IsPostgres)
        && !string.IsNullOrWhiteSpace(Database)
        && !string.IsNullOrWhiteSpace(Table);

    public bool UsesLimitPaging => IsClickHouse || IsPostgres;

    public string Quote(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (IsClickHouse)
        {
            return "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";
        }

        if (IsPostgres)
        {
            return "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return SqlIdentifiers.Quote(name);
    }

    public string Qualify(string schema, string objectName)
        => $"{Quote(schema)}.{Quote(objectName)}";

    public string FromClause(DatasetCacheRoutingTarget target)
        => Qualify(target.Schema, target.ObjectName);

    public void AppendPaging(StringBuilder sql, bool allRows)
    {
        if (allRows)
        {
            return;
        }

        if (IsClickHouse)
        {
            sql.Append(" LIMIT {take:UInt32} OFFSET {skip:UInt64}");
            return;
        }

        if (IsPostgres)
        {
            sql.Append(" LIMIT @take OFFSET @skip");
            return;
        }

        sql.Append(" OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY");
    }

    public string DistinctHead(int takePlusOne, string quotedColumn)
        => UsesLimitPaging
            ? $"SELECT DISTINCT {quotedColumn} AS {Quote("value")}"
            : $"SELECT DISTINCT TOP ({takePlusOne}) {quotedColumn} AS [value]";

    public string DistinctTail(int takePlusOne)
        => UsesLimitPaging ? $" LIMIT {takePlusOne}" : string.Empty;

    public string CountAlias => IsClickHouse ? "q" : IsPostgres ? Quote("_q") : "[_q]";

    public string DummyColumn => IsClickHouse ? "_x" : IsPostgres ? Quote("_x") : "[_x]";

    public string ContainsPredicate(string quotedColumn, string parameterName)
    {
        if (IsClickHouse)
        {
            return $"positionCaseInsensitive(toString({quotedColumn}), {Bind(parameterName, "nvarchar")}) > 0";
        }

        if (IsPostgres)
        {
            return $"{quotedColumn}::text ILIKE {parameterName} ESCAPE '\\'";
        }

        return $"{quotedColumn} LIKE {parameterName} ESCAPE '\\'";
    }

    public string Bind(string parameterName, string? sqlType = null)
        => IsClickHouse
            ? $"{{{ParamName(parameterName)}:{ToClickHouseParamType(parameterName, sqlType)}}}"
            : parameterName;

    public static string ParamName(string parameterName)
        => parameterName.TrimStart('@');

    public static string ToClickHouseParamType(string parameterName, string? sqlType = null)
    {
        if (parameterName.Contains("skip", StringComparison.OrdinalIgnoreCase))
        {
            return "UInt64";
        }

        if (parameterName.Contains("take", StringComparison.OrdinalIgnoreCase))
        {
            return "UInt32";
        }

        var type = (sqlType ?? string.Empty).Trim().ToLowerInvariant();
        var paren = type.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0)
        {
            type = type[..paren].Trim();
        }

        return type switch
        {
            "int" => "Int32",
            "bigint" => "Int64",
            "smallint" => "Int16",
            "tinyint" => "UInt8",
            "bit" => "UInt8",
            "uniqueidentifier" => "UUID",
            "date" => "Date",
            "datetime" or "datetime2" or "smalldatetime" => "DateTime64",
            "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" => "Float64",
            _ => "String"
        };
    }
}

public readonly record struct DatasetCacheRoutingTarget(string Schema, string ObjectName);
