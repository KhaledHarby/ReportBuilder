using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;

namespace ReportBuilder.Infrastructure.Identity;

internal static class IdentityRules
{
    private static readonly PasswordHasher<User> PasswordHasher = new();
    private static readonly Regex UserNamePattern = new("^[A-Za-z0-9._-]{3,64}$", RegexOptions.Compiled);
    private static readonly Regex PermissionCodePattern = new("^[a-z0-9]+(\\.[a-z0-9]+){1,4}$", RegexOptions.Compiled);

    public static string HashPassword(User user, string password) =>
        PasswordHasher.HashPassword(user, password);

    public static bool VerifyPassword(User user, string password) =>
        PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;

    public static string NormalizeUserName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (!UserNamePattern.IsMatch(name))
        {
            throw new IdentityException("User name must be 3–64 characters using letters, numbers, dot, underscore, or hyphen.");
        }

        return name;
    }

    public static string NormalizeExternalUserName(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (raw.Contains('\\'))
        {
            raw = raw[(raw.LastIndexOf('\\') + 1)..];
        }
        if (raw.Contains('@'))
        {
            raw = raw[..raw.IndexOf('@')];
        }

        raw = raw.Replace(' ', '.');
        var cleaned = new string(raw.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' ? ch : '-').ToArray())
            .Trim('-', '.', '_');
        if (cleaned.Length > 64)
        {
            cleaned = cleaned[..64].Trim('-', '.', '_');
        }

        return NormalizeUserName(cleaned);
    }

    public static string NormalizeEmail(string? value)
    {
        var email = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256 || !email.Contains('@'))
        {
            throw new IdentityException("A valid email address is required.");
        }

        return email;
    }

    public static string NormalizeDisplayName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 2 or > 128)
        {
            throw new IdentityException("Display name must be 2–128 characters.");
        }

        return name;
    }

    public static string? OptionalArabicDisplayName(string? incoming, string? current)
    {
        if (incoming is null)
        {
            return current;
        }

        var name = incoming.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        if (name.Length > 128)
        {
            throw new IdentityException("Arabic display name must be 128 characters or fewer.");
        }

        return name;
    }

    public static string NormalizeRoleName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 2 or > 64)
        {
            throw new IdentityException("Role name must be 2–64 characters.");
        }

        return name;
    }

    public static string NormalizePermissionCode(string? value)
    {
        var code = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!PermissionCodePattern.IsMatch(code))
        {
            throw new IdentityException("Permission code must look like resource.action, using lowercase letters, numbers, and dots.");
        }

        return code;
    }

    public static string NormalizePermissionName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 2 or > 128)
        {
            throw new IdentityException("Permission name must be 2–128 characters.");
        }

        return name;
    }

    public static void EnsurePassword(string? password, bool required)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            if (required)
            {
                throw new IdentityException("Password is required.");
            }

            return;
        }

        if (password.Length < 8 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            throw new IdentityException("Password must be at least 8 characters and include a letter and a number.");
        }
    }
}
