using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Settlements;
using SplitEverything.Application.Services;

namespace SplitEverything.Api.Controllers;

public sealed class GroupsController(
    ICurrentUser currentUser,
    IGroupService groups,
    IInviteService invites,
    ISettlementService settlements,
    IGroupLifecycleService lifecycle) : ApiControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GroupSummaryDto>>> List(
        [FromQuery] bool includeArchived = false, CancellationToken ct = default)
        => Ok(await groups.ListAsync(UserId, includeArchived, ct));

    [HttpPost]
    public async Task<ActionResult<GroupDto>> Create(CreateGroupRequest request, CancellationToken ct)
    {
        var group = await groups.CreateAsync(UserId, request, ct);
        return CreatedAtAction(nameof(Get), new { groupId = group.Id }, group);
    }

    [HttpGet("{groupId:guid}")]
    public async Task<ActionResult<GroupDto>> Get(Guid groupId, CancellationToken ct)
        => Ok(await groups.GetAsync(UserId, groupId, ct));

    [HttpPatch("{groupId:guid}")]
    public async Task<ActionResult<GroupDto>> Update(
        Guid groupId, UpdateGroupRequest request, CancellationToken ct)
        => Ok(await groups.UpdateAsync(UserId, groupId, request, ct));

    [HttpPost("{groupId:guid}/archive")]
    public async Task<ActionResult<GroupDto>> Archive(Guid groupId, CancellationToken ct)
        => Ok(await groups.ArchiveAsync(UserId, groupId, ct));

    [HttpPost("{groupId:guid}/unarchive")]
    public async Task<ActionResult<GroupDto>> Unarchive(Guid groupId, CancellationToken ct)
        => Ok(await groups.UnarchiveAsync(UserId, groupId, ct));

    [HttpPost("{groupId:guid}/members")]
    public async Task<ActionResult<GroupMemberDto>> AddPlaceholderMember(
        Guid groupId, AddPlaceholderMemberRequest request, CancellationToken ct)
        => Ok(await groups.AddPlaceholderMemberAsync(UserId, groupId, request, ct));

    [HttpPost("{groupId:guid}/members/user")]
    public async Task<ActionResult<GroupMemberDto>> AddUserMember(
        Guid groupId, AddUserMemberRequest request, CancellationToken ct)
        => Ok(await groups.AddUserMemberAsync(UserId, groupId, request, ct));

    /// <summary>
    /// People with an account who are not in the group yet, so the add-someone
    /// field can search them instead of asking for a name to be typed exactly.
    /// </summary>
    [HttpGet("/api/users/addable")]
    public async Task<ActionResult<IReadOnlyList<AddableUserDto>>> AddableUsers(
        [FromQuery] Guid? groupId, CancellationToken ct)
        => Ok(await groups.ListAddableUsersAsync(UserId, groupId, ct));

    [HttpDelete("{groupId:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid groupId, Guid memberId, CancellationToken ct)
    {
        await groups.RemoveMemberAsync(UserId, groupId, memberId, ct);
        return NoContent();
    }

    [HttpGet("{groupId:guid}/balance")]
    public async Task<ActionResult<GroupBalanceDto>> Balance(Guid groupId, CancellationToken ct)
        => Ok(await settlements.GetGroupBalanceAsync(UserId, groupId, ct));

    [HttpGet("{groupId:guid}/lineage")]
    public async Task<ActionResult<IReadOnlyList<GroupLineageDto>>> Lineage(
        Guid groupId, CancellationToken ct)
        => Ok(await groups.GetLineageAsync(UserId, groupId, ct));

    // ---- invites ---------------------------------------------------------

    [HttpGet("{groupId:guid}/invites")]
    public async Task<ActionResult<IReadOnlyList<InviteDto>>> ListInvites(
        Guid groupId, CancellationToken ct)
        => Ok(await invites.ListForGroupAsync(UserId, groupId, ct));

    [HttpPost("{groupId:guid}/invites")]
    public async Task<ActionResult<InviteDto>> CreateInvite(
        Guid groupId, CreateInviteRequest request, CancellationToken ct)
        => Ok(await invites.CreateAsync(UserId, groupId, request, ct));

    /// <summary>The same invite as a scannable PNG.</summary>
    [HttpGet("invites/{inviteId:guid}/qr")]
    public async Task<IActionResult> InviteQrCode(
        Guid inviteId, [FromQuery] int size = 10, CancellationToken ct = default)
        => File(await invites.RenderQrCodeAsync(UserId, inviteId, size, ct), "image/png");

    [HttpDelete("invites/{inviteId:guid}")]
    public async Task<IActionResult> RevokeInvite(Guid inviteId, CancellationToken ct)
    {
        await invites.RevokeAsync(UserId, inviteId, ct);
        return NoContent();
    }

    /// <summary>Unauthenticated peek, so the sign-in page can name the group.</summary>
    [AllowAnonymous]
    [HttpGet("/api/invites/{token}")]
    public async Task<ActionResult<InvitePreviewDto>> PreviewInvite(string token, CancellationToken ct)
        => Ok(await invites.PreviewAsync(token, ct));

    [HttpPost("/api/invites/{token}/redeem")]
    public async Task<ActionResult<RedeemInviteResult>> RedeemInvite(string token, CancellationToken ct)
        => Ok(await invites.RedeemAsync(UserId, token, ct));

    // ---- lifecycle -------------------------------------------------------

    [HttpPost("merge")]
    public async Task<ActionResult<MergeGroupsResult>> Merge(MergeGroupsRequest request, CancellationToken ct)
        => Ok(await lifecycle.MergeAsync(UserId, request, ct));

    [HttpPost("split")]
    public async Task<ActionResult<SplitGroupResult>> Split(SplitGroupRequest request, CancellationToken ct)
        => Ok(await lifecycle.SplitAsync(UserId, request, ct));
}
