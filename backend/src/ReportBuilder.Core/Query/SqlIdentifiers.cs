namespace ReportBuilder.Core.Query;

public static class SqlIdentifiers
{
    public static string Quote(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    public static string Qualify(string schema, string objectName)
        => $"{Quote(schema)}.{Quote(objectName)}";
}
