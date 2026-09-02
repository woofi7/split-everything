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
}
