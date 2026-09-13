using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Application;

/// <summary>
/// Names a group wants left out of its highlights.
///
/// A household with rent in it has one expense every month larger than everything
/// else put together, so "the biggest thing in August" answers "the rent" for ever.
/// These say which names to skip when picking that out - and nothing else: what a
/// month cost, who owes whom and every balance are money that moved, and a display
/// rule has no business touching them.
/// </summary>
public class GroupIgnoredNamesTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private async Task<(Guid UserId, GroupDto Group)> SetupAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob"]));
        return (user.Id, group);
    }

    private static UpdateGroupRequest Patterns(IReadOnlyList<string>? patterns)
        => new(null, null, null, null, null, null, null, patterns);

    [Fact]
    public async Task A_new_group_ignores_nothing()
    {
        var (_, group) = await SetupAsync();

        group.IgnoredNamePatterns.ShouldBeNull();
    }

    [Fact]
    public async Task Patterns_are_kept()
    {
        var (userId, group) = await SetupAsync();

        var updated = await Groups.UpdateAsync(userId, group.Id, Patterns(["Loyer", "^Hydro"]));

        updated.IgnoredNamePatterns.ShouldBe(["Loyer", "^Hydro"]);
    }

    [Fact]
    public async Task An_empty_list_clears_them()
    {
        var (userId, group) = await SetupAsync();
        await Groups.UpdateAsync(userId, group.Id, Patterns(["Loyer"]));

        var cleared = await Groups.UpdateAsync(userId, group.Id, Patterns([]));

        cleared.IgnoredNamePatterns.ShouldBeNull();
    }

    [Fact]
    public async Task Not_saying_anything_leaves_them_alone()
    {
        var (userId, group) = await SetupAsync();
        await Groups.UpdateAsync(userId, group.Id, Patterns(["Loyer"]));

        // A patch that renames the group should not drop a rule it never mentioned.
        var renamed = await Groups.UpdateAsync(userId, group.Id,
            new UpdateGroupRequest("Flat", null, null, null, null));

        renamed.Name.ShouldBe("Flat");
        renamed.IgnoredNamePatterns.ShouldBe(["Loyer"]);
    }

    [Fact]
    public async Task Blank_rows_and_repeats_are_dropped()
    {
        var (userId, group) = await SetupAsync();

        var updated = await Groups.UpdateAsync(userId, group.Id,
            Patterns(["Loyer", "  ", "loyer", "", "Hydro"]));

        // A blank row is somebody part-way through typing, and the same pattern twice
        // does the same job once.
        updated.IgnoredNamePatterns.ShouldBe(["Loyer", "Hydro"]);
    }

    [Fact]
    public async Task Anything_typed_is_a_pattern()
    {
        var (userId, group) = await SetupAsync();

        // These are globs, not regular expressions: there is no such thing as one
        // that fails to compile, so nothing here is rejected for its shape.
        var updated = await Groups.UpdateAsync(userId, group.Id, Patterns(["Loyer*", "*(("]));

        updated.IgnoredNamePatterns.ShouldBe(["Loyer*", "*(("]);
    }

    [Fact]
    public async Task A_list_of_them_is_bounded()
    {
        var (userId, group) = await SetupAsync();

        // A group setting is not a place to store a program, and an unbounded list of
        // expressions is a way to make somebody else's phone work hard.
        var many = Enumerable.Range(0, 11).Select(index => $"pattern{index}").ToList();

        await Should.ThrowAsync<ValidationException>(
            () => Groups.UpdateAsync(userId, group.Id, Patterns(many)));
    }

    [Fact]
    public async Task One_of_them_cannot_be_enormous()
    {
        var (userId, group) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(
            () => Groups.UpdateAsync(userId, group.Id, Patterns([new string('a', 201)])));
    }

    /// <summary>
    /// Who gets to decide.
    ///
    /// Everything else on a group's settings screen is an admin's: how costs are
    /// divided, who is in the group, what it is called. This is not that. It
    /// changes what a total reads and no amount, no balance and nothing anybody
    /// owes - and the person who notices that the rent is drowning out the month is
    /// rarely the one holding the owner's account.
    /// </summary>
    public class WhoCanSetThem(PostgresFixture fixture) : ServiceTestBase(fixture)
    {
        private async Task<(Guid OwnerId, Guid MemberId, GroupDto Group)> TwoOfUsAsync()
        {
            var owner = await TestData.SeedUserAsync(Db, "Nicolas", "nicolas@example.com");
            var other = await TestData.SeedUserAsync(Db, "Emma", "emma@example.com");

            var group = await Groups.CreateAsync(owner.Id,
                new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
            await Groups.AddUserMemberAsync(owner.Id, group.Id, new AddUserMemberRequest(other.Id));

            return (owner.Id, other.Id, group);
        }

        [Fact]
        public async Task Anybody_in_the_group_can_set_them()
        {
            var (_, memberId, group) = await TwoOfUsAsync();

            var updated = await Groups.SetIgnoredNamesAsync(
                memberId, group.Id, new SetIgnoredNamesRequest(["Loyer"]));

            updated.IgnoredNamePatterns.ShouldBe(["Loyer"]);
        }

        [Fact]
        public async Task An_ordinary_member_still_cannot_change_the_rest()
        {
            var (_, memberId, group) = await TwoOfUsAsync();

            // The line is drawn at money and membership, and it has not moved.
            await Should.ThrowAsync<ForbiddenException>(
                () => Groups.UpdateAsync(memberId, group.Id,
                    new UpdateGroupRequest("Renamed", null, null, null, null)));
        }

        [Fact]
        public async Task Somebody_outside_the_group_cannot()
        {
            var (_, _, group) = await TwoOfUsAsync();
            var stranger = await TestData.SeedUserAsync(Db, "Stranger", "stranger@example.com");

            await Should.ThrowAsync<ForbiddenException>(
                () => Groups.SetIgnoredNamesAsync(
                    stranger.Id, group.Id, new SetIgnoredNamesRequest(["Loyer"])));
        }

        [Fact]
        public async Task The_same_bounds_apply_however_it_is_set()
        {
            var (_, memberId, group) = await TwoOfUsAsync();
            var many = Enumerable.Range(0, 11).Select(index => $"pattern{index}").ToList();

            await Should.ThrowAsync<ValidationException>(
                () => Groups.SetIgnoredNamesAsync(memberId, group.Id, new SetIgnoredNamesRequest(many)));
        }

        [Fact]
        public async Task An_empty_list_clears_them()
        {
            var (ownerId, memberId, group) = await TwoOfUsAsync();
            await Groups.SetIgnoredNamesAsync(ownerId, group.Id, new SetIgnoredNamesRequest(["Loyer"]));

            var cleared = await Groups.SetIgnoredNamesAsync(
                memberId, group.Id, new SetIgnoredNamesRequest([]));

            cleared.IgnoredNamePatterns.ShouldBeNull();
        }
    }
}
