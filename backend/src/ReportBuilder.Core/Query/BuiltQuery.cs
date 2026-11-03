namespace ReportBuilder.Core.Query;

public sealed class BuiltQuery
{
    public required string DataSql { get; init; }
    public required string CountSql { get; init; }
    public required IReadOnlyList<QueryParameter> FilterParameters { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
    public bool AllRows { get; init; }
    public IReadOnlySet<string> HiddenResultNames { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A SQL parameter. <paramref name="SqlType"/> carries the declared type of the column the value is
/// compared against so the command can bind an explicitly typed parameter. Leaving it null falls back
/// to inference, which sizes strings to their own length and pollutes the plan cache.
/// </summary>
public sealed record QueryParameter(string Name, object Value, string? SqlType = null);

public sealed class BuiltDistinctQuery
{
    public required string Sql { get; init; }
    public required IReadOnlyList<QueryParameter> Parameters { get; init; }
}
