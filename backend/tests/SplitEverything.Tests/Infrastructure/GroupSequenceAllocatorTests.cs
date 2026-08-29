using Microsoft.EntityFrameworkCore;
using Shouldly;
using SplitEverything.Infrastructure.Sync;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Infrastructure;

/// <summary>
/// The per-group cursor every delta pull depends on. If it ever hands the same
/// number to two writers, a client's "everything after N" pull silently skips a
/// change, so the concurrency behaviour is the point of these tests.
/// </summary>
public class GroupSequenceAllocatorTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task The_first_allocation_is_one()
    {
        var (group, _) = await SeedAsync();
        var allocator = new GroupSequenceAllocator(Db);

        (await allocator.NextAsync(group.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Allocations_increase_by_one()
    {
        var (group, _) = await SeedAsync();
        var allocator = new GroupSequenceAllocator(Db);

        var first = await allocator.NextAsync(group.Id);
        var second = await allocator.NextAsync(group.Id);
        var third = await allocator.NextAsync(group.Id);

        new[] { first, second, third }.ShouldBe(new long[] { 1, 2, 3 });
    }

    [Fact]
    public async Task The_counter_is_persisted_on_the_group()
    {
        var (group, _) = await SeedAsync();
        await new GroupSequenceAllocator(Db).NextAsync(group.Id);
        await new GroupSequenceAllocator(Db).NextAsync(group.Id);

        (await NewContext().Groups.FirstAsync(g => g.Id == group.Id))
            .SequenceCounter.ShouldBe(2);
    }

    [Fact]
    public async Task Each_group_counts_independently()
    {
        var (first, user) = await SeedAsync();
        var second = TestData.Group(user.Id, "Trip");
        Db.Groups.Add(second);
        await Db.SaveChangesAsync();

        var allocator = new GroupSequenceAllocator(Db);
        await allocator.NextAsync(first.Id);
        await allocator.NextAsync(first.Id);

        (await allocator.NextAsync(second.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_tracked_group_sees_the_allocation_it_just_made()
    {
        var (group, _) = await SeedAsync();
        var tracked = await Db.Groups.FirstAsync(g => g.Id == group.Id);
        var allocator = new GroupSequenceAllocator(Db);

        await allocator.NextAsync(group.Id);
        await allocator.NextAsync(group.Id);

        // A caller that goes on to renumber rows from this counter would otherwise
        // reuse numbers already handed out and collide on (group, seq).
        tracked.SequenceCounter.ShouldBe(2);
    }

    [Fact]
    public async Task An_allocation_is_not_undone_by_a_later_save()
    {
        var (group, _) = await SeedAsync();
        var tracked = await Db.Groups.FirstAsync(g => g.Id == group.Id);
        await new GroupSequenceAllocator(Db).NextAsync(group.Id);

        tracked.Name = "Renamed";
        await Db.SaveChangesAsync();

        (await NewContext().Groups.FirstAsync(g => g.Id == group.Id)).SequenceCounter.ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_group_is_rejected()
        => await Should.ThrowAsync<InvalidOperationException>(
            () => new GroupSequenceAllocator(Db).NextAsync(Guid.NewGuid()));

    [Fact]
    public async Task Concurrent_writers_never_receive_the_same_number()
    {
        var (group, _) = await SeedAsync();
        const int writers = 24;

        // Separate contexts, so this really is 24 connections racing on one row
        // rather than one change tracker serialising them for us.
        var results = await Task.WhenAll(Enumerable.Range(0, writers).Select(async _ =>
        {
            await using var context = NewContext();
            return await new GroupSequenceAllocator(context).NextAsync(group.Id);
        }));

        results.Distinct().Count().ShouldBe(writers);
        results.Order().ShouldBe(Enumerable.Range(1, writers).Select(i => (long)i));
    }

    [Fact]
    public async Task Concurrent_writers_across_two_groups_do_not_interfere()
    {
        var (first, user) = await SeedAsync();
        var second = TestData.Group(user.Id, "Trip");
        Db.Groups.Add(second);
        await Db.SaveChangesAsync();

        var ids = Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? first.Id : second.Id);
        var results = await Task.WhenAll(ids.Select(async groupId =>
        {
            await using var context = NewContext();
            return (GroupId: groupId, Seq: await new GroupSequenceAllocator(context).NextAsync(groupId));
        }));

        foreach (var perGroup in results.GroupBy(r => r.GroupId))
            perGroup.Select(r => r.Seq).Order().ShouldBe(Enumerable.Range(1, 10).Select(i => (long)i));
    }

    private async Task<(SplitEverything.Domain.Entities.Group Group, SplitEverything.Domain.Entities.User User)> SeedAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, _) = await TestData.SeedGroupAsync(Db, user, "Alice");
        return (group, user);
    }
}
