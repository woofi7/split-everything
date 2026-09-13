using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Admin;
using SplitEverything.Application.Services;

namespace SplitEverything.Api.Controllers;

/// <summary>
/// For whoever runs this server, which is a different person from an owner of a
/// group. Every action re-checks that in the service rather than trusting this
/// route to be the only way in.
/// </summary>
[Route("api/admin")]
public sealed class AdminController(
    ICurrentUser currentUser,
    IAdminService admin) : ApiControllerBase(currentUser)
{
    [HttpGet("groups")]
    public async Task<ActionResult<IReadOnlyList<AdminGroupDto>>> Groups(CancellationToken ct)
        => Ok(await admin.GetGroupsAsync(UserId, ct));

    [HttpGet("groups/{groupId:guid}")]
    public async Task<ActionResult<AdminGroupDetailDto>> Group(Guid groupId, CancellationToken ct)
        => Ok(await admin.GetGroupAsync(UserId, groupId, ct));

    /// <summary>Removes an archived group and everything in it, for good.</summary>
    [HttpDelete("groups/{groupId:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid groupId, CancellationToken ct)
    {
        await admin.DeleteGroupAsync(UserId, groupId, ct);
        return NoContent();
    }
}
