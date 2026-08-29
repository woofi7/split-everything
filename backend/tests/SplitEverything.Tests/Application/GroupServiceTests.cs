using Microsoft.EntityFrameworkCore;
using Shouldly;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Domain.Common;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

public class GroupServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private static CreateGroupRequest Request(
        string name = "Roommates", string currency = "CAD",
        IReadOnlyList<string>? placeholders = null)
        => new(name, currency, null, null, null, placeholders);

    [Fact]
    public async Task Creating_a_group_makes_the_creator_its_owner()
    {
        var user = await TestData.SeedUserAsync(Db);

        var group = await Groups.CreateAsync(user.Id, Request());

        var owner = group.Members.ShouldHaveSingleItem();
        owner.UserId.ShouldBe(user.Id);
        owner.Role.ShouldBe(GroupRole.Owner);
        owner.DisplayName.ShouldBe(user.DisplayName);
    }

    [Fact]
    public async Task Creating_a_group_seeds_the_placeholder_members_it_was_given()
    {
        var user = await TestData.SeedUserAsync(Db);

        var group = await Groups.CreateAsync(user.Id, Request(placeholders: ["Bob", "Carol"]));

        group.Members.Count.ShouldBe(3);
        group.Members.Count(m => m.IsPlaceholder).ShouldBe(2);
    }

    [Fact]
    public async Task Creating_a_group_records_it_in_the_sync_log()
    {
        var user = await TestData.SeedUserAsync(Db);

        var group = await Groups.CreateAsync(user.Id, Request());

        var entries = await NewContext().SyncLog.Where(e => e.GroupId == group.Id).ToListAsync();
        entries.ShouldContain(e => e.EntityType == SyncEntityType.Group && e.Operation == SyncOperation.Create);
        entries.ShouldContain(e => e.EntityType == SyncEntityType.GroupMember);
    }

    [Fact]
    public async Task Creating_a_group_records_it_in_the_activity_feed()
    {
        var user = await TestData.SeedUserAsync(Db);

        var group = await Groups.CreateAsync(user.Id, Request());

        (await NewContext().ActivityLog.Where(a => a.GroupId == group.Id).ToListAsync())
            .ShouldContain(a => a.Kind == ActivityKind.GroupCreated);
    }

    [Fact]
    public async Task A_new_group_starts_with_a_zero_balance_and_no_spend()
    {
        var user = await TestData.SeedUserAsync(Db);

        var group = await Groups.CreateAsync(user.Id, Request());

        group.MyNetBalance.ShouldBe(0m);
        group.TotalSpend.ShouldBe(0m);
        group.ExpenseCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_group_gets_its_own_lineage_id()
    {
        var user = await TestData.SeedUserAsync(Db);

        var first = await Groups.CreateAsync(user.Id, Request("One"));
        var second = await Groups.CreateAsync(user.Id, Request("Two"));

        first.LineageId.ShouldNotBe(Guid.Empty);
        first.LineageId.ShouldNotBe(second.LineageId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_group_needs_a_name(string name)
    {
        var user = await TestData.SeedUserAsync(Db);

        await Should.ThrowAsync<ValidationException>(() => Groups.CreateAsync(user.Id, Request(name)));
    }

    [Theory]
    [InlineData("C")]
    [InlineData("CADX")]
    [InlineData("12")]
    public async Task A_group_needs_a_three_letter_currency(string currency)
    {
        var user = await TestData.SeedUserAsync(Db);

        await Should.ThrowAsync<ValidationException>(
            () => Groups.CreateAsync(user.Id, Request(currency: currency)));
    }

    [Fact]
    public async Task The_currency_is_stored_upper_cased()
    {
        var user = await TestData.SeedUserAsync(Db);

        (await Groups.CreateAsync(user.Id, Request(currency: "eur"))).BaseCurrency.ShouldBe("EUR");
    }

    [Fact]
    public async Task Listing_returns_only_the_groups_the_caller_belongs_to()
    {
        var alice = await TestData.SeedUserAsync(Db, "Alice");
        var bob = await TestData.SeedUserAsync(Db, "Bob");
        await Groups.CreateAsync(alice.Id, Request("Alice group"));
        await Groups.CreateAsync(bob.Id, Request("Bob group"));

        var forAlice = await Groups.ListAsync(alice.Id);

        forAlice.ShouldHaveSingleItem().Name.ShouldBe("Alice group");
    }

    [Fact]
    public async Task Listing_hides_archived_groups_unless_asked()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());
        await Groups.ArchiveAsync(user.Id, group.Id);

        (await Groups.ListAsync(user.Id)).ShouldBeEmpty();
        (await Groups.ListAsync(user.Id, includeArchived: true)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Reading_a_group_you_are_not_in_is_forbidden()
    {
        var alice = await TestData.SeedUserAsync(Db, "Alice");
        var bob = await TestData.SeedUserAsync(Db, "Bob");
        var group = await Groups.CreateAsync(alice.Id, Request());

        await Should.ThrowAsync<ForbiddenException>(() => Groups.GetAsync(bob.Id, group.Id));
    }

    [Fact]
    public async Task Reading_a_group_that_does_not_exist_is_a_not_found()
    {
        var user = await TestData.SeedUserAsync(Db);

        await Should.ThrowAsync<NotFoundException>(() => Groups.GetAsync(user.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Updating_changes_only_the_fields_that_were_supplied()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request("Original"));

        var updated = await Groups.UpdateAsync(user.Id, group.Id,
            new UpdateGroupRequest("Renamed", null, null, null, null));

        updated.Name.ShouldBe("Renamed");
        updated.BaseCurrency.ShouldBe(group.BaseCurrency);
        updated.ColorHex.ShouldBe(group.ColorHex);
    }

    [Fact]
    public async Task An_icon_can_be_set_and_changed()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());

        var withIcon = await Groups.UpdateAsync(user.Id, group.Id,
            new UpdateGroupRequest(null, null, "house", null, null));
        withIcon.IconName.ShouldBe("house");

        var changed = await Groups.UpdateAsync(user.Id, group.Id,
            new UpdateGroupRequest(null, null, "mountain-sun", null, null));
        changed.IconName.ShouldBe("mountain-sun");
    }

    [Fact]
    public async Task An_icon_can_be_removed_with_an_empty_string()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());
        await Groups.UpdateAsync(user.Id, group.Id,
            new UpdateGroupRequest(null, null, "house", null, null));

        // Null means "not supplied" in a patch, so it cannot also mean "clear".
        // An empty string is the explicit clear, or the remove button in the icon
        // picker would silently do nothing.
        var cleared = await Groups.UpdateAsync(user.Id, group.Id,
            new UpdateGroupRequest(null, null, string.Empty, null, null));

        cleared.IconName.ShouldBeNull();
    }

    [Fact]
    public async Task Omitting_the_icon_leaves_it_alone()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());
        await Groups.UpdateAsync(user.Id, group.Id,
            new UpdateGroupRequest(null, null, "house", null, null));

        var renamed = await Groups.UpdateAsync(user.Id, group.Id,
            new UpdateGroupRequest("Renamed", null, null, null, null));

        renamed.IconName.ShouldBe("house");
    }

    [Fact]
    public async Task A_description_can_be_removed_the_same_way()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", "A description", null, null, null));

        var cleared = await Groups.UpdateAsync(user.Id, group.Id,
            new UpdateGroupRequest(null, string.Empty, null, null, null));

        cleared.Description.ShouldBeNull();
    }

    [Fact]
    public async Task An_icon_name_longer_than_the_column_is_refused_rather_than_truncated()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());

        // Truncating would store a name that resolves to no icon at all.
        await Should.ThrowAsync<ValidationException>(() => Groups.UpdateAsync(
            user.Id, group.Id, new UpdateGroupRequest(null, null, new string('a', 49), null, null)));
    }

    [Fact]
    public async Task A_group_can_be_created_with_an_icon()
    {
        var user = await TestData.SeedUserAsync(Db);

        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Trip", "CAD", null, "person-skiing", null, null));

        group.IconName.ShouldBe("person-skiing");
    }

    [Fact]
    public async Task Updating_advances_the_clock_and_the_cursor()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());

        var updated = await Groups.UpdateAsync(user.Id, group.Id,
            new UpdateGroupRequest("Renamed", null, null, null, null));

        updated.SequenceCounter.ShouldBeGreaterThan(group.SequenceCounter);
    }

    [Fact]
    public async Task Only_an_owner_or_admin_can_update_a_group()
    {
        var owner = await TestData.SeedUserAsync(Db, "Alice");
        var other = await TestData.SeedUserAsync(Db, "Bob");
        var group = await Groups.CreateAsync(owner.Id, Request());
        Db.GroupMembers.Add(TestData.Member(group.Id, other.Id, "Bob"));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        await Should.ThrowAsync<ForbiddenException>(() => Groups.UpdateAsync(
            other.Id, group.Id, new UpdateGroupRequest("Nope", null, null, null, null)));
    }

    [Fact]
    public async Task Archiving_freezes_the_group_against_new_writes()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());

        (await Groups.ArchiveAsync(user.Id, group.Id)).IsArchived.ShouldBeTrue();

        await Should.ThrowAsync<GroupArchivedException>(() => Groups.AddPlaceholderMemberAsync(
            user.Id, group.Id, new AddPlaceholderMemberRequest("Bob")));
    }

    [Fact]
    public async Task Archiving_does_not_delete_anything()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request(placeholders: ["Bob"]));

        await Groups.ArchiveAsync(user.Id, group.Id);

        var fresh = NewContext();
        (await fresh.Groups.CountAsync(g => g.Id == group.Id)).ShouldBe(1);
        (await fresh.GroupMembers.CountAsync(m => m.GroupId == group.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task An_archived_group_can_be_brought_back()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());
        await Groups.ArchiveAsync(user.Id, group.Id);

        (await Groups.UnarchiveAsync(user.Id, group.Id)).IsArchived.ShouldBeFalse();
    }

    [Fact]
    public async Task Archiving_an_already_archived_group_is_harmless()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());
        await Groups.ArchiveAsync(user.Id, group.Id);

        (await Groups.ArchiveAsync(user.Id, group.Id)).IsArchived.ShouldBeTrue();
    }

    [Fact]
    public async Task Adding_a_placeholder_member_returns_it_unclaimed()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());

        var member = await Groups.AddPlaceholderMemberAsync(
            user.Id, group.Id, new AddPlaceholderMemberRequest("Bob"));

        member.IsPlaceholder.ShouldBeTrue();
        member.UserId.ShouldBeNull();
        member.DisplayName.ShouldBe("Bob");
    }

    [Fact]
    public async Task A_placeholder_member_needs_a_name()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());

        await Should.ThrowAsync<ValidationException>(() => Groups.AddPlaceholderMemberAsync(
            user.Id, group.Id, new AddPlaceholderMemberRequest("  ")));
    }

    [Fact]
    public async Task A_member_with_no_history_is_removed_outright()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request(placeholders: ["Bob"]));
        var bob = group.Members.First(m => m.DisplayName == "Bob");

        await Groups.RemoveMemberAsync(user.Id, group.Id, bob.Id);

        var members = (await Groups.GetAsync(user.Id, group.Id)).Members;
        members.ShouldNotContain(m => m.Id == bob.Id);
    }

    [Fact]
    public async Task A_member_with_history_is_deactivated_so_balances_survive()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request(placeholders: ["Bob"]));
        var bob = group.Members.First(m => m.DisplayName == "Bob");
        Db.Expenses.Add(TestData.Expense(group.Id, bob.Id, 40m));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        await Groups.RemoveMemberAsync(user.Id, group.Id, bob.Id);

        var reloaded = await NewContext().GroupMembers.FirstAsync(m => m.Id == bob.Id);
        reloaded.Status.ShouldBe(MembershipStatus.Removed);
        reloaded.LeftAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_last_owner_cannot_be_removed()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());
        var owner = group.Members.Single();

        await Should.ThrowAsync<ValidationException>(
            () => Groups.RemoveMemberAsync(user.Id, group.Id, owner.Id));
    }

    [Fact]
    public async Task Removing_a_member_of_another_group_is_a_not_found()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());
        var other = await Groups.CreateAsync(user.Id, Request("Other", placeholders: ["Zoe"]));
        var zoe = other.Members.First(m => m.DisplayName == "Zoe");

        await Should.ThrowAsync<NotFoundException>(
            () => Groups.RemoveMemberAsync(user.Id, group.Id, zoe.Id));
    }

    [Fact]
    public async Task A_group_reports_the_balances_of_its_members()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request(placeholders: ["Bob"]));
        var alice = group.Members.First(m => m.UserId == user.Id);
        var bob = group.Members.First(m => m.DisplayName == "Bob");

        var expense = TestData.Expense(group.Id, alice.Id, 100m);
        Db.Expenses.Add(expense);
        Db.ExpenseSplits.Add(TestData.Split(expense.Id, group.Id, alice.Id, 50m));
        Db.ExpenseSplits.Add(TestData.Split(expense.Id, group.Id, bob.Id, 50m));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Groups.GetAsync(user.Id, group.Id);

        reloaded.MyNetBalance.ShouldBe(50m);
        reloaded.Members.First(m => m.Id == bob.Id).NetBalance.ShouldBe(-50m);
        reloaded.TotalSpend.ShouldBe(100m);
        reloaded.ExpenseCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_deleted_expense_does_not_count_toward_the_group_total()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());
        var alice = group.Members.Single();
        var expense = TestData.Expense(group.Id, alice.Id, 100m);
        expense.IsDeleted = true;
        Db.Expenses.Add(expense);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var reloaded = await Groups.GetAsync(user.Id, group.Id);

        reloaded.TotalSpend.ShouldBe(0m);
        reloaded.ExpenseCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_group_summary_reports_the_last_activity_time()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id, Request());

        (await Groups.ListAsync(user.Id)).ShouldHaveSingleItem()
            .LastActivityAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_removed_member_loses_access_to_the_group()
    {
        var owner = await TestData.SeedUserAsync(Db, "Alice");
        var bob = await TestData.SeedUserAsync(Db, "Bob");
        var group = await Groups.CreateAsync(owner.Id, Request());
        var member = TestData.Member(group.Id, bob.Id, "Bob");
        Db.GroupMembers.Add(member);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        await Groups.RemoveMemberAsync(owner.Id, group.Id, member.Id);

        await Should.ThrowAsync<ForbiddenException>(() => Groups.GetAsync(bob.Id, group.Id));
    }
}
