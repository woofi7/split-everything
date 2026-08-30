using System.Text;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Import;
using SplitEverything.Domain.Algorithms;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

public class ImportServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private ImportService Imports { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Imports = new ImportService(Db, Writer, Activity, Currency, Clock, Groups);
    }

    private static Stream Csv(string fileName)
        => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static Stream CsvText(string content)
        => new MemoryStream(Encoding.UTF8.GetBytes(content));

    private async Task<(Guid UserId, GroupDto Group)> SetupAsync(params string[] placeholders)
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, placeholders));
        return (user.Id, group);
    }

    private static CsvColumnMapping BasicMapping() => new(
        DateColumn: 0, DescriptionColumn: 1, AmountColumn: 4,
        CurrencyColumn: 3, PaidByColumn: 5,
        ParticipantColumns: null, DateFormat: null, DecimalSeparator: null);

    // ---- analysis --------------------------------------------------------

    [Fact]
    public async Task Analysis_reads_the_header_row()
    {
        var (userId, _) = await SetupAsync();

        var analysis = await Imports.AnalyzeCsvAsync(userId, Csv("settleup-basic.csv"), "export.csv");

        analysis.Headers.ShouldBe(new[]
        {
            "Date", "Purpose", "Category", "Currency", "Amount", "Who paid", "For whom"
        });
        analysis.RowCount.ShouldBe(4);
    }

    [Fact]
    public async Task Analysis_guesses_the_columns_it_recognises()
    {
        var (userId, _) = await SetupAsync();

        var analysis = await Imports.AnalyzeCsvAsync(userId, Csv("settleup-basic.csv"), "export.csv");

        analysis.SuggestedMapping["date"].ShouldBe(0);
        analysis.SuggestedMapping["description"].ShouldBe(1);
        analysis.SuggestedMapping["amount"].ShouldBe(4);
        analysis.SuggestedMapping["paidBy"].ShouldBe(5);
    }

    [Fact]
    public async Task Analysis_collects_the_member_names_the_export_mentions()
    {
        var (userId, _) = await SetupAsync();

        var analysis = await Imports.AnalyzeCsvAsync(userId, Csv("settleup-basic.csv"), "export.csv");

        analysis.DetectedMemberNames.ShouldBe(new[] { "Alice", "Bob", "Carol" }, ignoreOrder: true);
    }

    [Fact]
    public async Task Analysis_returns_sample_rows_for_the_preview_table()
    {
        var (userId, _) = await SetupAsync();

        var analysis = await Imports.AnalyzeCsvAsync(userId, Csv("settleup-basic.csv"), "export.csv");

        analysis.SampleRows.ShouldNotBeEmpty();
        analysis.SampleRows[0][1].ShouldBe("Groceries at Metro");
    }

    [Fact]
    public async Task Analysis_detects_a_semicolon_delimited_export()
    {
        var (userId, _) = await SetupAsync();

        var analysis = await Imports.AnalyzeCsvAsync(userId, Csv("settleup-semicolon-eu.csv"), "export.csv");

        analysis.DetectedDelimiter.ShouldBe(";");
        analysis.Headers.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task Analysis_detects_the_currency_the_export_uses()
    {
        var (userId, _) = await SetupAsync();

        (await Imports.AnalyzeCsvAsync(userId, Csv("settleup-basic.csv"), "export.csv"))
            .DetectedCurrency.ShouldBe("CAD");
    }

    [Fact]
    public async Task An_empty_file_is_rejected()
    {
        var (userId, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(
            () => Imports.AnalyzeCsvAsync(userId, CsvText(""), "empty.csv"));
    }

    [Fact]
    public async Task A_file_with_only_a_header_is_rejected()
    {
        var (userId, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(
            () => Imports.AnalyzeCsvAsync(userId, CsvText("Date,Purpose,Amount"), "header.csv"));
    }

    // ---- preview ---------------------------------------------------------

    [Fact]
    public async Task Preview_parses_every_row_with_the_confirmed_mapping()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");
        var members = NameMap(group);

        var preview = await Imports.PreviewCsvAsync(userId, Csv("settleup-basic.csv"),
            new CsvPreviewRequest(group.Id, BasicMapping(), members, "CAD"));

        preview.Rows.Count.ShouldBe(4);
        preview.CommittableCount.ShouldBe(4);
        preview.ProblemCount.ShouldBe(0);
    }

    [Fact]
    public async Task Preview_reads_the_amount_and_date_of_each_row()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");

        var preview = await Imports.PreviewCsvAsync(userId, Csv("settleup-basic.csv"),
            new CsvPreviewRequest(group.Id, BasicMapping(), NameMap(group), "CAD"));

        var first = preview.Rows[0];
        first.Amount.ShouldBe(84.32m);
        first.SpentAt!.Value.UtcDateTime.Date.ShouldBe(new DateTime(2026, 1, 5));
        first.Description.ShouldBe("Groceries at Metro");
    }

    [Fact]
    public async Task Preview_resolves_the_payer_and_participants_to_members()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");
        var members = NameMap(group);

        var preview = await Imports.PreviewCsvAsync(userId, Csv("settleup-basic.csv"),
            new CsvPreviewRequest(group.Id, BasicMapping(), members, "CAD"));

        var dinner = preview.Rows.First(r => r.Description == "Dinner out");
        dinner.PaidByMemberId.ShouldBe(members["Alice"]);
        dinner.ParticipantMemberIds.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Preview_flags_the_rows_it_cannot_parse_without_failing_the_file()
    {
        var (userId, group) = await SetupAsync("Bob");
        var members = NameMap(group);

        var preview = await Imports.PreviewCsvAsync(userId, Csv("settleup-messy.csv"),
            new CsvPreviewRequest(group.Id, BasicMapping(), members, "CAD"));

        preview.Rows.Count.ShouldBe(5);
        preview.CommittableCount.ShouldBe(1);
        preview.ProblemCount.ShouldBe(4);
        preview.Rows.First(r => r.Description == "Bad date").Problems.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Preview_reports_names_that_have_no_member_yet()
    {
        var (userId, group) = await SetupAsync("Bob");
        var members = NameMap(group);

        var preview = await Imports.PreviewCsvAsync(userId, Csv("settleup-basic.csv"),
            new CsvPreviewRequest(group.Id, BasicMapping(), members, "CAD"));

        preview.UnmappedMemberNames.ShouldContain("Carol");
    }

    [Fact]
    public async Task Preview_flags_a_row_that_duplicates_an_existing_expense()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");
        var members = NameMap(group);
        await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, members["Alice"]!.Value, "Groceries at Metro", 84.32m, "CAD",
            new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero), SplitType.Equal,
            [new SplitInputDto(members["Alice"]!.Value, null)], null, null, null, null,
            ExpenseFingerprint.Compute(
                new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero), 84.32m, "CAD", "Groceries at Metro"),
            null));

        var preview = await Imports.PreviewCsvAsync(userId, Csv("settleup-basic.csv"),
            new CsvPreviewRequest(group.Id, BasicMapping(), members, "CAD"));

        preview.DuplicateCount.ShouldBe(1);
        preview.Rows.First(r => r.Description == "Groceries at Metro").IsDuplicate.ShouldBeTrue();
    }

    [Fact]
    public async Task Preview_handles_a_european_number_and_date_format()
    {
        var (userId, group) = await SetupAsync("Bob");
        var members = NameMap(group);
        var mapping = new CsvColumnMapping(0, 1, 4, 3, 5, null, "dd.MM.yyyy", ",");

        var preview = await Imports.PreviewCsvAsync(userId, Csv("settleup-semicolon-eu.csv"),
            new CsvPreviewRequest(group.Id, mapping, members, "EUR"));

        preview.Rows[0].Amount.ShouldBe(84.32m);
        preview.Rows[0].SpentAt!.Value.UtcDateTime.Date.ShouldBe(new DateTime(2026, 1, 5));
    }

    [Fact]
    public async Task Preview_of_a_group_you_are_not_in_is_forbidden()
    {
        var (_, group) = await SetupAsync("Bob");
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        await Should.ThrowAsync<ForbiddenException>(() => Imports.PreviewCsvAsync(
            stranger.Id, Csv("settleup-basic.csv"),
            new CsvPreviewRequest(group.Id, BasicMapping(), new Dictionary<string, Guid?>(), "CAD")));
    }

    // ---- commit ----------------------------------------------------------

    [Fact]
    public async Task Committing_creates_the_expenses_with_their_original_dates()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");

        var result = await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), NameMap(group),
            [], false, true, "CAD", "export.csv"));

        result.CreatedExpenses.ShouldBe(4);
        var stored = await NewContext().Expenses
            .Where(e => e.GroupId == group.Id)
            .OrderBy(e => e.SpentAt)
            .ToListAsync();
        stored[0].SpentAt.UtcDateTime.Date.ShouldBe(new DateTime(2026, 1, 5));
        stored[0].Amount.ShouldBe(84.32m);
    }

    [Fact]
    public async Task Committing_splits_each_expense_between_the_participants_named()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");
        var members = NameMap(group);

        await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), members, [], false, true, "CAD", null));

        var dinner = await NewContext().Expenses
            .Include(e => e.Splits)
            .FirstAsync(e => e.Description == "Dinner out");
        dinner.Splits.Count.ShouldBe(3);
        dinner.Splits.Sum(s => s.Amount).ShouldBe(64.50m);
    }

    [Fact]
    public async Task Committing_leaves_the_group_balanced_the_way_the_export_described()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");

        await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), NameMap(group), [], false, true, "CAD", null));

        var balance = await Settlements.GetGroupBalanceAsync(userId, group.Id);
        balance.Balances.Sum(b => b.Net).ShouldBe(0m);
    }

    [Fact]
    public async Task Committing_can_create_the_members_the_export_mentions()
    {
        var (userId, group) = await SetupAsync("Bob");

        var result = await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), NameMap(group),
            [], CreateMissingMembers: true, true, "CAD", null));

        result.CreatedMemberIds.ShouldNotBeEmpty();
        (await NewContext().GroupMembers.AnyAsync(m =>
            m.GroupId == group.Id && m.DisplayName == "Carol")).ShouldBeTrue();
    }

    [Fact]
    public async Task Committing_without_creating_members_skips_the_rows_it_cannot_resolve()
    {
        var (userId, group) = await SetupAsync("Bob");

        var result = await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), NameMap(group),
            [], CreateMissingMembers: false, true, "CAD", null));

        result.CreatedExpenses.ShouldBe(2);
        result.SkippedRows.ShouldBe(2);
        result.Warnings.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Committing_skips_the_rows_the_user_unticked()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");

        var result = await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), NameMap(group),
            SkipRowNumbers: [1, 2], false, true, "CAD", null));

        result.CreatedExpenses.ShouldBe(2);
    }

    [Fact]
    public async Task Committing_the_same_export_twice_skips_the_duplicates()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");
        var request = new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), NameMap(group), [], false, true, "CAD", null);

        await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), request);
        var second = await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), request);

        second.CreatedExpenses.ShouldBe(0);
        (await NewContext().Expenses.CountAsync(e => e.GroupId == group.Id)).ShouldBe(4);
    }

    [Fact]
    public async Task Committing_can_be_forced_through_the_duplicate_check()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");
        var members = NameMap(group);
        await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), members, [], false, true, "CAD", null));

        var second = await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), members, [], false,
            SkipDuplicates: false, "CAD", null));

        second.CreatedExpenses.ShouldBe(4);
    }

    [Fact]
    public async Task Committing_records_the_batch_so_it_can_be_undone()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");

        var result = await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), NameMap(group), [], false, true, "CAD", "export.csv"));

        var batch = await NewContext().ImportBatches.SingleAsync(b => b.Id == result.ImportBatchId);
        batch.Source.ShouldBe("settleup-csv");
        batch.SourceLabel.ShouldBe("export.csv");
        batch.ExpenseCount.ShouldBe(4);
    }

    [Fact]
    public async Task Rolling_back_a_batch_removes_everything_it_created()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");
        var result = await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), NameMap(group), [], false, true, "CAD", null));

        await Imports.RollbackBatchAsync(userId, result.ImportBatchId);

        (await NewContext().Expenses.CountAsync(e =>
            e.GroupId == group.Id && !e.IsDeleted)).ShouldBe(0);
        (await Settlements.GetGroupBalanceAsync(userId, group.Id))
            .Balances.ShouldAllBe(b => b.Net == 0m);
    }

    [Fact]
    public async Task Rolling_back_someone_elses_batch_is_forbidden()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");
        var result = await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), NameMap(group), [], false, true, "CAD", null));
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        await Should.ThrowAsync<ForbiddenException>(
            () => Imports.RollbackBatchAsync(stranger.Id, result.ImportBatchId));
    }

    [Fact]
    public async Task Committing_records_the_import_in_the_activity_feed()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");

        await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), NameMap(group), [], false, true, "CAD", null));

        (await NewContext().ActivityLog.AnyAsync(a => a.Kind == ActivityKind.ImportCommitted)).ShouldBeTrue();
    }

    [Fact]
    public async Task Imported_expenses_are_recorded_in_the_sync_log()
    {
        var (userId, group) = await SetupAsync("Bob", "Carol");

        await Imports.CommitCsvAsync(userId, Csv("settleup-basic.csv"), new CsvCommitRequest(
            Guid.NewGuid(), group.Id, null, BasicMapping(), NameMap(group), [], false, true, "CAD", null));

        (await NewContext().SyncLog.CountAsync(e =>
            e.GroupId == group.Id && e.EntityType == SyncEntityType.Expense)).ShouldBe(4);
    }

    // ---- statement commit (client-side parsing) --------------------------

    [Fact]
    public async Task A_statement_commit_records_only_the_file_name_never_the_file()
    {
        var (userId, group) = await SetupAsync("Bob");
        var members = NameMap(group);

        var result = await Imports.CommitStatementAsync(userId, new StatementCommitRequest([
            new ConfirmedStatementRow(group.Id, members["Alice"]!.Value, "METRO", 20m, "CAD",
                TestData.Jan1, SplitType.Equal,
                [new SplitInputDto(members["Alice"]!.Value, null)], "fingerprint-1", null)
        ], true, "visa-january.pdf"));

        var batch = await NewContext().ImportBatches.SingleAsync(b => b.Id == result.ImportBatchId);
        batch.Source.ShouldBe("statement");
        batch.SourceLabel.ShouldBe("visa-january.pdf");
    }

    [Fact]
    public async Task A_statement_commit_can_span_several_groups()
    {
        var (userId, first) = await SetupAsync("Bob");
        var second = await Groups.CreateAsync(userId,
            new CreateGroupRequest("Trip", "CAD", null, null, null, null));
        var firstMember = first.Members.First(m => m.UserId == userId).Id;
        var secondMember = second.Members.Single().Id;

        var result = await Imports.CommitStatementAsync(userId, new StatementCommitRequest([
            new ConfirmedStatementRow(first.Id, firstMember, "METRO", 20m, "CAD", TestData.Jan1, SplitType.Equal, [new SplitInputDto(firstMember, null)], "fp-1", null),
            new ConfirmedStatementRow(second.Id, secondMember, "AIR CANADA", 400m, "CAD", TestData.Jan1, SplitType.Equal, [new SplitInputDto(secondMember, null)], "fp-2", null)
        ], true, null));

        result.CreatedExpenses.ShouldBe(2);
        (await NewContext().Expenses.CountAsync(e => e.GroupId == second.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_statement_row_that_duplicates_an_existing_expense_is_skipped()
    {
        var (userId, group) = await SetupAsync("Bob");
        var members = NameMap(group);
        var row = new ConfirmedStatementRow(group.Id, members["Alice"]!.Value, "METRO", 20m, "CAD",
            TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(members["Alice"]!.Value, null)], "fp-dup", null);

        await Imports.CommitStatementAsync(userId, new StatementCommitRequest([row], true, null));
        var second = await Imports.CommitStatementAsync(userId, new StatementCommitRequest([row], true, null));

        second.CreatedExpenses.ShouldBe(0);
        second.SkippedRows.ShouldBe(1);
    }

    [Fact]
    public async Task A_statement_row_for_a_group_you_are_not_in_is_refused()
    {
        var (_, group) = await SetupAsync("Bob");
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");
        var member = group.Members.First().Id;

        await Should.ThrowAsync<ForbiddenException>(() => Imports.CommitStatementAsync(
            stranger.Id, new StatementCommitRequest([
                new ConfirmedStatementRow(group.Id, member, "METRO", 20m, "CAD", TestData.Jan1, SplitType.Equal, [new SplitInputDto(member, null)], "fp", null)
            ], true, null)));
    }

    [Fact]
    public async Task An_empty_statement_commit_is_rejected()
    {
        var (userId, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Imports.CommitStatementAsync(
            userId, new StatementCommitRequest([], true, null)));
    }

    [Fact]
    public async Task A_foreign_currency_statement_row_is_converted_into_the_group_currency()
    {
        var (userId, group) = await SetupAsync("Bob");
        var members = NameMap(group);
        Currency.ConvertAsync(50m, "USD", "CAD", Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SplitEverything.Application.Abstractions.ConversionResult(68m, 1.36m, Clock.UtcNow)));

        await Imports.CommitStatementAsync(userId, new StatementCommitRequest([
            new ConfirmedStatementRow(group.Id, members["Alice"]!.Value, "AMAZON US", 50m, "USD",
                TestData.Jan1, SplitType.Equal,
                [new SplitInputDto(members["Alice"]!.Value, null)], "fp-usd", null)
        ], true, null));

        (await NewContext().Expenses.SingleAsync()).AmountInBaseCurrency.ShouldBe(68m);
    }

    // ---- duplicate check -------------------------------------------------

    [Fact]
    public async Task The_duplicate_check_reports_the_expense_a_fingerprint_matches()
    {
        var (userId, group) = await SetupAsync("Bob");
        var members = NameMap(group);
        await Imports.CommitStatementAsync(userId, new StatementCommitRequest([
            new ConfirmedStatementRow(group.Id, members["Alice"]!.Value, "METRO", 20m, "CAD",
                TestData.Jan1, SplitType.Equal,
                [new SplitInputDto(members["Alice"]!.Value, null)], "fp-known", null)
        ], true, null));

        var result = await Imports.CheckDuplicatesAsync(userId,
            new DuplicateCheckRequest(["fp-known", "fp-unknown"], null));

        result.Matches.ShouldHaveSingleItem().Fingerprint.ShouldBe("fp-known");
        result.Matches[0].GroupName.ShouldBe("Roommates");
    }

    [Fact]
    public async Task A_merchant_split_before_is_suggested_again()
    {
        var (userId, group) = await SetupAsync("Bob");
        var members = NameMap(group);
        await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, members["Alice"]!.Value, "UBER EATS 4429 MONTREAL", 40m, "CAD",
            TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(members["Alice"]!.Value, null), new SplitInputDto(members["Bob"]!.Value, null)], null, null, null, null, null, null));

        var result = await Imports.GetSplitSuggestionsAsync(userId,
            new SplitSuggestionRequest(["UBER EATS 8829 TORONTO ON"]));

        var suggestion = result.Suggestions.ShouldHaveSingleItem();
        suggestion.GroupId.ShouldBe(group.Id);
        suggestion.Splits.Count.ShouldBe(2);
        suggestion.SplitType.ShouldBe(SplitType.Equal);
    }

    [Fact]
    public async Task A_merchant_never_split_before_gets_no_suggestion()
    {
        var (userId, _) = await SetupAsync("Bob");

        (await Imports.GetSplitSuggestionsAsync(userId,
            new SplitSuggestionRequest(["SOMEWHERE NEW"]))).Suggestions.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_suggestion_reports_how_often_that_merchant_was_split_that_way()
    {
        var (userId, group) = await SetupAsync("Bob");
        var members = NameMap(group);
        for (var i = 0; i < 3; i++)
        {
            await Expenses.CreateAsync(userId, new CreateExpenseRequest(
                group.Id, members["Alice"]!.Value, $"METRO PLUS {i}", 20m + i, "CAD",
                TestData.Jan1.AddDays(i), SplitType.Equal,
                [new SplitInputDto(members["Alice"]!.Value, null), new SplitInputDto(members["Bob"]!.Value, null)], null, null, null, null, null, null));
        }

        var result = await Imports.GetSplitSuggestionsAsync(userId,
            new SplitSuggestionRequest(["METRO PLUS MARCHE"]));

        result.Suggestions.ShouldHaveSingleItem().TimesUsed.ShouldBe(3);
    }

    [Fact]
    public async Task Suggestions_never_reach_into_another_users_history()
    {
        var (userId, group) = await SetupAsync("Bob");
        var members = NameMap(group);
        await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, members["Alice"]!.Value, "UBER EATS", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(members["Alice"]!.Value, null)], null, null, null, null, null, null));
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        (await Imports.GetSplitSuggestionsAsync(stranger.Id,
            new SplitSuggestionRequest(["UBER EATS"]))).Suggestions.ShouldBeEmpty();
    }

    private static Dictionary<string, Guid?> NameMap(GroupDto group)
        => group.Members.ToDictionary(m => m.DisplayName, m => (Guid?)m.Id);
}
