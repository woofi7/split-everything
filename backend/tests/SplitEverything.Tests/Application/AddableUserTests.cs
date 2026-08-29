using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Application;

/// <summary>
/// Adding someone who already has an account.
///
/// The other route into a group is an invite link, which suits someone who has
/// never opened the app. For a person who is already here, sending a link and
/// waiting is the wrong shape: they should be findable and addable directly.
/// </summary>
public class AddableUserTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private async Task<(Guid OwnerId, GroupDto Group)> SetupAsync()
    {
        var owner = await TestData.SeedUserAsync(Db, "Olivia", "owner@example.com", googleSub: "google-owner@example.com");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, []));
        return (owner.Id, group);
    }

    [Fact]
    public async Task Lists_other_people_with_an_account()
    {
        var (ownerId, group) = await SetupAsync();
        await TestData.SeedUserAsync(Db, "Bob", "bob@example.com", googleSub: "google-bob@example.com");
        await TestData.SeedUserAsync(Db, "Carol", "carol@example.com", googleSub: "google-carol@example.com");

        var addable = await Groups.ListAddableUsersAsync(ownerId, group.Id);

        addable.Select(u => u.DisplayName).ShouldBe(["Bob", "Carol"], ignoreOrder: true);
    }

    [Fact]
    public async Task Leaves_out_the_person_doing_the_adding()
    {
        var (ownerId, group) = await SetupAsync();

        var addable = await Groups.ListAddableUsersAsync(ownerId, group.Id);

        addable.ShouldBeEmpty();
    }

    [Fact]
    public async Task Leaves_out_people_already_in_the_group()
    {
        var (ownerId, group) = await SetupAsync();
        var bob = await TestData.SeedUserAsync(Db, "Bob", "bob@example.com", googleSub: "google-bob@example.com");

        await Groups.AddUserMemberAsync(ownerId, group.Id, new AddUserMemberRequest(bob.Id));
        var addable = await Groups.ListAddableUsersAsync(ownerId, group.Id);

        addable.ShouldBeEmpty();
    }

    [Fact]
    public async Task Includes_someone_who_was_removed_from_the_group()
    {
        var (ownerId, group) = await SetupAsync();
        var bob = await TestData.SeedUserAsync(Db, "Bob", "bob@example.com", googleSub: "google-bob@example.com");
        var member = await Groups.AddUserMemberAsync(ownerId, group.Id, new AddUserMemberRequest(bob.Id));

        await Groups.RemoveMemberAsync(ownerId, group.Id, member.Id);
        var addable = await Groups.ListAddableUsersAsync(ownerId, group.Id);

        // They left, so they can be asked back.
        addable.ShouldHaveSingleItem().DisplayName.ShouldBe("Bob");
    }

    [Fact]
    public async Task Carries_the_email_so_two_people_with_one_name_can_be_told_apart()
    {
        var (ownerId, group) = await SetupAsync();
        await TestData.SeedUserAsync(Db, "Bob", "bob.smith@example.com", googleSub: "google-bob.smith@example.com");
        await TestData.SeedUserAsync(Db, "Bob", "bob.jones@example.com", googleSub: "google-bob.jones@example.com");

        var addable = await Groups.ListAddableUsersAsync(ownerId, group.Id);

        addable.Select(u => u.Email).ShouldBe(
            ["bob.smith@example.com", "bob.jones@example.com"], ignoreOrder: true);
    }

    [Fact]
    public async Task Lists_everyone_but_the_caller_when_no_group_is_named()
    {
        // The new-group screen has no group yet.
        var (ownerId, _) = await SetupAsync();
        await TestData.SeedUserAsync(Db, "Bob", "bob@example.com", googleSub: "google-bob@example.com");

        var addable = await Groups.ListAddableUsersAsync(ownerId, null);

        addable.ShouldHaveSingleItem().DisplayName.ShouldBe("Bob");
    }

    [Fact]
    public async Task Refuses_to_list_for_a_group_the_caller_is_not_in()
    {
        var (_, group) = await SetupAsync();
        var stranger = await TestData.SeedUserAsync(Db, "Stranger", "stranger@example.com", googleSub: "google-stranger@example.com");

        await Should.ThrowAsync<ForbiddenException>(
            () => Groups.ListAddableUsersAsync(stranger.Id, group.Id));
    }

    [Fact]
    public async Task Adds_a_user_as_a_real_member_not_a_placeholder()
    {
        var (ownerId, group) = await SetupAsync();
        var bob = await TestData.SeedUserAsync(Db, "Bob", "bob@example.com", googleSub: "google-bob@example.com");

        var member = await Groups.AddUserMemberAsync(ownerId, group.Id, new AddUserMemberRequest(bob.Id));

        member.UserId.ShouldBe(bob.Id);
        member.DisplayName.ShouldBe("Bob");
        member.IsPlaceholder.ShouldBeFalse();

        var stored = await Db.GroupMembers.SingleAsync(m => m.Id == member.Id);
        stored.UserId.ShouldBe(bob.Id);
        stored.Status.ShouldBe(MembershipStatus.Active);
    }

    [Fact]
    public async Task Adding_a_user_shows_up_in_the_group_they_can_now_see()
    {
        var (ownerId, group) = await SetupAsync();
        var bob = await TestData.SeedUserAsync(Db, "Bob", "bob@example.com", googleSub: "google-bob@example.com");

        await Groups.AddUserMemberAsync(ownerId, group.Id, new AddUserMemberRequest(bob.Id));

        var theirs = await Groups.ListAsync(bob.Id);
        theirs.ShouldHaveSingleItem().Name.ShouldBe("Roommates");
    }

    [Fact]
    public async Task Adding_someone_twice_returns_the_membership_they_already_have()
    {
        var (ownerId, group) = await SetupAsync();
        var bob = await TestData.SeedUserAsync(Db, "Bob", "bob@example.com", googleSub: "google-bob@example.com");

        var first = await Groups.AddUserMemberAsync(ownerId, group.Id, new AddUserMemberRequest(bob.Id));
        var second = await Groups.AddUserMemberAsync(ownerId, group.Id, new AddUserMemberRequest(bob.Id));

        second.Id.ShouldBe(first.Id);
        (await Db.GroupMembers.CountAsync(m => m.GroupId == group.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task Re_adding_someone_who_left_reclaims_their_original_row()
    {
        var (ownerId, group) = await SetupAsync();
        var bob = await TestData.SeedUserAsync(Db, "Bob", "bob@example.com", googleSub: "google-bob@example.com");
        var member = await Groups.AddUserMemberAsync(ownerId, group.Id, new AddUserMemberRequest(bob.Id));
        await Groups.RemoveMemberAsync(ownerId, group.Id, member.Id);

        var again = await Groups.AddUserMemberAsync(ownerId, group.Id, new AddUserMemberRequest(bob.Id));

        // A second row would collide with the one-membership-per-user index and
        // orphan whatever history is attached to the first.
        again.Id.ShouldBe(member.Id);
        (await Db.GroupMembers.CountAsync(m => m.GroupId == group.Id && m.UserId == bob.Id)).ShouldBe(1);

        var stored = await Db.GroupMembers.SingleAsync(m => m.Id == member.Id);
        stored.IsDeleted.ShouldBeFalse();
        stored.Status.ShouldBe(MembershipStatus.Active);
    }

    [Fact]
    public async Task Refuses_a_user_who_does_not_exist()
    {
        var (ownerId, group) = await SetupAsync();

        await Should.ThrowAsync<NotFoundException>(
            () => Groups.AddUserMemberAsync(ownerId, group.Id, new AddUserMemberRequest(Guid.NewGuid())));
    }

    [Fact]
    public async Task Refuses_when_the_caller_is_not_in_the_group()
    {
        var (_, group) = await SetupAsync();
        var stranger = await TestData.SeedUserAsync(Db, "Stranger", "stranger@example.com", googleSub: "google-stranger@example.com");
        var bob = await TestData.SeedUserAsync(Db, "Bob", "bob@example.com", googleSub: "google-bob@example.com");

        await Should.ThrowAsync<ForbiddenException>(
            () => Groups.AddUserMemberAsync(stranger.Id, group.Id, new AddUserMemberRequest(bob.Id)));
    }

    [Fact]
    public async Task Refuses_on_an_archived_group()
    {
        var (ownerId, group) = await SetupAsync();
        var bob = await TestData.SeedUserAsync(Db, "Bob", "bob@example.com", googleSub: "google-bob@example.com");
        await Groups.ArchiveAsync(ownerId, group.Id);

        await Should.ThrowAsync<GroupArchivedException>(
            () => Groups.AddUserMemberAsync(ownerId, group.Id, new AddUserMemberRequest(bob.Id)));
    }
}
