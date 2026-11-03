using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReportBuilder.Api.Contracts;
using ReportBuilder.Core.Identity;

namespace ReportBuilder.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserAdminService _users;

    public UsersController(IUserAdminService users)
    {
        _users = users;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.UsersView)]
    public async Task<ActionResult<IReadOnlyList<UserAccount>>> List(CancellationToken cancellationToken)
        => Ok(await _users.ListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCodes.UsersView)]
    public async Task<ActionResult<UserAccount>> Get(Guid id, CancellationToken cancellationToken)
    {
        var user = await _users.GetAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.UsersCreate)]
    public async Task<ActionResult<UserAccount>> Create([FromBody] UserWriteDto request, CancellationToken cancellationToken)
    {
        var created = await _users.CreateAsync(request.ToRequest(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [HttpPost("{id:guid}")]
    [Authorize(Policy = PermissionCodes.UsersUpdate)]
    public async Task<ActionResult<UserAccount>> Update(Guid id, [FromBody] UserWriteDto request, CancellationToken cancellationToken)
        => Ok(await _users.UpdateAsync(id, request.ToRequest(), cancellationToken));

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = PermissionCodes.UsersActivate)]
    public async Task<ActionResult<UserAccount>> Activate(Guid id, [FromBody] ActivateUserRequest request, CancellationToken cancellationToken)
        => Ok(await _users.SetActiveAsync(id, request.IsActive, cancellationToken));

    [HttpPost("{id:guid}/sign-out")]
    [Authorize(Policy = PermissionCodes.UsersActivate)]
    public async Task<IActionResult> SignOutEverywhere(Guid id, CancellationToken cancellationToken)
    {
        await _users.SignOutEverywhereAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [HttpPost("{id:guid}/delete")]
    [Authorize(Policy = PermissionCodes.UsersDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _users.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
