namespace ReportBuilder.Core.Identity;

public interface IAuthService
{
    Task<AuthSession> LoginAsync(string userName, string password, CancellationToken cancellationToken = default);
    Task<LaunchTicket> LaunchAsync(LaunchRequest request, string? sharedSecret, CancellationToken cancellationToken = default);
    Task<AuthSession> ExchangeAsync(string code, CancellationToken cancellationToken = default);
    AuthPublicOptions GetPublicOptions();
    Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);
    Task<UserAccount> GetMeAsync(CancellationToken cancellationToken = default);
    Task<UserGrants> GetGrantsAsync(CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        string? currentRefreshToken,
        CancellationToken cancellationToken = default);
    Task RequestPasswordResetAsync(string userName, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}

public interface IUserAdminService
{
    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default);
    Task<UserAccount?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserAccount> CreateAsync(UserWriteRequest request, CancellationToken cancellationToken = default);
    Task<UserAccount> UpdateAsync(Guid id, UserWriteRequest request, CancellationToken cancellationToken = default);
    Task<UserAccount> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
    Task SignOutEverywhereAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IRoleAdminService
{
    Task<IReadOnlyList<RoleRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task<RoleRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RoleMember>?> ListUsersAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RoleRecord> CreateAsync(RoleWriteRequest request, CancellationToken cancellationToken = default);
    Task<RoleRecord> UpdateAsync(Guid id, RoleWriteRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IPermissionAdminService
{
    Task<IReadOnlyList<PermissionRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task<PermissionRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PermissionRecord> CreateAsync(PermissionWriteRequest request, CancellationToken cancellationToken = default);
    Task<PermissionRecord> UpdateAsync(Guid id, PermissionWriteRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
