using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;
using ReportBuilder.Infrastructure.Options;
using ReportBuilder.Infrastructure.Services;

namespace ReportBuilder.Infrastructure.Identity;

public sealed class AuthService : IAuthService
{
    private readonly ReportBuilderDbContext _db;
    private readonly JwtTokenFactory _tokens;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityLog _log;
    private readonly IAppBrandingService _branding;
    private readonly AuthOptions _options;

    private static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ResetCooldown = TimeSpan.FromSeconds(60);

    public AuthService(
        ReportBuilderDbContext db,
        JwtTokenFactory tokens,
        ICurrentUser currentUser,
        IActivityLog log,
        IAppBrandingService branding,
        IOptions<AuthOptions> options)
    {
        _db = db;
        _tokens = tokens;
        _currentUser = currentUser;
        _log = log;
        _branding = branding;
        _options = options.Value;
    }

    public async Task<AuthSession> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        var name = (userName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
        {
            throw new IdentityException("User name and password are required.", 401);
        }

        var user = await _db.Users.WithAccess()
            .FirstOrDefaultAsync(item => item.UserName == name, cancellationToken);

        if (user is null || !IdentityRules.VerifyPassword(user, password))
        {
            await _log.WriteAsync(
                new ActivityEvent
                {
                    Level = ActivityLevels.Warning,
                    Category = ActivityCategories.Auth,
                    Action = "login.failed",
                    Message = "Invalid user name or password.",
                    TargetUserName = name,
                    StatusCode = 401
                },
                cancellationToken);
            throw new IdentityException("Invalid user name or password.", 401);
        }

        if (!user.IsActive)
        {
            await _log.WriteAsync(
                new ActivityEvent
                {
                    Level = ActivityLevels.Warning,
                    Category = ActivityCategories.Auth,
                    Action = "login.blocked",
                    Message = "Sign-in blocked because the account is deactivated.",
                    TargetUserId = user.Id,
                    TargetUserName = user.UserName,
                    StatusCode = 403
                },
                cancellationToken);
            throw new IdentityException("This account is deactivated.", 403);
        }

        var session = await IssueSessionAsync(user, cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Auth,
                Action = "login.success",
                Message = $"{user.UserName} signed in.",
                ActorUserId = user.Id,
                ActorUserName = user.UserName,
                TargetUserId = user.Id,
                TargetUserName = user.UserName
            },
            cancellationToken);
        return session;
    }

    public async Task<LaunchTicket> LaunchAsync(
        LaunchRequest request,
        string? sharedSecret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Launch.Secret))
        {
            throw new IdentityException("Launch sign-in is not configured.", 503);
        }

        if (!SecretsEqual(sharedSecret, _options.Launch.Secret))
        {
            await _log.WriteAsync(
                new ActivityEvent
                {
                    Level = ActivityLevels.Warning,
                    Category = ActivityCategories.Auth,
                    Action = "launch.denied",
                    Message = "Launch request used an invalid shared secret.",
                    TargetUserName = request.UserName,
                    StatusCode = 401
                },
                cancellationToken);
            throw new IdentityException("Launch secret is invalid.", 401);
        }

        await CleanupExpiredCodesAsync(cancellationToken);

        var userName = ResolveLaunchUserName(request);
        var source = NormalizeSource(request.Source);
        var externalId = NormalizeExternalId(request.ExternalId, userName);
        var user = await ResolveLaunchUserAsync(request, userName, source, externalId, cancellationToken);

        if (!user.IsActive)
        {
            await _log.WriteAsync(
                new ActivityEvent
                {
                    Level = ActivityLevels.Warning,
                    Category = ActivityCategories.Auth,
                    Action = "launch.denied",
                    Message = "Launch blocked because the account is deactivated.",
                    TargetUserId = user.Id,
                    TargetUserName = user.UserName,
                    StatusCode = 403
                },
                cancellationToken);
            throw new IdentityException("This account is deactivated.", 403);
        }

        var seconds = Math.Clamp(_options.Launch.CodeSeconds, 15, 300);
        var code = JwtTokenFactory.CreateRefreshToken();
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        var returnPath = SanitizeReturnPath(request.ReturnPath);

        _db.LaunchCodes.Add(new LaunchCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CodeHash = JwtTokenFactory.HashToken(code),
            ReturnPath = returnPath,
            Source = source,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Auth,
                Action = "launch.issued",
                Message = $"Issued a one-time launch code for {user.UserName}.",
                TargetUserId = user.Id,
                TargetUserName = user.UserName
            },
            cancellationToken);

        return new LaunchTicket
        {
            Code = code,
            ExpiresAt = expiresAt,
            RedirectUrl = BuildRedirectUrl(code, returnPath)
        };
    }

    public async Task<AuthSession> ExchangeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new IdentityException("Launch code is required.", 401);
        }

        var hash = JwtTokenFactory.HashToken(code.Trim());
        var stored = await _db.LaunchCodes
            .FirstOrDefaultAsync(item => item.CodeHash == hash, cancellationToken);

        if (stored is null || !stored.IsActive)
        {
            await _log.WriteAsync(
                new ActivityEvent
                {
                    Level = ActivityLevels.Warning,
                    Category = ActivityCategories.Auth,
                    Action = "sso.failed",
                    Message = "Launch code is invalid, used, or expired.",
                    StatusCode = 401
                },
                cancellationToken);
            throw new IdentityException("Launch code is invalid or expired.", 401);
        }

        stored.ConsumedAt = DateTimeOffset.UtcNow;

        var user = await _db.Users.WithAccess()
            .FirstOrDefaultAsync(item => item.Id == stored.UserId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            await _db.SaveChangesAsync(cancellationToken);
            await _log.WriteAsync(
                new ActivityEvent
                {
                    Level = ActivityLevels.Warning,
                    Category = ActivityCategories.Auth,
                    Action = "sso.failed",
                    Message = "Launch exchange blocked because the account is no longer active.",
                    TargetUserId = stored.UserId,
                    StatusCode = 403
                },
                cancellationToken);
            throw new IdentityException("This account is no longer active.", 403);
        }

        var session = await IssueSessionAsync(user, cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Auth,
                Action = "sso.exchange",
                Message = $"{user.UserName} signed in through a launch code.",
                ActorUserId = user.Id,
                ActorUserName = user.UserName,
                TargetUserId = user.Id,
                TargetUserName = user.UserName
            },
            cancellationToken);
        return session;
    }

    public AuthPublicOptions GetPublicOptions()
    {
        var url = (_options.Launch.ExternalLoginUrl ?? string.Empty).Trim();
        return new AuthPublicOptions
        {
            ExternalLoginUrl = string.IsNullOrWhiteSpace(url) ? null : url,
            LaunchEnabled = !string.IsNullOrWhiteSpace(_options.Launch.Secret)
        };
    }

    public async Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new IdentityException("Refresh token is required.", 401);
        }

        var hash = JwtTokenFactory.HashToken(refreshToken);
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);

        if (stored is null || !stored.IsActive)
        {
            await _log.WriteAsync(
                new ActivityEvent
                {
                    Level = ActivityLevels.Warning,
                    Category = ActivityCategories.Auth,
                    Action = "refresh.failed",
                    Message = "Refresh token is invalid or expired.",
                    StatusCode = 401
                },
                cancellationToken);
            throw new IdentityException("Refresh token is invalid or expired.", 401);
        }

        var user = await _db.Users.WithAccess()
            .FirstOrDefaultAsync(item => item.Id == stored.UserId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            stored.RevokedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            throw new IdentityException("This account is no longer active.", 401);
        }

        stored.RevokedAt = DateTimeOffset.UtcNow;
        return await IssueSessionAsync(user, cancellationToken);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var hash = JwtTokenFactory.HashToken(refreshToken);
            var stored = await _db.RefreshTokens
                .FirstOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);
            if (stored is not null && stored.RevokedAt is null)
            {
                stored.RevokedAt = DateTimeOffset.UtcNow;
            }
        }

        if (_currentUser.UserId is { } userId)
        {
            var active = await _db.RefreshTokens
                .Where(token => token.UserId == userId && token.RevokedAt == null)
                .ToListAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            foreach (var token in active)
            {
                token.RevokedAt = now;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Auth,
                Action = "logout",
                Message = "Signed out."
            },
            cancellationToken);
    }

    public async Task<UserAccount> GetMeAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            throw new IdentityException("Not authenticated.", 401);
        }

        var user = await _db.Users.WithAccess()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new IdentityException("This account is no longer active.", 401);
        }

        var names = await IdentityLookup.NamesAsync(_db, [user.CreatedBy, user.ModifiedBy], cancellationToken);
        return IdentityMapper.ToAccount(user, names);
    }

    public async Task ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        string? currentRefreshToken,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            throw new IdentityException("Not authenticated.", 401);
        }

        var user = await _db.Users.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new IdentityException("Not authenticated.", 401);

        if (!user.IsActive)
        {
            throw new IdentityException("This account is no longer active.", 401);
        }

        if (string.IsNullOrWhiteSpace(currentPassword) || !IdentityRules.VerifyPassword(user, currentPassword))
        {
            throw new IdentityException("Current password is incorrect.");
        }

        IdentityRules.EnsurePassword(newPassword, required: true);
        if (IdentityRules.VerifyPassword(user, newPassword))
        {
            throw new IdentityException("Choose a different password.");
        }

        user.PasswordHash = IdentityRules.HashPassword(user, newPassword);
        user.SecurityStamp = Guid.NewGuid();
        user.ModifiedBy = user.Id;
        user.ModifiedAt = DateTimeOffset.UtcNow;

        var keepHash = string.IsNullOrWhiteSpace(currentRefreshToken)
            ? null
            : JwtTokenFactory.HashToken(currentRefreshToken);
        var tokens = await _db.RefreshTokens
            .Where(token => token.UserId == user.Id && token.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var token in tokens)
        {
            if (keepHash is not null && token.TokenHash == keepHash)
            {
                continue;
            }

            token.RevokedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Auth,
                Action = "password.change",
                Message = $"{user.UserName} changed their password.",
                ActorUserId = user.Id,
                ActorUserName = user.UserName,
                TargetUserId = user.Id,
                TargetUserName = user.UserName
            },
            cancellationToken);
    }

    public async Task RequestPasswordResetAsync(string userName, CancellationToken cancellationToken = default)
    {
        var name = (userName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new IdentityException("User name is required.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(item => item.UserName == name, cancellationToken);
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var open = await _db.PasswordResetTokens
            .Where(item => item.UserId == user.Id && item.UsedAt == null)
            .ToListAsync(cancellationToken);
        if (open.Any(item => item.CreatedAt > now.Subtract(ResetCooldown)))
        {
            return;
        }

        foreach (var item in open)
        {
            item.UsedAt = now;
        }

        var token = NewResetToken();
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = JwtTokenFactory.HashToken(token),
            ExpiresAt = now.Add(ResetLifetime),
            CreatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await SendPasswordResetAsync(user, token, cancellationToken);
        }
        catch (Exception exception)
        {
            await _log.WriteAsync(
                new ActivityEvent
                {
                    Level = ActivityLevels.Warning,
                    Category = ActivityCategories.Auth,
                    Action = "password.reset.mail",
                    Message = "The password reset mail could not be sent.",
                    TargetUserId = user.Id,
                    TargetUserName = user.UserName,
                    Details = exception.Message
                },
                cancellationToken);
        }
    }

    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var presented = (token ?? string.Empty).Trim();
        if (presented.Length == 0)
        {
            throw new IdentityException("This reset link is no longer valid.");
        }

        var hash = JwtTokenFactory.HashToken(presented);
        var row = await _db.PasswordResetTokens
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.TokenHash == hash, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (row is null || row.UsedAt is not null || row.ExpiresAt <= now || !row.User.IsActive)
        {
            throw new IdentityException("This reset link is no longer valid.");
        }

        var user = row.User;
        IdentityRules.EnsurePassword(newPassword, required: true);
        if (IdentityRules.VerifyPassword(user, newPassword))
        {
            throw new IdentityException("Choose a different password.");
        }

        user.PasswordHash = IdentityRules.HashPassword(user, newPassword);
        user.SecurityStamp = Guid.NewGuid();
        user.ModifiedBy = user.Id;
        user.ModifiedAt = now;
        row.UsedAt = now;

        var sessions = await _db.RefreshTokens
            .Where(item => item.UserId == user.Id && item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Auth,
                Action = "password.reset",
                Message = $"{user.UserName} reset their password.",
                ActorUserId = user.Id,
                ActorUserName = user.UserName,
                TargetUserId = user.Id,
                TargetUserName = user.UserName
            },
            cancellationToken);
    }

    public async Task<UserGrants> GetGrantsAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            throw new IdentityException("Not authenticated.", 401);
        }

        var user = await _db.Users.WithAccess()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new IdentityException("This account is no longer active.", 401);
        }

        var roles = user.UserRoles
            .Select(link => link.Role)
            .DistinctBy(role => role.Id)
            .OrderBy(role => role.Name)
            .Select(role => new GrantRole
            {
                Name = role.Name,
                Description = role.Description
            })
            .ToList();

        var codes = IdentityQueries.EffectivePermissions(user);
        var permissionRows = await _db.Permissions.AsNoTracking()
            .Where(permission => codes.Contains(permission.Code))
            .ToListAsync(cancellationToken);
        var byCode = permissionRows.ToDictionary(permission => permission.Code, StringComparer.OrdinalIgnoreCase);
        var permissions = codes
            .Select(code => byCode.TryGetValue(code, out var permission)
                ? new GrantPermission
                {
                    Code = permission.Code,
                    Name = permission.Name,
                    Description = permission.Description
                }
                : new GrantPermission { Code = code, Name = code })
            .ToList();

        var reports = await _db.ReportTemplates.AsNoTracking()
            .Include(template => template.Access)
            .Include(template => template.RoleAccess)
            .ThenInclude(row => row.Role)
            .Include(template => template.Dataset)
            .OrderBy(template => template.Name)
            .ToListAsync(cancellationToken);
        var visibleReports = reports
            .Where(template => ReportAccessRules.CanView(template, _currentUser))
            .Select(template => new GrantReport
            {
                Id = template.Id,
                Name = template.Name,
                NameArabic = template.NameArabic,
                Status = template.Status,
                DatasetName = template.Dataset.DisplayName,
                DatasetNameArabic = template.Dataset.DisplayNameArabic
            })
            .ToList();

        var datasets = await _db.Datasets.AsNoTracking()
            .Include(dataset => dataset.Access)
            .Include(dataset => dataset.RoleAccess)
                .ThenInclude(row => row.Role)
            .Where(dataset => dataset.IsEnabled)
            .OrderBy(dataset => dataset.DisplayName)
            .ToListAsync(cancellationToken);
        var visibleDatasets = datasets
            .Where(dataset => DatasetAccessRules.CanUseInDesigner(dataset, _currentUser))
            .Select(dataset => new GrantDataset
            {
                Id = dataset.Id,
                DisplayName = dataset.DisplayName,
                DisplayNameArabic = dataset.DisplayNameArabic,
                SchemaName = dataset.SchemaName,
                ObjectName = dataset.ObjectName,
                ObjectType = dataset.ObjectType.ToString(),
                IsRestricted = dataset.IsRestricted
            })
            .ToList();

        return new UserGrants
        {
            Roles = roles,
            Permissions = permissions,
            Reports = visibleReports,
            Datasets = visibleDatasets
        };
    }

    private async Task<AuthSession> IssueSessionAsync(User user, CancellationToken cancellationToken)
    {
        var permissions = IdentityQueries.EffectivePermissions(user);
        var roles = IdentityQueries.RoleNames(user);
        var (accessToken, expiresAt) = _tokens.CreateAccessToken(user, permissions, roles);
        var refreshToken = JwtTokenFactory.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = JwtTokenFactory.HashToken(refreshToken),
            ExpiresAt = _tokens.RefreshExpiresAt(),
            CreatedBy = user.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        var names = await IdentityLookup.NamesAsync(_db, [user.CreatedBy, user.ModifiedBy], cancellationToken);
        return new AuthSession
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = expiresAt,
            User = IdentityMapper.ToAccount(user, names)
        };
    }

    private async Task<User> ResolveLaunchUserAsync(
        LaunchRequest request,
        string userName,
        string source,
        string externalId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var actor = IdentityDefaults.SystemActorId;

        var user = await _db.Users
            .Include(item => item.UserRoles)
            .FirstOrDefaultAsync(
                item => item.ExternalSource == source && item.ExternalId == externalId,
                cancellationToken);

        if (user is null)
        {
            user = await _db.Users
                .Include(item => item.UserRoles)
                .FirstOrDefaultAsync(
                    item => item.UserName == userName && item.ExternalSource == source,
                    cancellationToken);
        }

        if (user is null)
        {
            var allocatedName = await AllocateUserNameAsync(userName, cancellationToken);
            user = new User
            {
                Id = Guid.NewGuid(),
                UserName = allocatedName,
                Email = await AllocateEmailAsync(request.Email, allocatedName, null, cancellationToken),
                DisplayName = ResolveDisplayName(request.DisplayName, allocatedName),
                DisplayNameArabic = IdentityRules.OptionalArabicDisplayName(request.DisplayNameArabic, null),
                ExternalId = externalId,
                ExternalSource = source,
                IsActive = true,
                CreatedBy = actor,
                CreatedAt = now,
                ModifiedBy = actor,
                ModifiedAt = now
            };
            user.PasswordHash = IdentityRules.HashPassword(
                user,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            _db.Users.Add(user);
        }
        else
        {
            user.ExternalId = externalId;
            user.ExternalSource = source;
            user.DisplayName = ResolveDisplayName(request.DisplayName, user.DisplayName);
            user.DisplayNameArabic = IdentityRules.OptionalArabicDisplayName(
                request.DisplayNameArabic,
                user.DisplayNameArabic);
            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                user.Email = await AllocateEmailAsync(request.Email, user.UserName, user.Id, cancellationToken);
            }
            user.ModifiedBy = actor;
            user.ModifiedAt = now;
        }

        if (user.UserRoles.Count == 0)
        {
            await AssignDefaultLaunchRoleAsync(user, now, actor, cancellationToken);
        }

        return user;
    }

    private async Task AssignDefaultLaunchRoleAsync(
        User user,
        DateTimeOffset now,
        Guid actor,
        CancellationToken cancellationToken)
    {
        var roleName = string.IsNullOrWhiteSpace(_options.Launch.DefaultRoleName)
            ? IdentityDefaults.PortalViewerRoleName
            : _options.Launch.DefaultRoleName.Trim();
        var role = await _db.Roles.FirstOrDefaultAsync(item => item.Name == roleName, cancellationToken)
            ?? throw new IdentityException("Launch default role is not configured.", 503);

        user.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            CreatedBy = actor,
            CreatedAt = now
        });
    }

    private async Task<string> AllocateUserNameAsync(string desired, CancellationToken cancellationToken)
    {
        if (!await _db.Users.AnyAsync(item => item.UserName == desired, cancellationToken))
        {
            return desired;
        }

        for (var index = 2; index < 1000; index++)
        {
            var suffix = $"-{index}";
            var prefixLength = Math.Max(3, 64 - suffix.Length);
            var candidate = (desired.Length > prefixLength ? desired[..prefixLength] : desired) + suffix;
            if (candidate.Length < 3)
            {
                continue;
            }

            if (!await _db.Users.AnyAsync(item => item.UserName == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new IdentityException("Could not allocate a user name for this launch.", 409);
    }

    private async Task<string> AllocateEmailAsync(
        string? email,
        string userName,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var candidate = TryEmail(email) ?? $"{userName}@sso.invalid";
        if (!await EmailTakenAsync(candidate, excludeId, cancellationToken))
        {
            return candidate;
        }

        for (var index = 2; index < 1000; index++)
        {
            var fallback = $"{userName}-{index}@sso.invalid";
            if (!await EmailTakenAsync(fallback, excludeId, cancellationToken))
            {
                return fallback;
            }
        }

        throw new IdentityException("Could not allocate an email for this launch.", 409);
    }

    private async Task<bool> EmailTakenAsync(string email, Guid? excludeId, CancellationToken cancellationToken) =>
        await _db.Users.AnyAsync(item => item.Email == email && item.Id != excludeId, cancellationToken);

    private async Task CleanupExpiredCodesAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        var stale = await _db.LaunchCodes
            .Where(item => item.ExpiresAt < cutoff)
            .Take(50)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return;
        }

        _db.LaunchCodes.RemoveRange(stale);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SendPasswordResetAsync(User user, string token, CancellationToken cancellationToken)
    {
        var baseUrl = PublicBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Auth:Launch:PublicBaseUrl is not configured.");
        }

        var profile = await _db.SmtpProfiles.AsNoTracking()
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            throw new InvalidOperationException("No SMTP profile is configured.");
        }

        var template = await _db.EmailTemplates.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Name == EmailTemplateSeeder.PasswordResetName, cancellationToken);
        var branding = await _branding.GetAsync(cancellationToken);
        AppLogoFile? logo = branding.HasLogo ? await _branding.LogoAsync(cancellationToken) : null;
        var resetUrl = $"{baseUrl}/reset-password?token={Uri.EscapeDataString(token)}";
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName.Trim();
        var displayNameArabic = string.IsNullOrWhiteSpace(user.DisplayNameArabic)
            ? displayName
            : user.DisplayNameArabic.Trim();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resetUrl"] = resetUrl,
            ["expiresMinutes"] = ((int)ResetLifetime.TotalMinutes).ToString(),
            ["userName"] = user.UserName,
            ["displayName"] = displayName,
            ["displayNameArabic"] = displayNameArabic,
            ["email"] = user.Email,
            ["applicationName"] = branding.ApplicationName,
            ["applicationNameArabic"] = branding.ApplicationNameArabic,
            ["studioName"] = branding.StudioName,
            ["studioNameArabic"] = branding.StudioNameArabic,
            ["organizationName"] = branding.OrganizationName,
            ["organizationNameArabic"] = branding.OrganizationNameArabic,
            ["logo"] = branding.HasLogo ? EmailLogoHtml.Render(branding.ApplicationName, logo?.Bytes) : string.Empty
        };
        var fallback = $"""
            Reset your password
            {resetUrl}

            This link works once and expires in {(int)ResetLifetime.TotalMinutes} minutes.
            """;
        var composed = EmailTemplateComposer.Compose(
            template,
            values,
            "Reset your password",
            fallback.Trim());

        await SmtpMailSender.SendAsync(
            profile,
            user.Email.Trim(),
            composed.Subject,
            composed.Body,
            fileName: null,
            content: null,
            contentType: null,
            cancellationToken,
            _log,
            new MailActivity(user.Id, user.UserName, user.Id, user.UserName, user.Id, "Password reset"),
            composed.IsHtml,
            composed.IsHtml ? logo?.Bytes : null,
            composed.IsHtml ? logo?.ContentType : null);

        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Auth,
                Action = "password.reset.mail",
                Message = $"Sent a password reset mail to {user.UserName}.",
                TargetUserId = user.Id,
                TargetUserName = user.UserName
            },
            cancellationToken);
    }

    private static string NewResetToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private string BuildRedirectUrl(string code, string? returnPath)
    {
        if (TryBuildEmbedRedirect(returnPath, code, out var embedUrl))
        {
            return embedUrl;
        }

        var query = "code=" + Uri.EscapeDataString(code);
        if (!string.IsNullOrWhiteSpace(returnPath))
        {
            query += "&return=" + Uri.EscapeDataString(returnPath);
        }

        var baseUrl = (_options.Launch.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl)
            ? $"/sso?{query}"
            : $"{baseUrl}/sso?{query}";
    }

    private string? PublicBaseUrl() =>
        string.IsNullOrWhiteSpace(_options.Launch.PublicBaseUrl)
            ? null
            : _options.Launch.PublicBaseUrl.Trim().TrimEnd('/');

    /// <summary>
    /// An embed return path opens the report frame directly. The one-time code is
    /// exchanged there, so staff never pass through the studio sign-in page.
    /// </summary>
    private bool TryBuildEmbedRedirect(string? returnPath, string code, out string url)
    {
        url = string.Empty;
        var path = (returnPath ?? string.Empty).Trim();
        var split = path.IndexOf('?', StringComparison.Ordinal);
        var route = split >= 0 ? path[..split] : path;
        if (!EmbedReportPath.IsMatch(route))
        {
            return false;
        }

        var query = split >= 0 ? path[(split + 1)..] : string.Empty;
        var kept = string.Join(
            '&',
            query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => !part.StartsWith("code=", StringComparison.OrdinalIgnoreCase)));
        var relative = string.IsNullOrEmpty(kept)
            ? $"{route}?code={Uri.EscapeDataString(code)}"
            : $"{route}?{kept}&code={Uri.EscapeDataString(code)}";
        var baseUrl = PublicBaseUrl();
        url = baseUrl is null ? relative : baseUrl + relative;
        return true;
    }

    private static readonly Regex EmbedReportPath = new(
        "^/embed/reports/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static bool SecretsEqual(string? provided, string expected)
    {
        var left = Encoding.UTF8.GetBytes(JwtTokenFactory.HashToken(provided ?? string.Empty));
        var right = Encoding.UTF8.GetBytes(JwtTokenFactory.HashToken(expected));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string? SanitizeReturnPath(string? value)
    {
        var path = (value ?? string.Empty).Trim();
        if (path.Length == 0
            || !path.StartsWith('/')
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }

        return path.Length > 256 ? path[..256] : path;
    }

    private static string ResolveLaunchUserName(LaunchRequest request)
    {
        foreach (var candidate in new[] { request.UserName, request.ExternalId, request.Email })
        {
            try
            {
                return IdentityRules.NormalizeExternalUserName(candidate);
            }
            catch (IdentityException)
            {
                // Try the next identifier MECS may have sent.
            }
        }

        return "portal-user";
    }

    private static string NormalizeSource(string? value)
    {
        var source = (value ?? string.Empty).Trim();
        if (source.Length == 0)
        {
            return "MECS";
        }

        return source.Length > 64 ? source[..64] : source;
    }

    private static string NormalizeExternalId(string? value, string fallback)
    {
        var id = (value ?? string.Empty).Trim();
        if (id.Length == 0)
        {
            id = fallback;
        }

        return id.Length > 128 ? id[..128] : id;
    }

    private static string ResolveDisplayName(string? incoming, string fallback)
    {
        var name = (incoming ?? string.Empty).Trim();
        if (name.Length < 2)
        {
            name = fallback;
        }

        return name.Length > 128 ? name[..128] : name;
    }

    private static string? TryEmail(string? value)
    {
        var email = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256 || !email.Contains('@'))
        {
            return null;
        }

        return email;
    }
}
