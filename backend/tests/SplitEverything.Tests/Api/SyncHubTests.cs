using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Shouldly;
using SplitEverything.Api.Hubs;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Domain.Common;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Api;

/// <summary>
/// The hub decides which connections see which changes, so its membership checks
/// are the boundary that keeps one group's activity out of another's clients.
/// </summary>
public class SyncHubTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private const string ConnectionId = "connection-1";

    private (SyncHub Hub, IGroupManager Groups) CreateHub(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.RequireUserId().Returns(userId);

        var groups = Substitute.For<IGroupManager>();
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(ConnectionId);
        context.ConnectionAborted.Returns(CancellationToken.None);

        var hub = new SyncHub(Db, currentUser) { Groups = groups, Context = context };
        return (hub, groups);
    }

    [Fact]
    public async Task Connecting_subscribes_to_every_group_the_caller_is_in()
    {
        var user = await TestData.SeedUserAsync(Db);
        var first = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var second = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Trip", "CAD", null, null, null, null));
        var (hub, groups) = CreateHub(user.Id);

        await hub.OnConnectedAsync();

        await groups.Received().AddToGroupAsync(ConnectionId, SyncHub.GroupChannel(first.Id));
        await groups.Received().AddToGroupAsync(ConnectionId, SyncHub.GroupChannel(second.Id));
    }

    [Fact]
    public async Task Connecting_subscribes_to_the_callers_own_channel_for_conflicts()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (hub, groups) = CreateHub(user.Id);

        await hub.OnConnectedAsync();

        await groups.Received().AddToGroupAsync(ConnectionId, SyncHub.UserChannel(user.Id));
    }

    [Fact]
    public async Task Connecting_does_not_subscribe_to_a_group_the_caller_left()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var member = await TestData.SeedUserAsync(Db, "Member");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var membership = TestData.Member(group.Id, member.Id, "Member");
        Db.GroupMembers.Add(membership);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        await Groups.RemoveMemberAsync(owner.Id, group.Id, membership.Id);

        var (hub, groups) = CreateHub(member.Id);
        await hub.OnConnectedAsync();

        await groups.DidNotReceive().AddToGroupAsync(ConnectionId, SyncHub.GroupChannel(group.Id));
    }

    [Fact]
    public async Task Following_a_group_the_caller_belongs_to_subscribes_them()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var (hub, groups) = CreateHub(user.Id);

        await hub.Follow(group.Id);

        await groups.Received().AddToGroupAsync(ConnectionId, SyncHub.GroupChannel(group.Id));
    }

    [Fact]
    public async Task Following_a_group_the_caller_is_not_in_is_refused()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var (hub, groups) = CreateHub(stranger.Id);

        await Should.ThrowAsync<HubException>(() => hub.Follow(group.Id));
        await groups.DidNotReceive().AddToGroupAsync(ConnectionId, SyncHub.GroupChannel(group.Id));
    }

    [Fact]
    public async Task Unfollowing_removes_the_subscription()
    {
        var user = await TestData.SeedUserAsync(Db);
        var groupId = Guid.NewGuid();
        var (hub, groups) = CreateHub(user.Id);

        await hub.Unfollow(groupId);

        await groups.Received().RemoveFromGroupAsync(ConnectionId, SyncHub.GroupChannel(groupId));
    }

    [Fact]
    public void The_channel_names_are_namespaced_so_a_group_and_a_user_id_cannot_collide()
    {
        var id = Guid.NewGuid();

        SyncHub.GroupChannel(id).ShouldNotBe(SyncHub.UserChannel(id));
    }

    [Fact]
    public async Task The_broadcaster_sends_accepted_operations_to_the_group_channel()
    {
        var clients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        clients.Group(Arg.Any<string>()).Returns(proxy);
        var hubContext = Substitute.For<IHubContext<SyncHub>>();
        hubContext.Clients.Returns(clients);

        var groupId = Guid.NewGuid();
        var broadcaster = new SignalRSyncBroadcaster(hubContext);

        await broadcaster.BroadcastAsync(groupId, new SyncPushResult(
            [new SyncAcceptedDto(Guid.NewGuid(), Guid.NewGuid(), 4, new Dictionary<string, long>())],
            [], [], new Dictionary<Guid, long> { [groupId] = 4 }), TestData.DeviceA);

        clients.Received().Group(SyncHub.GroupChannel(groupId));
        await proxy.Received().SendCoreAsync("syncChanged", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_broadcaster_sends_a_conflict_only_to_the_user_who_caused_it()
    {
        var clients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        clients.Group(Arg.Any<string>()).Returns(proxy);
        var hubContext = Substitute.For<IHubContext<SyncHub>>();
        hubContext.Clients.Returns(clients);

        var userId = Guid.NewGuid();
        var broadcaster = new SignalRSyncBroadcaster(hubContext);

        await broadcaster.NotifyConflictAsync(Guid.NewGuid(), userId, new SyncConflictDto(
            Guid.NewGuid(), Guid.NewGuid(), SyncEntityType.Expense, Guid.NewGuid(),
            "{}", new Dictionary<string, long>(), "{}", new Dictionary<string, long>(),
            ["description"], DateTimeOffset.UtcNow));

        clients.Received().Group(SyncHub.UserChannel(userId));
        await proxy.Received().SendCoreAsync("syncConflict", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }
}
