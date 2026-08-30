using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Domain.Common;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Application;

/// <summary>
/// How a group splits by default.
///
/// A household that always divides rent sixty forty had to say so on every
/// expense. This is a group setting rather than a device preference, because how
/// a household divides its costs is a fact about the household: it should hold on
/// whichever phone the next expense is typed on.
/// </summary>
public class GroupDefaultSplitTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private async Task<(Guid UserId, GroupDto Group, Guid Alice, Guid Bob)> SetupAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob"]));
        return (user.Id, group,
            group.Members.First(m => m.UserId == user.Id).Id,
            group.Members.First(m => m.DisplayName == "Bob").Id);
    }

    private static UpdateGroupRequest Default(
        SplitType? type, IReadOnlyDictionary<Guid, decimal>? values = null)
        => new(null, null, null, null, null, type, values);

    [Fact]
    public async Task A_new_group_splits_equally()
    {
        var (_, group, _, _) = await SetupAsync();

        group.DefaultSplitType.ShouldBe(SplitType.Equal);
        group.DefaultSplitValues.ShouldBeNull();
    }

    [Fact]
    public async Task A_default_can_be_set()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        var updated = await Groups.UpdateAsync(userId, group.Id, Default(
            SplitType.Shares, new Dictionary<Guid, decimal> { [alice] = 2m, [bob] = 1m }));

        updated.DefaultSplitType.ShouldBe(SplitType.Shares);
        updated.DefaultSplitValues!.Count.ShouldBe(2);
        updated.DefaultSplitValues[alice].ShouldBe(2m);
    }

    [Fact]
    public async Task It_survives_a_reread()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        await Groups.UpdateAsync(userId, group.Id, Default(
            SplitType.Percentage, new Dictionary<Guid, decimal> { [alice] = 60m, [bob] = 40m }));

        var read = await Groups.GetAsync(userId, group.Id);

        read.DefaultSplitType.ShouldBe(SplitType.Percentage);
        read.DefaultSplitValues![bob].ShouldBe(40m);
    }

    [Fact]
    public async Task Going_back_to_equal_drops_the_values()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await Groups.UpdateAsync(userId, group.Id, Default(
            SplitType.Shares, new Dictionary<Guid, decimal> { [alice] = 2m, [bob] = 1m }));

        var updated = await Groups.UpdateAsync(userId, group.Id, Default(SplitType.Equal));

        // An equal split needs no values, and keeping them would resurrect an old
        // ratio the next time someone chose shares.
        updated.DefaultSplitType.ShouldBe(SplitType.Equal);
        updated.DefaultSplitValues.ShouldBeNull();
    }

    [Fact]
    public async Task An_empty_map_clears_the_values_but_keeps_the_type()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await Groups.UpdateAsync(userId, group.Id, Default(
            SplitType.Shares, new Dictionary<Guid, decimal> { [alice] = 2m, [bob] = 1m }));

        var updated = await Groups.UpdateAsync(userId, group.Id,
            Default(SplitType.Shares, new Dictionary<Guid, decimal>()));

        updated.DefaultSplitType.ShouldBe(SplitType.Shares);
        updated.DefaultSplitValues.ShouldBeNull();
    }

    [Fact]
    public async Task Not_naming_a_type_leaves_the_default_alone()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await Groups.UpdateAsync(userId, group.Id, Default(
            SplitType.Shares, new Dictionary<Guid, decimal> { [alice] = 2m, [bob] = 1m }));

        // A patch that renames the group must not reset how it splits.
        var updated = await Groups.UpdateAsync(userId, group.Id,
            new UpdateGroupRequest("Flat", null, null, null, null));

        updated.Name.ShouldBe("Flat");
        updated.DefaultSplitType.ShouldBe(SplitType.Shares);
        updated.DefaultSplitValues!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_value_for_someone_outside_the_group_is_refused()
    {
        var (userId, group, alice, _) = await SetupAsync();

        // It would sit in the group forever, silently ignored by every form.
        await Should.ThrowAsync<ValidationException>(() => Groups.UpdateAsync(userId, group.Id,
            Default(SplitType.Shares, new Dictionary<Guid, decimal>
            {
                [alice] = 2m,
                [Guid.NewGuid()] = 1m
            })));
    }

    [Fact]
    public async Task A_negative_value_is_refused()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Groups.UpdateAsync(userId, group.Id,
            Default(SplitType.Shares, new Dictionary<Guid, decimal> { [alice] = -1m, [bob] = 1m })));
    }

    [Fact]
    public async Task Only_an_admin_can_change_it()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var other = await TestData.SeedUserAsync(Db, "Carol", "carol@example.com", "google-carol");
        await Groups.AddUserMemberAsync(userId, group.Id, new AddUserMemberRequest(other.Id));

        // It changes what everyone else's next expense does, so it is not a
        // per-member preference.
        await Should.ThrowAsync<ForbiddenException>(() =>
            Groups.UpdateAsync(other.Id, group.Id, Default(SplitType.Shares)));
    }

    [Fact]
    public async Task Stored_values_of_the_wrong_shape_read_as_no_default()
    {
        var (userId, group, _, _) = await SetupAsync();

        var context = NewContext();
        var stored = await context.Groups.SingleAsync(g => g.Id == group.Id);
        stored.DefaultSplitType = SplitType.Shares;
        // Valid JSON, wrong shape. Postgres refuses outright invalid JSON in a
        // jsonb column, so this is the case the guard is actually for: a shape
        // written by an older version of this app.
        stored.DefaultSplitValuesJson = "[1, 2, 3]";
        await context.SaveChangesAsync();

        // It must not stop the group loading.
        var read = await Groups.GetAsync(userId, group.Id);
        read.DefaultSplitValues.ShouldBeNull();
    }

    [Fact]
    public async Task The_default_travels_in_the_sync_payload()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        await Groups.UpdateAsync(userId, group.Id, Default(
            SplitType.Percentage, new Dictionary<Guid, decimal> { [alice] = 60m, [bob] = 40m }));

        // So another device learns it from the delta pull, not only a full read.
        var entry = await NewContext().SyncLog
            .Where(e => e.GroupId == group.Id && e.EntityType == SyncEntityType.Group)
            .OrderByDescending(e => e.ServerSeq)
            .FirstAsync();

        entry.PayloadJson.ShouldContain("defaultSplitType");
    }
}
