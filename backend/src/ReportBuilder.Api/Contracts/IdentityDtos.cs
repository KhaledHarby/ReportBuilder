using ReportBuilder.Core.Identity;

namespace ReportBuilder.Api.Contracts;

public sealed class LoginRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class LogoutRequest
{
    public string? RefreshToken { get; set; }
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
}

public sealed class ForgotPasswordRequest
{
    public string UserName { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class ExchangeRequest
{
    public string Code { get; set; } = string.Empty;
}

public sealed class UserWriteDto
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameArabic { get; set; }
    public string? Password { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Guid> RoleIds { get; set; } = [];
    public List<Guid> PermissionIds { get; set; } = [];

    public UserWriteRequest ToRequest() => new()
    {
        UserName = UserName,
        Email = Email,
        DisplayName = DisplayName,
        DisplayNameArabic = DisplayNameArabic,
        Password = Password,
        IsActive = IsActive,
        RoleIds = RoleIds,
        PermissionIds = PermissionIds
    };
}

public sealed class ActivateUserRequest
{
    public bool IsActive { get; set; }
}

public sealed class RoleWriteDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<Guid> PermissionIds { get; set; } = [];

    public RoleWriteRequest ToRequest() => new()
    {
        Name = Name,
        Description = Description,
        PermissionIds = PermissionIds
    };
}

public sealed class PermissionWriteDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public PermissionWriteRequest ToRequest() => new()
    {
        Code = Code,
        Name = Name,
        Description = Description
    };
}
