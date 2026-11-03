using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ReportBuilder.Core.Entities;
using ReportBuilder.Infrastructure.Options;

namespace ReportBuilder.Infrastructure.Identity;

public sealed class JwtTokenFactory
{
    public const string SecurityStampClaim = "security_stamp";

    private readonly AuthOptions _options;
    private readonly SigningCredentials _credentials;

    public JwtTokenFactory(IOptions<AuthOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must be at least 32 characters.");
        }

        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(User user, IReadOnlyList<string> permissions, IReadOnlyList<string> roles)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _options.AccessTokenMinutes));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("display_name", user.DisplayName),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(SecurityStampClaim, user.SecurityStamp.ToString())
        };

        claims.AddRange(permissions.Select(code => new Claim("perm", code)));
        claims.AddRange(roles.Select(name => new Claim(ClaimTypes.Role, name)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: expires.UtcDateTime,
            signingCredentials: _credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public static string CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    public static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public DateTimeOffset RefreshExpiresAt() =>
        DateTimeOffset.UtcNow.AddDays(Math.Max(1, _options.RefreshTokenDays));
}
