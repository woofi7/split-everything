using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Api.Hubs;

/// <summary>
/// Live sync transport.
///
/// A client joins one SignalR group per expense group it follows, so a broadcast
/// reaches exactly the devices entitled to it. The hub carries no authority of its
/// own: everything it pushes has already been accepted and written, and an offline
/// client gets the same operations later through the delta pull. That is why a
/// dropped connection is never a correctness problem.
/// </summary>
[Authorize]
public sealed class SyncHub(AppDbContext db, ICurrentUser currentUser) : Hub
{
    public static string GroupChannel(Guid groupId) => $"group:{groupId}";
    public static string UserChannel(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var userId = currentUser.RequireUserId();

        var groupIds = await db.GroupMembers
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && !m.IsDeleted)
            .Select(m => m.GroupId)
            .ToListAsync(Context.ConnectionAborted);

        foreach (var groupId in groupIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupChannel(groupId));

        await Groups.AddToGroupAsync(Context.ConnectionId, UserChannel(userId));
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Joins a group channel after the caller has been added to a new group
    /// mid-connection, so they do not have to reconnect to start receiving.
    /// </summary>
    public async Task Follow(Guid groupId)
    {
        var userId = currentUser.RequireUserId();

        var isMember = await db.GroupMembers.AnyAsync(m =>
            m.GroupId == groupId && m.UserId == userId
            && m.Status == MembershipStatus.Active && !m.IsDeleted,
            Context.ConnectionAborted);

        if (!isMember) throw new HubException("You are not a member of that group.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupChannel(groupId));
    }

    public Task Unfollow(Guid groupId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupChannel(groupId));
}

/// <summary>
/// Sends accepted operations to the live clients of a group, skipping the device
/// that produced them: it already applied the change locally.
/// </summary>
public sealed class SignalRSyncBroadcaster(IHubContext<SyncHub> hub) : ISyncBroadcaster
{
    public Task BroadcastAsync(
        Guid groupId, SyncPushResult result, string? originDeviceId, CancellationToken ct = default)
        => hub.Clients.Group(SyncHub.GroupChannel(groupId))
            .SendAsync("syncChanged", new
            {
                groupId,
                originDeviceId,
                accepted = result.Accepted,
                cursors = result.GroupCursors
            }, ct);

    public Task NotifyConflictAsync(
        Guid groupId, Guid userId, SyncConflictDto conflict, CancellationToken ct = default)
        => hub.Clients.Group(SyncHub.UserChannel(userId))
            .SendAsync("syncConflict", conflict, ct);
}
