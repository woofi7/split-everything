using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Auth;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

public class AdminServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private const string AdminEmail = "nicolas@example.com";

    private AdminService NewAdmin(IReceiptStorage? storage = null)
        => new(Db, new AdminOptions { Emails = [AdminEmail] },
            storage ?? Substitute.For<IReceiptStorage>(), Logger<AdminService>());

    private async Task<(Guid AdminId, Guid OtherId, GroupDto Theirs)> SetupAsync()
    {
        var admin = await TestData.SeedUserAsync(Db, "Nicolas", AdminEmail);
        var other = await TestData.SeedUserAsync(Db, "Emma", "emma@example.com");

        var theirs = await Groups.CreateAsync(other.Id,
            new CreateGroupRequest("Their flat", "CAD", null, null, null, ["Roommate"]));

        return (admin.Id, other.Id, theirs);
    }

    [Fact]
    public async Task An_administrator_is_whoever_the_server_says()
    {
        var (adminId, otherId, _) = await SetupAsync();
        var admin = NewAdmin();

        (await admin.IsAdminAsync(adminId)).ShouldBeTrue();
        (await admin.IsAdminAsync(otherId)).ShouldBeFalse();
    }

    [Fact]
    public async Task An_install_with_nobody_configured_has_no_administrator()
    {
        var (adminId, _, _) = await SetupAsync();

        var admin = new AdminService(Db, new AdminOptions(),
            Substitute.For<IReceiptStorage>(), Logger<AdminService>());

        (await admin.IsAdminAsync(adminId)).ShouldBeFalse();
        await Should.ThrowAsync<ForbiddenException>(() => admin.GetGroupsAsync(adminId));
    }

    [Fact]
    public async Task The_address_is_matched_whatever_the_case()
    {
        var user = await TestData.SeedUserAsync(Db, "Nicolas", "Nicolas@Example.COM");

        var admin = new AdminService(Db, new AdminOptions { Emails = ["  nicolas@example.com "] },
            Substitute.For<IReceiptStorage>(), Logger<AdminService>());

        (await admin.IsAdminAsync(user.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Every_group_is_listed_including_the_ones_they_are_not_in()
    {
        var (adminId, _, theirs) = await SetupAsync();

        var groups = await NewAdmin().GetGroupsAsync(adminId);

        var listed = groups.ShouldHaveSingleItem();
        listed.Id.ShouldBe(theirs.Id);
        listed.IsMine.ShouldBeFalse();
        listed.CreatedByName.ShouldBe("Emma");
        listed.MemberCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_listed_group_carries_what_is_in_it()
    {
        var (adminId, otherId, theirs) = await SetupAsync();
        var payer = theirs.Members.First(m => m.UserId == otherId).Id;

        await Expenses.CreateAsync(otherId, new CreateExpenseRequest(
            theirs.Id, payer, "Groceries", 60m, "CAD", Clock.UtcNow, SplitType.Equal,
            theirs.Members.Select(m => new SplitInputDto(m.Id, null)).ToList(),
            null, null, null, null, null, null));

        var listed = (await NewAdmin().GetGroupsAsync(adminId)).ShouldHaveSingleItem();

        listed.ExpenseCount.ShouldBe(1);
        listed.TotalSpend.ShouldBe(60m);
    }

    [Fact]
    public async Task Somebody_who_is_not_an_administrator_is_refused_everything()
    {
        var (_, otherId, theirs) = await SetupAsync();
        var admin = NewAdmin();

        await Should.ThrowAsync<ForbiddenException>(() => admin.GetGroupsAsync(otherId));
        await Should.ThrowAsync<ForbiddenException>(() => admin.GetGroupAsync(otherId, theirs.Id));
        await Should.ThrowAsync<ForbiddenException>(() => admin.DeleteGroupAsync(otherId, theirs.Id));
    }

    [Fact]
    public async Task A_group_can_be_read_without_joining_it()
    {
        var (adminId, otherId, theirs) = await SetupAsync();
        var payer = theirs.Members.First(m => m.UserId == otherId).Id;

        await Expenses.CreateAsync(otherId, new CreateExpenseRequest(
            theirs.Id, payer, "Groceries", 60m, "CAD", Clock.UtcNow, SplitType.Equal,
            theirs.Members.Select(m => new SplitInputDto(m.Id, null)).ToList(),
            null, null, null, null, null, null));

        var detail = await NewAdmin().GetGroupAsync(adminId, theirs.Id);

        detail.Members.Select(m => m.DisplayName).ShouldBe(["Emma", "Roommate"], ignoreOrder: true);
        detail.RecentExpenses.ShouldHaveSingleItem().Description.ShouldBe("Groceries");

        var members = await Db.GroupMembers.CountAsync(m => m.GroupId == theirs.Id);
        members.ShouldBe(2);
    }

    [Fact]
    public async Task A_group_still_in_use_cannot_be_deleted()
    {
        var (adminId, _, theirs) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(
            () => NewAdmin().DeleteGroupAsync(adminId, theirs.Id));

        (await Db.Groups.CountAsync(g => g.Id == theirs.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task An_archived_group_goes_with_everything_in_it()
    {
        var (adminId, otherId, theirs) = await SetupAsync();
        var payer = theirs.Members.First(m => m.UserId == otherId).Id;

        var expense = await Expenses.CreateAsync(otherId, new CreateExpenseRequest(
            theirs.Id, payer, "Groceries", 60m, "CAD", Clock.UtcNow, SplitType.Equal,
            theirs.Members.Select(m => new SplitInputDto(m.Id, null)).ToList(),
            null, null, null, null, null, null));

        await Groups.ArchiveAsync(otherId, theirs.Id);

        await NewAdmin().DeleteGroupAsync(adminId, theirs.Id);

        (await Db.Groups.AnyAsync(g => g.Id == theirs.Id)).ShouldBeFalse();
        (await Db.GroupMembers.AnyAsync(m => m.GroupId == theirs.Id)).ShouldBeFalse();
        (await Db.Expenses.AnyAsync(e => e.GroupId == theirs.Id)).ShouldBeFalse();
        (await Db.ExpenseSplits.AnyAsync(s => s.ExpenseId == expense.Id)).ShouldBeFalse();
        (await Db.SyncLog.AnyAsync(l => l.GroupId == theirs.Id)).ShouldBeFalse();
        (await Db.ActivityLog.AnyAsync(a => a.GroupId == theirs.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_one_group_leaves_the_others_alone()
    {
        var (adminId, otherId, theirs) = await SetupAsync();
        var keep = await Groups.CreateAsync(otherId,
            new CreateGroupRequest("Still going", "CAD", null, null, null, ["Roommate"]));

        await Groups.ArchiveAsync(otherId, theirs.Id);
        await NewAdmin().DeleteGroupAsync(adminId, theirs.Id);

        (await Db.Groups.AnyAsync(g => g.Id == keep.Id)).ShouldBeTrue();
        (await Db.GroupMembers.CountAsync(m => m.GroupId == keep.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task A_group_that_is_not_there_is_a_404_rather_than_a_500()
    {
        var (adminId, _, _) = await SetupAsync();

        await Should.ThrowAsync<NotFoundException>(
            () => NewAdmin().GetGroupAsync(adminId, Guid.NewGuid()));
        await Should.ThrowAsync<NotFoundException>(
            () => NewAdmin().DeleteGroupAsync(adminId, Guid.NewGuid()));
    }
}
