using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReportBuilder.Api.Contracts;
using ReportBuilder.Core.Identity;

namespace ReportBuilder.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/permissions")]
public sealed class PermissionsController : ControllerBase
{
    private readonly IPermissionAdminService _permissions;

    public PermissionsController(IPermissionAdminService permissions)
    {
        _permissions = permissions;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.PermissionsView)]
    public async Task<ActionResult<IReadOnlyList<PermissionRecord>>> List(CancellationToken cancellationToken)
        => Ok(await _permissions.ListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCodes.PermissionsView)]
    public async Task<ActionResult<PermissionRecord>> Get(Guid id, CancellationToken cancellationToken)
    {
        var permission = await _permissions.GetAsync(id, cancellationToken);
        return permission is null ? NotFound() : Ok(permission);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.PermissionsCreate)]
    public async Task<ActionResult<PermissionRecord>> Create([FromBody] PermissionWriteDto request, CancellationToken cancellationToken)
    {
        var created = await _permissions.CreateAsync(request.ToRequest(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [HttpPost("{id:guid}")]
    [Authorize(Policy = PermissionCodes.PermissionsUpdate)]
    public async Task<ActionResult<PermissionRecord>> Update(Guid id, [FromBody] PermissionWriteDto request, CancellationToken cancellationToken)
        => Ok(await _permissions.UpdateAsync(id, request.ToRequest(), cancellationToken));

    [HttpDelete("{id:guid}")]
    [HttpPost("{id:guid}/delete")]
    [Authorize(Policy = PermissionCodes.PermissionsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _permissions.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
