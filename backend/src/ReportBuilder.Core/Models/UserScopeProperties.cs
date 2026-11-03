using ReportBuilder.Core.Identity;

namespace ReportBuilder.Core.Models;

public static class UserScopeProperties
{
    public const string Id = "id";
    public const string UserName = "userName";
    public const string Email = "email";
    public const string DisplayName = "displayName";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        Id,
        UserName,
        Email,
        DisplayName
    };

    public static string Normalize(string? property)
    {
        if (string.IsNullOrWhiteSpace(property))
        {
            return Id;
        }

        var value = property.Trim();
        return Allowed.Contains(value) ? value : Id;
    }

    public static bool IsCompatible(string property, string sqlType)
        => PropertiesForSqlType(sqlType).Contains(Normalize(property), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> PropertiesForSqlType(string sqlType)
    {
        var type = sqlType.Trim().ToLowerInvariant();
        if (type is "uniqueidentifier")
        {
            return [Id];
        }

        if (type is "int" or "bigint" or "smallint" or "tinyint")
        {
            return [];
        }

        return [Id, UserName, Email, DisplayName];
    }

    public static string? ResolveValue(string property, ICurrentUser user)
    {
        return Normalize(property) switch
        {
            Id => user.UserId?.ToString(),
            UserName => user.UserName,
            Email => user.Email,
            DisplayName => user.DisplayName,
            _ => null
        };
    }
}
