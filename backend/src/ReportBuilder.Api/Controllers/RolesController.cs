using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReportBuilder.Api.Contracts;
using ReportBuilder.Core.Identity;

namespace ReportBuilder.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/roles")]
public sealed class RolesController : ControllerBase
{
    private readonly IRoleAdminService _roles;

    public RolesController(IRoleAdminService roles)
    {
        _roles = roles;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.RolesView)]
    public async Task<ActionResult<IReadOnlyList<RoleRecord>>> List(CancellationToken cancellationToken)
        => Ok(await _roles.ListAsync(cancellationToken));

    [HttpGet("{id:guid}/users")]
    [Authorize(Policy = PermissionCodes.RolesView)]
    public async Task<ActionResult<IReadOnlyList<RoleMember>>> ListUsers(Guid id, CancellationToken cancellationToken)
    {
        var users = await _roles.ListUsersAsync(id, cancellationToken);
        return users is null ? NotFound() : Ok(users);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCodes.RolesView)]
    public async Task<ActionResult<RoleRecord>> Get(Guid id, CancellationToken cancellationToken)
    {
        var role = await _roles.GetAsync(id, cancellationToken);
        return role is null ? NotFound() : Ok(role);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.RolesCreate)]
    public async Task<ActionResult<RoleRecord>> Create([FromBody] RoleWriteDto request, CancellationToken cancellationToken)
    {
        var created = await _roles.CreateAsync(request.ToRequest(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [HttpPost("{id:guid}")]
    [Authorize(Policy = PermissionCodes.RolesUpdate)]
    public async Task<ActionResult<RoleRecord>> Update(Guid id, [FromBody] RoleWriteDto request, CancellationToken cancellationToken)
        => Ok(await _roles.UpdateAsync(id, request.ToRequest(), cancellationToken));

    [HttpDelete("{id:guid}")]
    [HttpPost("{id:guid}/delete")]
    [Authorize(Policy = PermissionCodes.RolesDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _roles.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
