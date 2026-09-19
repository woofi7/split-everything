using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Admin;
using SplitEverything.Application.Contracts.Categories;
using SplitEverything.Application.Services;

namespace SplitEverything.Api.Controllers;

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

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> Categories(CancellationToken ct)
        => Ok(await categories.GetGlobalAsync(UserId, ct));

    [HttpPut("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> SetCategories(
        SetCategoriesRequest request, CancellationToken ct)
        => Ok(await categories.SetGlobalAsync(UserId, request, ct));

    [HttpDelete("groups/{groupId:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid groupId, CancellationToken ct)
    {
        await admin.DeleteGroupAsync(UserId, groupId, ct);
        return NoContent();
    }
}
