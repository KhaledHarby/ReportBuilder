using ReportBuilder.Core.Entities;

namespace ReportBuilder.Core.Identity;

public sealed class UserAccount
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameArabic { get; set; }
    public string? ExternalSource { get; set; }
    public bool IsActive { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
    public IReadOnlyList<string> Permissions { get; set; } = [];
    public IReadOnlyList<Guid> RoleIds { get; set; } = [];
    public IReadOnlyList<Guid> DirectPermissionIds { get; set; } = [];
    public Guid? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public string? ModifiedByName { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}

public sealed class RoleMember
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameArabic { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class RoleRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public IReadOnlyList<Guid> PermissionIds { get; set; } = [];
    public IReadOnlyList<string> Permissions { get; set; } = [];
    public int UserCount { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public string? ModifiedByName { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}

public sealed class PermissionRecord
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public string? ModifiedByName { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}

public sealed class AuthSession
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset AccessTokenExpiresAt { get; set; }
    public UserAccount User { get; set; } = null!;
}

public sealed class LaunchRequest
{
    public string UserName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? DisplayNameArabic { get; set; }
    public string? Email { get; set; }
    public string? ExternalId { get; set; }
    public string? Source { get; set; }
    public string? ReturnPath { get; set; }
}

public sealed class LaunchTicket
{
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string RedirectUrl { get; set; } = string.Empty;
}

public sealed class AuthPublicOptions
{
    public string? ExternalLoginUrl { get; set; }
    public bool LaunchEnabled { get; set; }
}

public sealed class UserWriteRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameArabic { get; set; }
    public string? Password { get; set; }
    public bool IsActive { get; set; } = true;
    public IReadOnlyList<Guid> RoleIds { get; set; } = [];
    public IReadOnlyList<Guid> PermissionIds { get; set; } = [];
}

public sealed class RoleWriteRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IReadOnlyList<Guid> PermissionIds { get; set; } = [];
}

public sealed class PermissionWriteRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class UserGrants
{
    public IReadOnlyList<GrantRole> Roles { get; set; } = [];
    public IReadOnlyList<GrantPermission> Permissions { get; set; } = [];
    public IReadOnlyList<GrantReport> Reports { get; set; } = [];
    public IReadOnlyList<GrantDataset> Datasets { get; set; } = [];
}

public sealed class GrantRole
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class GrantPermission
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class GrantReport
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public ReportStatus Status { get; set; }
    public string DatasetName { get; set; } = string.Empty;
    public string? DatasetNameArabic { get; set; }
}

public sealed class GrantDataset
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameArabic { get; set; }
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public bool IsRestricted { get; set; }
}
