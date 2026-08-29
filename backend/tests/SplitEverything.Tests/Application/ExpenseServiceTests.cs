using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Domain.Common;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

public class ExpenseServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private async Task<(Guid UserId, GroupDto Group, Guid Alice, Guid Bob)> SetupAsync(string currency = "CAD")
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", currency, null, null, null, ["Bob"]));
        return (user.Id,
            group,
            group.Members.First(m => m.UserId == user.Id).Id,
            group.Members.First(m => m.DisplayName == "Bob").Id);
    }

    private static CreateExpenseRequest Create(
        Guid groupId, Guid payer, decimal amount,
        IReadOnlyList<SplitInputDto> splits,
        SplitType splitType = SplitType.Equal,
        string currency = "CAD",
        string description = "Dinner",
        IReadOnlyList<ExpenseItemDto>? items = null,
        Guid? clientId = null,
        Guid? categoryId = null)
        => new(groupId, payer, description, amount, currency, TestData.Jan1, splitType,
            splits, categoryId, items, null, null, clientId, null, null);

    [Fact]
    public async Task An_equal_split_charges_each_participant_their_share()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 100m,
            [new SplitInputDto(alice, null), new SplitInputDto(bob, null)]));

        expense.Splits.Count.ShouldBe(2);
        expense.Splits.ShouldAllBe(s => s.Amount == 50m);
    }

    [Fact]
    public async Task Split_amounts_always_add_up_to_the_expense_total()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 33.33m,
            [new SplitInputDto(alice, null), new SplitInputDto(bob, null)]));

        expense.Splits.Sum(s => s.Amount).ShouldBe(33.33m);
    }

    [Fact]
    public async Task A_percentage_split_keeps_the_percentages_the_user_typed()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 200m,
            [new SplitInputDto(alice, 30m), new SplitInputDto(bob, 70m)],
            SplitType.Percentage));

        expense.Splits.Single(s => s.MemberId == alice).Amount.ShouldBe(60m);
        expense.Splits.Single(s => s.MemberId == alice).InputValue.ShouldBe(30m);
    }

    [Fact]
    public async Task A_shares_split_weights_by_share_count()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 90m,
            [new SplitInputDto(alice, 1m), new SplitInputDto(bob, 2m)],
            SplitType.Shares));

        expense.Splits.Single(s => s.MemberId == bob).Amount.ShouldBe(60m);
    }

    [Fact]
    public async Task An_exact_split_uses_the_amounts_given()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 100m,
            [new SplitInputDto(alice, 25m), new SplitInputDto(bob, 75m)],
            SplitType.ExactAmount));

        expense.Splits.Single(s => s.MemberId == bob).Amount.ShouldBe(75m);
    }

    [Fact]
    public async Task An_exact_split_that_does_not_add_up_is_rejected()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Expenses.CreateAsync(userId,
            Create(group.Id, alice, 100m,
                [new SplitInputDto(alice, 25m), new SplitInputDto(bob, 25m)],
                SplitType.ExactAmount)));
    }

    [Fact]
    public async Task An_itemized_expense_charges_each_line_to_whoever_had_it()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 30m,
            [new SplitInputDto(alice, null), new SplitInputDto(bob, null)],
            SplitType.Itemized,
            items:
            [
                new ExpenseItemDto(null, "Appetizer", 10m, 1, 0, [bob]),
                new ExpenseItemDto(null, "Mains", 20m, 1, 1, [alice])
            ]));

        expense.Splits.Single(s => s.MemberId == bob).Amount.ShouldBe(10m);
        expense.Splits.Single(s => s.MemberId == alice).Amount.ShouldBe(20m);
        expense.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_itemized_expense_spreads_tax_over_the_people_who_ordered()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 36m,
            [new SplitInputDto(alice, null), new SplitInputDto(bob, null)],
            SplitType.Itemized,
            items:
            [
                new ExpenseItemDto(null, "Mains", 20m, 1, 0, [alice]),
                new ExpenseItemDto(null, "Appetizer", 10m, 1, 1, [bob])
            ]));

        expense.Splits.Sum(s => s.Amount).ShouldBe(36m);
        expense.Splits.Single(s => s.MemberId == alice).Amount.ShouldBe(24m);
    }

    [Fact]
    public async Task An_itemized_expense_needs_items()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Expenses.CreateAsync(userId,
            Create(group.Id, alice, 30m, [new SplitInputDto(alice, null)], SplitType.Itemized)));
    }

    [Fact]
    public async Task An_expense_needs_a_positive_amount()
    {
        var (userId, group, alice, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Expenses.CreateAsync(userId,
            Create(group.Id, alice, 0m, [new SplitInputDto(alice, null)])));
    }

    [Fact]
    public async Task An_expense_needs_a_description()
    {
        var (userId, group, alice, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Expenses.CreateAsync(userId,
            Create(group.Id, alice, 10m, [new SplitInputDto(alice, null)], description: "  ")));
    }

    [Fact]
    public async Task An_expense_needs_at_least_one_participant()
    {
        var (userId, group, alice, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Expenses.CreateAsync(userId,
            Create(group.Id, alice, 10m, [])));
    }

    [Fact]
    public async Task The_payer_must_be_a_member_of_the_group()
    {
        var (userId, group, alice, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Expenses.CreateAsync(userId,
            Create(group.Id, Guid.NewGuid(), 10m, [new SplitInputDto(alice, null)])));
    }

    [Fact]
    public async Task A_participant_from_another_group_is_rejected()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var other = await Groups.CreateAsync(userId,
            new CreateGroupRequest("Other", "CAD", null, null, null, ["Zoe"]));
        var zoe = other.Members.First(m => m.DisplayName == "Zoe").Id;

        await Should.ThrowAsync<ValidationException>(() => Expenses.CreateAsync(userId,
            Create(group.Id, alice, 10m, [new SplitInputDto(alice, null), new SplitInputDto(zoe, null)])));
    }

    [Fact]
    public async Task Creating_an_expense_in_a_group_you_are_not_in_is_forbidden()
    {
        var (_, group, alice, _) = await SetupAsync();
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        await Should.ThrowAsync<ForbiddenException>(() => Expenses.CreateAsync(stranger.Id,
            Create(group.Id, alice, 10m, [new SplitInputDto(alice, null)])));
    }

    [Fact]
    public async Task An_archived_group_accepts_no_new_expenses()
    {
        var (userId, group, alice, _) = await SetupAsync();
        await Groups.ArchiveAsync(userId, group.Id);

        await Should.ThrowAsync<GroupArchivedException>(() => Expenses.CreateAsync(userId,
            Create(group.Id, alice, 10m, [new SplitInputDto(alice, null)])));
    }

    [Fact]
    public async Task A_foreign_currency_expense_is_converted_and_the_rate_is_frozen()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        Currency.ConvertAsync(100m, "EUR", "CAD", Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ConversionResult(148m, 1.48m, Clock.UtcNow)));

        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 100m,
            [new SplitInputDto(alice, null), new SplitInputDto(bob, null)], currency: "EUR"));

        expense.AmountInBaseCurrency.ShouldBe(148m);
        expense.ExchangeRate.ShouldBe(1.48m);
        expense.Splits.Sum(s => s.AmountInBaseCurrency).ShouldBe(148m);
    }

    [Fact]
    public async Task An_expense_in_the_group_currency_needs_no_conversion()
    {
        var (userId, group, alice, _) = await SetupAsync();

        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)]));

        expense.ExchangeRate.ShouldBe(1m);
        await Currency.DidNotReceive().ConvertAsync(
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Creating_an_expense_records_a_first_revision()
    {
        var (userId, group, alice, _) = await SetupAsync();

        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)]));

        var history = await Expenses.GetHistoryAsync(userId, expense.Id);
        history.ShouldHaveSingleItem().Revision.ShouldBe(1);
    }

    [Fact]
    public async Task Creating_an_expense_writes_the_sync_log_and_the_activity_feed()
    {
        var (userId, group, alice, _) = await SetupAsync();

        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)]));

        var fresh = NewContext();
        (await fresh.SyncLog.AnyAsync(e =>
            e.EntityId == expense.Id && e.Operation == SyncOperation.Create)).ShouldBeTrue();
        (await fresh.ActivityLog.AnyAsync(a =>
            a.SubjectId == expense.Id && a.Kind == ActivityKind.ExpenseCreated)).ShouldBeTrue();
    }

    [Fact]
    public async Task Creating_an_expense_notifies_the_other_members()
    {
        var (userId, group, alice, _) = await SetupAsync();

        await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m, [new SplitInputDto(alice, null)]));

        await Push.Received(1).SendToGroupAsync(
            group.Id, Arg.Any<PushMessage>(), userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Creating_an_expense_broadcasts_it_for_live_sync()
    {
        var (userId, group, alice, _) = await SetupAsync();

        await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m, [new SplitInputDto(alice, null)]));

        await Broadcaster.Received().BroadcastAsync(
            group.Id, Arg.Any<SplitEverything.Application.Contracts.Sync.SyncPushResult>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replaying_a_create_with_the_same_client_id_returns_the_first_expense()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var clientId = Guid.NewGuid();
        var request = Create(group.Id, alice, 50m, [new SplitInputDto(alice, null)], clientId: clientId);

        var first = await Expenses.CreateAsync(userId, request);
        var second = await Expenses.CreateAsync(userId, request);

        // An offline client that retries a queued create must not double-charge.
        second.Id.ShouldBe(first.Id);
        (await NewContext().Expenses.CountAsync(e => e.GroupId == group.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Updating_an_expense_bumps_its_revision_and_keeps_the_old_one()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)]));

        var updated = await Expenses.UpdateAsync(userId, expense.Id, new UpdateExpenseRequest(
            null, "Renamed dinner", null, null, null, null, null, null, null, null, null, null));

        updated.Revision.ShouldBe(2);
        updated.Description.ShouldBe("Renamed dinner");
        (await Expenses.GetHistoryAsync(userId, expense.Id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Updating_the_amount_recalculates_the_splits()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 100m,
            [new SplitInputDto(alice, null), new SplitInputDto(bob, null)]));

        var updated = await Expenses.UpdateAsync(userId, expense.Id, new UpdateExpenseRequest(
            null, null, 60m, null, null, null, null, null, null, null, null, null));

        updated.Splits.ShouldAllBe(s => s.Amount == 30m);
    }

    [Fact]
    public async Task Updating_the_participants_replaces_the_splits()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 100m,
            [new SplitInputDto(alice, null), new SplitInputDto(bob, null)]));

        var updated = await Expenses.UpdateAsync(userId, expense.Id, new UpdateExpenseRequest(
            null, null, null, null, null, SplitType.Equal, [new SplitInputDto(alice, null)],
            null, null, null, null, null));

        updated.Splits.ShouldHaveSingleItem().Amount.ShouldBe(100m);
    }

    [Fact]
    public async Task An_edit_based_on_a_stale_clock_is_refused_rather_than_overwriting()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)]));
        var staleClock = expense.VectorClock;

        await Expenses.UpdateAsync(userId, expense.Id, new UpdateExpenseRequest(
            null, "First edit", null, null, null, null, null, null, null, null, null, null));

        // Second device edits from the clock it last saw: concurrent, so it must not win.
        await Should.ThrowAsync<SyncConflictException>(() => Expenses.UpdateAsync(
            userId, expense.Id, new UpdateExpenseRequest(
                null, "Second edit", null, null, null, null, null, null, null, null, null, staleClock)));
    }

    [Fact]
    public async Task An_edit_based_on_the_current_clock_is_accepted()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)]));

        var updated = await Expenses.UpdateAsync(userId, expense.Id, new UpdateExpenseRequest(
            null, "Edited", null, null, null, null, null, null, null, null, null, expense.VectorClock));

        updated.Description.ShouldBe("Edited");
    }

    [Fact]
    public async Task Deleting_an_expense_hides_it_but_keeps_the_tombstone()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)]));

        await Expenses.DeleteAsync(userId, expense.Id);

        await Should.ThrowAsync<NotFoundException>(() => Expenses.GetAsync(userId, expense.Id));
        var row = await NewContext().Expenses.FirstAsync(e => e.Id == expense.Id);
        row.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_deleted_expense_stops_counting_toward_balances()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 100m,
            [new SplitInputDto(alice, null), new SplitInputDto(bob, null)]));

        await Expenses.DeleteAsync(userId, expense.Id);

        (await Groups.GetAsync(userId, group.Id)).MyNetBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task Listing_expenses_filters_by_group_and_orders_newest_first()
    {
        var (userId, group, alice, _) = await SetupAsync();
        await Expenses.CreateAsync(userId, Create(group.Id, alice, 10m,
            [new SplitInputDto(alice, null)], description: "Older"));
        Clock.UtcNow = Clock.UtcNow.AddDays(1);
        await Expenses.CreateAsync(userId, Create(group.Id, alice, 20m,
            [new SplitInputDto(alice, null)], description: "Newer") with { SpentAt = TestData.Jan1.AddDays(5) });

        var page = await Expenses.ListAsync(userId, new ExpenseQuery(GroupId: group.Id));

        page.Total.ShouldBe(2);
        page.Items[0].Description.ShouldBe("Newer");
    }

    [Fact]
    public async Task Listing_can_search_by_description()
    {
        var (userId, group, alice, _) = await SetupAsync();
        await Expenses.CreateAsync(userId, Create(group.Id, alice, 10m,
            [new SplitInputDto(alice, null)], description: "Hydro bill"));
        await Expenses.CreateAsync(userId, Create(group.Id, alice, 20m,
            [new SplitInputDto(alice, null)], description: "Groceries"));

        var page = await Expenses.ListAsync(userId, new ExpenseQuery(GroupId: group.Id, Search: "hydro"));

        page.Items.ShouldHaveSingleItem().Description.ShouldBe("Hydro bill");
    }

    [Fact]
    public async Task Listing_can_filter_by_date_range()
    {
        var (userId, group, alice, _) = await SetupAsync();
        await Expenses.CreateAsync(userId, Create(group.Id, alice, 10m,
            [new SplitInputDto(alice, null)]) with { SpentAt = TestData.Jan1 });
        await Expenses.CreateAsync(userId, Create(group.Id, alice, 20m,
            [new SplitInputDto(alice, null)]) with { SpentAt = TestData.Jan1.AddMonths(6) });

        var page = await Expenses.ListAsync(userId, new ExpenseQuery(
            GroupId: group.Id, From: TestData.Jan1.AddMonths(3)));

        page.Items.ShouldHaveSingleItem().Amount.ShouldBe(20m);
    }

    [Fact]
    public async Task Listing_without_a_group_spans_every_group_the_caller_is_in()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var second = await Groups.CreateAsync(userId, new CreateGroupRequest("Trip", "CAD", null, null, null, null));
        var secondMember = second.Members.Single().Id;
        await Expenses.CreateAsync(userId, Create(group.Id, alice, 10m, [new SplitInputDto(alice, null)]));
        await Expenses.CreateAsync(userId, Create(second.Id, secondMember, 20m, [new SplitInputDto(secondMember, null)]));

        (await Expenses.ListAsync(userId, new ExpenseQuery())).Total.ShouldBe(2);
    }

    [Fact]
    public async Task Listing_never_shows_another_users_groups()
    {
        var (userId, group, alice, _) = await SetupAsync();
        await Expenses.CreateAsync(userId, Create(group.Id, alice, 10m, [new SplitInputDto(alice, null)]));
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        (await Expenses.ListAsync(stranger.Id, new ExpenseQuery())).Total.ShouldBe(0);
    }

    [Fact]
    public async Task Listing_pages_the_results()
    {
        var (userId, group, alice, _) = await SetupAsync();
        for (var i = 0; i < 5; i++)
            await Expenses.CreateAsync(userId, Create(group.Id, alice, 10m + i,
                [new SplitInputDto(alice, null)], description: $"Item {i}"));

        var page = await Expenses.ListAsync(userId, new ExpenseQuery(GroupId: group.Id, Page: 2, PageSize: 2));

        page.Items.Count.ShouldBe(2);
        page.Total.ShouldBe(5);
        page.HasMore.ShouldBeTrue();
    }

    [Fact]
    public async Task A_comment_can_be_posted_and_read_back()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)]));

        await Expenses.AddCommentAsync(userId, new CreateCommentRequest(expense.Id, "Was this the taxi?", null, null));

        var comments = await Expenses.GetCommentsAsync(userId, expense.Id);
        comments.ShouldHaveSingleItem().Body.ShouldBe("Was this the taxi?");
    }

    [Fact]
    public async Task Comments_thread_one_level_deep()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)]));
        var parent = await Expenses.AddCommentAsync(userId,
            new CreateCommentRequest(expense.Id, "Question", null, null));

        await Expenses.AddCommentAsync(userId,
            new CreateCommentRequest(expense.Id, "Answer", parent.Id, null));

        var comments = await Expenses.GetCommentsAsync(userId, expense.Id);
        comments.ShouldHaveSingleItem().Replies.ShouldHaveSingleItem().Body.ShouldBe("Answer");
    }

    [Fact]
    public async Task An_empty_comment_is_rejected()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)]));

        await Should.ThrowAsync<ValidationException>(() => Expenses.AddCommentAsync(
            userId, new CreateCommentRequest(expense.Id, "   ", null, null)));
    }

    [Fact]
    public async Task Only_the_author_can_delete_their_comment()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var other = await TestData.SeedUserAsync(Db, "Bobby");
        Db.GroupMembers.Add(TestData.Member(group.Id, other.Id, "Bobby"));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)]));
        var comment = await Expenses.AddCommentAsync(userId,
            new CreateCommentRequest(expense.Id, "Mine", null, null));

        await Should.ThrowAsync<ForbiddenException>(() => Expenses.DeleteCommentAsync(other.Id, comment.Id));
    }

    [Fact]
    public async Task A_comment_count_is_reported_on_the_expense()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)]));
        await Expenses.AddCommentAsync(userId, new CreateCommentRequest(expense.Id, "One", null, null));

        (await Expenses.GetAsync(userId, expense.Id)).CommentCount.ShouldBe(1);
    }

    [Fact]
    public async Task Reading_an_expense_from_a_group_you_are_not_in_is_forbidden()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)]));
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        await Should.ThrowAsync<ForbiddenException>(() => Expenses.GetAsync(stranger.Id, expense.Id));
    }

    [Fact]
    public async Task An_expense_carries_its_category_key()
    {
        var (userId, group, alice, _) = await SetupAsync();

        var expense = await Expenses.CreateAsync(userId, Create(group.Id, alice, 50m,
            [new SplitInputDto(alice, null)], categoryId: TestData.CategoryId("groceries")));

        expense.CategoryKey.ShouldBe("groceries");
    }
}
