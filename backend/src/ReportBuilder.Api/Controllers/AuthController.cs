using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReportBuilder.Api.Contracts;
using ReportBuilder.Core.Identity;

namespace ReportBuilder.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    [AllowAnonymous]
    [HttpGet("options")]
    public ActionResult<AuthPublicOptions> Options() => Ok(_auth.GetPublicOptions());

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthSession>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        => Ok(await _auth.LoginAsync(request.UserName, request.Password, cancellationToken));

    [AllowAnonymous]
    [HttpPost("launch")]
    public async Task<ActionResult<LaunchTicket>> Launch(
        [FromBody] LaunchRequest request,
        CancellationToken cancellationToken)
        => Ok(await _auth.LaunchAsync(request, ReadLaunchSecret(), cancellationToken));

    [AllowAnonymous]
    [HttpPost("exchange")]
    public async Task<ActionResult<AuthSession>> Exchange(
        [FromBody] ExchangeRequest request,
        CancellationToken cancellationToken)
        => Ok(await _auth.ExchangeAsync(request.Code, cancellationToken));

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthSession>> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
        => Ok(await _auth.RefreshAsync(request.RefreshToken, cancellationToken));

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request, CancellationToken cancellationToken)
    {
        await _auth.LogoutAsync(request?.RefreshToken, cancellationToken);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserAccount>> Me(CancellationToken cancellationToken)
        => Ok(await _auth.GetMeAsync(cancellationToken));

    [HttpGet("grants")]
    public async Task<ActionResult<UserGrants>> Grants(CancellationToken cancellationToken)
        => Ok(await _auth.GetGrantsAsync(cancellationToken));

    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _auth.ChangePasswordAsync(
            request.CurrentPassword,
            request.NewPassword,
            request.RefreshToken,
            cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _auth.RequestPasswordResetAsync(request.UserName, cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _auth.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
        return NoContent();
    }

    private string? ReadLaunchSecret()
    {
        if (Request.Headers.TryGetValue("X-Launch-Secret", out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }

        var authorization = Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return authorization[bearer.Length..].Trim();
        }

        return null;
    }
}
