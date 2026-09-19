using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Admin;
using SplitEverything.Application.Contracts.Categories;
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
    IAdminService admin,
    ICategoryService categories) : ApiControllerBase(currentUser)
{
    [HttpGet("groups")]
    public async Task<ActionResult<IReadOnlyList<AdminGroupDto>>> Groups(CancellationToken ct)
        => Ok(await admin.GetGroupsAsync(UserId, ct));

    [HttpGet("groups/{groupId:guid}")]
    public async Task<ActionResult<AdminGroupDetailDto>> Group(Guid groupId, CancellationToken ct)
        => Ok(await admin.GetGroupAsync(UserId, groupId, ct));

    /// <summary>
    /// The server's own category list: what every group starts from, and what a
    /// group keeps seeing until it edits its own.
    /// </summary>
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> Categories(CancellationToken ct)
        => Ok(await categories.GetGlobalAsync(UserId, ct));

    [HttpPut("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> SetCategories(
        SetCategoriesRequest request, CancellationToken ct)
        => Ok(await categories.SetGlobalAsync(UserId, request, ct));

    /// <summary>Removes an archived group and everything in it, for good.</summary>
    [HttpDelete("groups/{groupId:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid groupId, CancellationToken ct)
    {
        await admin.DeleteGroupAsync(UserId, groupId, ct);
        return NoContent();
    }
}
