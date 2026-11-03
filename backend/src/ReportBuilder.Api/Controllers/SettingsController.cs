using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly ICacheSettingsService _cacheSettings;
    private readonly IAppBrandingService _branding;
    private readonly ISmtpProfileService _smtp;
    private readonly IAiConnectionService _ai;
    private readonly IEmailTemplateService _emailTemplates;
    private readonly ICurrentUser _currentUser;

    public SettingsController(
        ICacheSettingsService cacheSettings,
        IAppBrandingService branding,
        ISmtpProfileService smtp,
        IAiConnectionService ai,
        IEmailTemplateService emailTemplates,
        ICurrentUser currentUser)
    {
        _cacheSettings = cacheSettings;
        _branding = branding;
        _smtp = smtp;
        _ai = ai;
        _emailTemplates = emailTemplates;
        _currentUser = currentUser;
    }

    [HttpGet("branding")]
    [AllowAnonymous]
    public async Task<ActionResult<AppBrandingModel>> Branding(CancellationToken cancellationToken)
        => Ok(await _branding.GetAsync(cancellationToken));

    [HttpGet("branding/logo")]
    [AllowAnonymous]
    public async Task<IActionResult> BrandingLogo(CancellationToken cancellationToken)
    {
        var logo = await _branding.LogoAsync(cancellationToken);
        if (logo is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public,max-age=300";
        return File(logo.Bytes, logo.ContentType);
    }

    [HttpPut("general")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<ActionResult<AppBrandingModel>> UpdateGeneral(
        [FromBody] AppBrandingWrite request,
        CancellationToken cancellationToken)
        => Ok(await _branding.UpdateAsync(request, _currentUser.UserId, cancellationToken));

    [HttpGet("cache")]
    [Authorize(Policy = PermissionCodes.SettingsView)]
    public async Task<ActionResult<CacheSettingsModel>> GetCache(CancellationToken cancellationToken)
        => Ok(await _cacheSettings.GetAsync(cancellationToken));

    [HttpPut("cache")]
    [HttpPost("cache")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<ActionResult<CacheSettingsModel>> UpdateCache(
        [FromBody] CacheSettingsModel request,
        CancellationToken cancellationToken)
        => Ok(await _cacheSettings.UpdateAsync(request, _currentUser.UserId, cancellationToken));

    [HttpPost("cache/test")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<ActionResult<object>> TestCache(
        [FromBody] CacheSettingsTestRequest request,
        CancellationToken cancellationToken)
    {
        await _cacheSettings.TestAsync(request.Provider, request.Connection, cancellationToken);
        return Ok(new { ok = true });
    }

    [HttpGet("smtp")]
    [Authorize(Policy = PermissionCodes.SettingsView)]
    public async Task<ActionResult<IReadOnlyList<SmtpProfileModel>>> ListSmtp(CancellationToken cancellationToken)
        => Ok(await _smtp.ListAsync(cancellationToken));

    [HttpGet("smtp/options")]
    [Authorize(Policy = PermissionCodes.ReportsUpdate)]
    public async Task<ActionResult<IReadOnlyList<SmtpProfileOption>>> ListSmtpOptions(CancellationToken cancellationToken)
        => Ok(await _smtp.ListOptionsAsync(cancellationToken));

    [HttpPost("smtp")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<ActionResult<SmtpProfileModel>> CreateSmtp(
        [FromBody] SmtpProfileWrite request,
        CancellationToken cancellationToken)
        => Ok(await _smtp.CreateAsync(request, _currentUser.UserId, cancellationToken));

    [HttpPut("smtp/{id:guid}")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<ActionResult<SmtpProfileModel>> UpdateSmtp(
        Guid id,
        [FromBody] SmtpProfileWrite request,
        CancellationToken cancellationToken)
        => Ok(await _smtp.UpdateAsync(id, request, _currentUser.UserId, cancellationToken));

    [HttpDelete("smtp/{id:guid}")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<IActionResult> DeleteSmtp(Guid id, CancellationToken cancellationToken)
    {
        await _smtp.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("smtp/test")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<ActionResult<object>> TestSmtp(
        [FromBody] SmtpProfileWrite request,
        CancellationToken cancellationToken)
    {
        await _smtp.TestAsync(request, cancellationToken);
        return Ok(new { ok = true });
    }

    [HttpGet("ai")]
    [Authorize(Policy = PermissionCodes.SettingsView)]
    public async Task<ActionResult<IReadOnlyList<AiConnectionModel>>> ListAi(CancellationToken cancellationToken)
        => Ok(await _ai.ListAsync(cancellationToken));

    [HttpPost("ai")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<ActionResult<AiConnectionModel>> CreateAi(
        [FromBody] AiConnectionWrite request,
        CancellationToken cancellationToken)
        => Ok(await _ai.CreateAsync(request, cancellationToken));

    [HttpPut("ai/{id:guid}")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<ActionResult<AiConnectionModel>> UpdateAi(
        Guid id,
        [FromBody] AiConnectionWrite request,
        CancellationToken cancellationToken)
        => Ok(await _ai.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("ai/{id:guid}")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<IActionResult> DeleteAi(Guid id, CancellationToken cancellationToken)
    {
        await _ai.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("ai/test")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<ActionResult<object>> TestAi(
        [FromBody] AiConnectionWrite request,
        CancellationToken cancellationToken)
    {
        var reply = await _ai.TestAsync(request, cancellationToken);
        return Ok(new { ok = true, reply });
    }

    [HttpGet("email-templates")]
    [Authorize(Policy = PermissionCodes.SettingsView)]
    public async Task<ActionResult<IReadOnlyList<EmailTemplateModel>>> ListEmailTemplates(CancellationToken cancellationToken)
        => Ok(await _emailTemplates.ListAsync(cancellationToken));

    [HttpGet("email-templates/options")]
    [Authorize(Policy = PermissionCodes.ReportsUpdate)]
    public async Task<ActionResult<IReadOnlyList<EmailTemplateOption>>> ListEmailTemplateOptions(CancellationToken cancellationToken)
        => Ok(await _emailTemplates.ListOptionsAsync(cancellationToken));

    [HttpPost("email-templates")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<ActionResult<EmailTemplateModel>> CreateEmailTemplate(
        [FromBody] EmailTemplateWrite request,
        CancellationToken cancellationToken)
        => Ok(await _emailTemplates.CreateAsync(request, cancellationToken));

    [HttpPut("email-templates/{id:guid}")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<ActionResult<EmailTemplateModel>> UpdateEmailTemplate(
        Guid id,
        [FromBody] EmailTemplateWrite request,
        CancellationToken cancellationToken)
        => Ok(await _emailTemplates.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("email-templates/{id:guid}")]
    [Authorize(Policy = PermissionCodes.SettingsUpdate)]
    public async Task<IActionResult> DeleteEmailTemplate(Guid id, CancellationToken cancellationToken)
    {
        await _emailTemplates.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

public sealed class CacheSettingsTestRequest
{
    public string Provider { get; set; } = string.Empty;
    public CacheProviderConnection? Connection { get; set; }
}
