using System.Text;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Common;
using SplitEverything.Domain.Common;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Import;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Application;

/// <summary>
/// Importing a Settle Up export into a group that does not exist yet.
///
/// That is the ordinary case: an export is one group's history, and the reason to
/// import it is that the group is not here. Requiring one to be created first made
/// the wizard start with unrelated work, and a failed import would leave an empty
/// group behind.
/// </summary>
public class CsvImportNewGroupTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private ImportService Imports { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Imports = new ImportService(Db, Writer, Activity, Currency, Clock, Groups);
    }

    private static Stream Csv(string fileName)
        => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static CsvColumnMapping BasicMapping() => new(
        DateColumn: 0, DescriptionColumn: 1, AmountColumn: 4,
        CurrencyColumn: 3, PaidByColumn: 5,
        ParticipantColumns: null, DateFormat: null, DecimalSeparator: null);

    private static CsvCommitRequest CommitInto(
        Guid? groupId, string? newGroupName, IReadOnlyDictionary<string, Guid?>? names = null)
        => new(Guid.NewGuid(), groupId, newGroupName, BasicMapping(),
            names ?? new Dictionary<string, Guid?>(), [], true, true, "CAD", "export.csv");

    [Fact]
    public async Task Previewing_without_a_group_reports_every_name_as_unmapped()
    {
        var user = await TestData.SeedUserAsync(Db);

        var preview = await Imports.PreviewCsvAsync(user.Id, Csv("settleup-basic.csv"),
            new CsvPreviewRequest(null, BasicMapping(), new Dictionary<string, Guid?>(), "CAD"));

        // Nothing exists to map onto yet, so the wizard offers to create them all.
        preview.UnmappedMemberNames.ShouldBe(["Alice", "Bob", "Carol"], ignoreOrder: true);
        preview.Rows.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Previewing_without_a_group_flags_nothing_as_a_duplicate()
    {
        var user = await TestData.SeedUserAsync(Db);
        var existing = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Alice", "Bob"]));

        await Imports.CommitCsvAsync(user.Id, Csv("settleup-basic.csv"),
            CommitInto(existing.Id, null));

        var preview = await Imports.PreviewCsvAsync(user.Id, Csv("settleup-basic.csv"),
            new CsvPreviewRequest(null, BasicMapping(), new Dictionary<string, Guid?>(), "CAD"));

        // A new group shares no history, so the same rows are not duplicates in it.
        preview.DuplicateCount.ShouldBe(0);
    }

    [Fact]
    public async Task Previewing_still_shows_the_purpose_of_each_row()
    {
        var user = await TestData.SeedUserAsync(Db);

        var preview = await Imports.PreviewCsvAsync(user.Id, Csv("settleup-basic.csv"),
            new CsvPreviewRequest(null, BasicMapping(), new Dictionary<string, Guid?>(), "CAD"));

        preview.Rows.Select(r => r.Description)
            .ShouldBe(["Groceries at Metro", "Hydro bill", "Dinner out", "Uber home"]);
    }

    [Fact]
    public async Task Committing_creates_the_group_and_imports_into_it()
    {
        var user = await TestData.SeedUserAsync(Db);

        var result = await Imports.CommitCsvAsync(user.Id, Csv("settleup-basic.csv"),
            CommitInto(null, "Old flat"));

        result.CreatedExpenses.ShouldBe(4);

        var group = await NewContext().Groups.SingleAsync(g => g.Name == "Old flat");
        result.GroupId.ShouldBe(group.Id);

        var expenses = await NewContext().Expenses.Where(e => e.GroupId == group.Id).ToListAsync();
        expenses.Count.ShouldBe(4);
    }

    [Fact]
    public async Task The_new_group_has_the_person_importing_as_its_owner()
    {
        var user = await TestData.SeedUserAsync(Db);

        var result = await Imports.CommitCsvAsync(user.Id, Csv("settleup-basic.csv"),
            CommitInto(null, "Old flat"));

        var owner = await NewContext().GroupMembers
            .SingleAsync(m => m.GroupId == result.GroupId && m.Role == GroupRole.Owner);

        owner.UserId.ShouldBe(user.Id);
    }

    [Fact]
    public async Task The_new_group_gets_a_member_for_every_name_in_the_export()
    {
        var user = await TestData.SeedUserAsync(Db);

        var result = await Imports.CommitCsvAsync(user.Id, Csv("settleup-basic.csv"),
            CommitInto(null, "Old flat"));

        var names = await NewContext().GroupMembers
            .Where(m => m.GroupId == result.GroupId)
            .Select(m => m.DisplayName)
            .ToListAsync();

        // The owner's own row plus one per exported name.
        names.ShouldContain("Alice");
        names.ShouldContain("Bob");
        names.ShouldContain("Carol");
    }

    [Fact]
    public async Task A_name_mapped_to_the_person_importing_does_not_become_a_second_member()
    {
        var user = await TestData.SeedUserAsync(Db, "Alice");

        var result = await Imports.CommitCsvAsync(user.Id, Csv("settleup-basic.csv"),
            CommitInto(null, "Old flat"));

        var aliceRows = await NewContext().GroupMembers
            .Where(m => m.GroupId == result.GroupId && m.DisplayName == "Alice")
            .ToListAsync();

        // The importer is already in the group under that name; a placeholder beside
        // them would split their history in two.
        aliceRows.Count.ShouldBe(1);
        aliceRows[0].UserId.ShouldBe(user.Id);
    }

    [Fact]
    public async Task Refuses_a_new_group_with_no_name()
    {
        var user = await TestData.SeedUserAsync(Db);

        await Should.ThrowAsync<ValidationException>(
            () => Imports.CommitCsvAsync(user.Id, Csv("settleup-basic.csv"), CommitInto(null, "   ")));
    }

    [Fact]
    public async Task Creates_no_group_when_the_export_cannot_be_read()
    {
        var user = await TestData.SeedUserAsync(Db);
        var before = await NewContext().Groups.CountAsync();

        await Should.ThrowAsync<ValidationException>(() => Imports.CommitCsvAsync(
            user.Id,
            new MemoryStream(Encoding.UTF8.GetBytes("not,a,settleup,export\n")),
            CommitInto(null, "Old flat")));

        // An empty group left behind would be worse than the failure itself.
        (await NewContext().Groups.CountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task Still_imports_into_an_existing_group_when_one_is_named()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Alice", "Bob", "Carol"]));

        var result = await Imports.CommitCsvAsync(user.Id, Csv("settleup-basic.csv"),
            CommitInto(group.Id, null));

        result.GroupId.ShouldBe(group.Id);
        (await NewContext().Groups.CountAsync(g => g.Name == "Roommates")).ShouldBe(1);
    }

    /// <summary>
    /// Binding an exported name to an account rather than to a member.
    ///
    /// An export is another group's history. The people in it usually have
    /// accounts here already and only their names came across, so the useful
    /// answer to "who is Alice" is an account, which may not be in this group and
    /// in the ordinary case is being imported into a group that does not exist yet.
    /// </summary>
    [Fact]
    public async Task An_exported_name_can_be_bound_to_an_account()
    {
        var user = await TestData.SeedUserAsync(Db, "Owner", "owner@example.com", "google-owner");
        var alice = await TestData.SeedUserAsync(Db, "Alice", "alice@example.com", "google-alice");

        var result = await Imports.CommitCsvAsync(user.Id, Csv("settleup-basic.csv"),
            CommitInto(null, "Imported") with
            {
                MemberUserMapping = new Dictionary<string, Guid> { ["Alice"] = alice.Id },
            });

        var group = await Groups.GetAsync(user.Id, result.GroupId);
        var bound = group.Members.FirstOrDefault(m => m.UserId == alice.Id);

        bound.ShouldNotBeNull();
        // Their own name and their own colour, not a placeholder wearing the name
        // from the file.
        bound.DisplayName.ShouldBe("Alice");
        bound.ColorHex.ShouldNotBeNull();
        bound.IsPlaceholder.ShouldBeFalse();
    }

    [Fact]
    public async Task A_bound_account_owns_the_rows_that_named_it()
    {
        var user = await TestData.SeedUserAsync(Db, "Owner", "owner@example.com", "google-owner");
        var alice = await TestData.SeedUserAsync(Db, "Alice", "alice@example.com", "google-alice");

        var result = await Imports.CommitCsvAsync(user.Id, Csv("settleup-basic.csv"),
            CommitInto(null, "Imported") with
            {
                MemberUserMapping = new Dictionary<string, Guid> { ["Alice"] = alice.Id },
            });

        var fresh = NewContext();
        var member = await fresh.GroupMembers
            .FirstAsync(m => m.GroupId == result.GroupId && m.UserId == alice.Id);

        // The whole point: the history lands on the person, not beside them.
        (await fresh.Expenses.CountAsync(e => e.PaidByMemberId == member.Id))
            .ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_bound_account_is_not_duplicated_when_it_is_already_a_member()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner", "owner@example.com", "google-owner");
        var alice = await TestData.SeedUserAsync(Db, "Alice", "alice@example.com", "google-alice");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        await Groups.AddUserMemberAsync(owner.Id, group.Id, new AddUserMemberRequest(alice.Id));

        await Imports.CommitCsvAsync(owner.Id, Csv("settleup-basic.csv"),
            CommitInto(group.Id, null) with
            {
                MemberUserMapping = new Dictionary<string, Guid> { ["Alice"] = alice.Id },
            });

        // A second row for one account would collide with the one-membership-per-user
        // index and orphan whatever history points at the first.
        var fresh = NewContext();
        (await fresh.GroupMembers.CountAsync(m => m.GroupId == group.Id && m.UserId == alice.Id))
            .ShouldBe(1);
    }

    [Fact]
    public async Task A_bound_account_that_was_removed_comes_back_rather_than_doubling()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner", "owner@example.com", "google-owner");
        var alice = await TestData.SeedUserAsync(Db, "Alice", "alice@example.com", "google-alice");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var added = await Groups.AddUserMemberAsync(owner.Id, group.Id, new AddUserMemberRequest(alice.Id));
        await Groups.RemoveMemberAsync(owner.Id, group.Id, added.Id);

        await Imports.CommitCsvAsync(owner.Id, Csv("settleup-basic.csv"),
            CommitInto(group.Id, null) with
            {
                MemberUserMapping = new Dictionary<string, Guid> { ["Alice"] = alice.Id },
            });

        var fresh = NewContext();
        var members = await fresh.GroupMembers
            .Where(m => m.GroupId == group.Id && m.UserId == alice.Id)
            .ToListAsync();

        members.Count.ShouldBe(1);
        members[0].Status.ShouldBe(MembershipStatus.Active);
    }

    [Fact]
    public async Task A_previewed_name_bound_to_an_account_is_no_longer_unmapped()
    {
        var user = await TestData.SeedUserAsync(Db, "Owner", "owner@example.com", "google-owner");
        var alice = await TestData.SeedUserAsync(Db, "Alice", "alice@example.com", "google-alice");

        var preview = await Imports.PreviewCsvAsync(user.Id, Csv("settleup-basic.csv"),
            new CsvPreviewRequest(null, BasicMapping(), new Dictionary<string, Guid?>(), "CAD",
                new Dictionary<string, Guid> { ["Alice"] = alice.Id }));

        // Saying somebody is not a member yet, when the import is about to make
        // them one, is a warning about nothing.
        preview.UnmappedMemberNames.ShouldNotContain("Alice");
        preview.Rows.ShouldNotContain(r => r.Problems.Any(p => p.Contains("Alice is not a member")));
    }

    [Fact]
    public async Task Binding_a_name_to_an_account_that_does_not_exist_is_refused()
    {
        var user = await TestData.SeedUserAsync(Db, "Owner", "owner@example.com", "google-owner");

        await Should.ThrowAsync<NotFoundException>(() => Imports.CommitCsvAsync(
            user.Id, Csv("settleup-basic.csv"),
            CommitInto(null, "Imported") with
            {
                MemberUserMapping = new Dictionary<string, Guid> { ["Alice"] = Guid.CreateVersion7() },
            }));
    }
}
