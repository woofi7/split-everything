using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Contracts.Import;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Import;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Application;

public class WorldTourExportTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private ImportService Imports { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Imports = new ImportService(Db, Writer, Activity, Currency, Clock, Groups);
    }

    private static Stream Export()
        => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settleup-world-tour.csv"));

    [Fact]
    public void The_reader_decodes_utf16()
    {
        var table = SettleUpCsvReader.Read(Export());

        table.Headers.ShouldBe([
            "Who paid", "Amount", "Currency", "For whom", "Split amounts", "Purpose",
            "Category", "Date & time", "Timezone", "Exchange rate", "Converted amount",
            "Type", "Receipt"
        ]);
        table.Rows.Count.ShouldBe(28);
    }

    [Fact]
    public void The_reader_keeps_accents_and_emoji_in_a_purpose()
    {
        var table = SettleUpCsvReader.Read(Export());

        var purposes = table.Rows.Select(r => r[5]).ToList();
        purposes.ShouldContain("Airbnb M\u00e1laga");
        purposes.ShouldContain(p => p.StartsWith("Flight Montr\u00e9al to Madrid"));
    }

    [Fact]
    public async Task Analysis_finds_the_columns_wherever_they_are()
    {
        var user = await TestData.SeedUserAsync(Db);

        var analysis = await Imports.AnalyzeCsvAsync(user.Id, Export(), "World tour.csv");

        analysis.SuggestedMapping["paidBy"].ShouldBe(0);
        analysis.SuggestedMapping["amount"].ShouldBe(1);
        analysis.SuggestedMapping["currency"].ShouldBe(2);
        analysis.SuggestedMapping["participants"].ShouldBe(3);
        analysis.SuggestedMapping["description"].ShouldBe(5);
        analysis.SuggestedMapping["date"].ShouldBe(7);

        analysis.SuggestedMapping["splitAmounts"].ShouldBe(4);
        analysis.SuggestedMapping["type"].ShouldBe(11);
    }

    [Fact]
    public async Task Analysis_finds_both_people()
    {
        var user = await TestData.SeedUserAsync(Db);

        var analysis = await Imports.AnalyzeCsvAsync(user.Id, Export(), "World tour.csv");

        analysis.DetectedMemberNames.ShouldBe(["Emma", "Nicolas"], ignoreOrder: true);
    }

    [Fact]
    public async Task Analysis_reports_the_currency()
    {
        var user = await TestData.SeedUserAsync(Db);

        var analysis = await Imports.AnalyzeCsvAsync(user.Id, Export(), "World tour.csv");

        analysis.DetectedCurrency.ShouldBe("CAD");
        analysis.RowCount.ShouldBe(28);
    }

    private static CsvColumnMapping WorldTourMapping() => new(
        DateColumn: 7, DescriptionColumn: 5, AmountColumn: 1,
        CurrencyColumn: 2, PaidByColumn: 0,
        ParticipantColumns: null, DateFormat: null, DecimalSeparator: null,
        ParticipantsColumn: 3, SplitAmountsColumn: 4, TypeColumn: 11);

    [Fact]
    public async Task Previewing_reads_every_purpose()
    {
        var user = await TestData.SeedUserAsync(Db);

        var preview = await Imports.PreviewCsvAsync(user.Id, Export(),
            new CsvPreviewRequest(null, WorldTourMapping(), new Dictionary<string, Guid?>(), "CAD"));

        preview.Rows.Count.ShouldBe(28);
        preview.Rows.Select(r => r.Description).ShouldContain("Flights YYC to YUL");
        preview.Rows.ShouldAllBe(r => r.Description.Length > 0);
    }

    [Fact]
    public async Task Committing_imports_the_whole_export_into_a_new_group()
    {
        var user = await TestData.SeedUserAsync(Db, "Nicolas");

        var result = await Imports.CommitCsvAsync(user.Id, Export(), new CsvCommitRequest(
            Guid.CreateVersion7(), null, "World tour", WorldTourMapping(),
            new Dictionary<string, Guid?>(), [], true, true, "CAD", "World tour.csv"));

        var group = await NewContext().Groups.SingleAsync(g => g.Name == "World tour");
        result.GroupId.ShouldBe(group.Id);

        var members = await NewContext().GroupMembers
            .Where(m => m.GroupId == group.Id).Select(m => m.DisplayName).ToListAsync();
        members.ShouldContain("Emma");
        members.ShouldContain("Nicolas");
    }

    [Fact]
    public async Task A_transfer_row_becomes_a_settlement_not_an_expense()
    {
        var user = await TestData.SeedUserAsync(Db, "Nicolas");

        var result = await Imports.CommitCsvAsync(user.Id, Export(), new CsvCommitRequest(
            Guid.CreateVersion7(), null, "World tour", WorldTourMapping(),
            new Dictionary<string, Guid?>(), [], true, true, "CAD", "World tour.csv"));

        var expenses = await NewContext().Expenses.Where(e => e.GroupId == result.GroupId).ToListAsync();
        var settlements = await NewContext().Settlements.Where(s => s.GroupId == result.GroupId).ToListAsync();

        expenses.Count.ShouldBe(26);
        settlements.Count.ShouldBe(2);
        settlements.Select(s => s.Amount).ShouldBe([590.80m, 204.17m], ignoreOrder: true);
    }

    [Fact]
    public async Task A_settlement_points_from_the_payer_to_the_person_named()
    {
        var user = await TestData.SeedUserAsync(Db, "Nicolas");

        var result = await Imports.CommitCsvAsync(user.Id, Export(), new CsvCommitRequest(
            Guid.CreateVersion7(), null, "World tour", WorldTourMapping(),
            new Dictionary<string, Guid?>(), [], true, true, "CAD", "World tour.csv"));

        var context = NewContext();
        var members = await context.GroupMembers
            .Where(m => m.GroupId == result.GroupId)
            .ToDictionaryAsync(m => m.Id, m => m.DisplayName);

        var settlement = await context.Settlements
            .Where(s => s.GroupId == result.GroupId)
            .OrderBy(s => s.SettledAt)
            .FirstAsync();

        members[settlement.FromMemberId].ShouldBe("Emma");
        members[settlement.ToMemberId].ShouldBe("Nicolas");
    }

    [Fact]
    public async Task Split_amounts_are_taken_from_the_export_rather_than_recomputed()
    {
        var user = await TestData.SeedUserAsync(Db, "Nicolas");

        var result = await Imports.CommitCsvAsync(user.Id, Export(), new CsvCommitRequest(
            Guid.CreateVersion7(), null, "World tour", WorldTourMapping(),
            new Dictionary<string, Guid?>(), [], true, true, "CAD", "World tour.csv"));

        var flights = await NewContext().Expenses
            .Include(e => e.Splits)
            .SingleAsync(e => e.GroupId == result.GroupId && e.Description == "Flights YYC to YUL");

        flights.Splits.Count.ShouldBe(2);
        flights.Splits.Select(s => s.Amount).ShouldAllBe(a => a == 209.43m);
    }

    [Fact]
    public async Task The_balance_after_importing_matches_what_settle_up_showed()
    {
        var user = await TestData.SeedUserAsync(Db, "Nicolas");

        var result = await Imports.CommitCsvAsync(user.Id, Export(), new CsvCommitRequest(
            Guid.CreateVersion7(), null, "World tour", WorldTourMapping(),
            new Dictionary<string, Guid?>(), [], true, true, "CAD", "World tour.csv"));

        var group = await Groups.GetAsync(user.Id, result.GroupId);

        var nicolas = group.Members.Single(m => m.DisplayName == "Nicolas").NetBalance;
        var emma = group.Members.Single(m => m.DisplayName == "Emma").NetBalance;

        nicolas.ShouldBeInRange(475.20m, 475.40m);
        emma.ShouldBeInRange(-475.40m, -475.20m);
    }
}
