namespace ReportBuilder.Core.Identity;

public static class ReportAccessModes
{
    public const string Everyone = "Everyone";
    public const string People = "People";
    public const string Roles = "Roles";

    public static string Normalize(string? mode, bool? isRestricted)
    {
        if (string.Equals(mode, Roles, StringComparison.OrdinalIgnoreCase))
        {
            return Roles;
        }

        if (string.Equals(mode, People, StringComparison.OrdinalIgnoreCase))
        {
            return People;
        }

        if (string.Equals(mode, Everyone, StringComparison.OrdinalIgnoreCase))
        {
            return Everyone;
        }

        return isRestricted == true ? People : Everyone;
    }

    public static bool IsRoles(string? mode) =>
        string.Equals(mode, Roles, StringComparison.OrdinalIgnoreCase);
}
