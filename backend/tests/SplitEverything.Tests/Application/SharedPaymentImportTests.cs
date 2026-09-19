using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Contracts.Import;
using SplitEverything.Infrastructure.Import;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Application;

public class SharedPaymentImportTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private ImportService Imports { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Imports = new ImportService(Db, Writer, Activity, Currency, Clock, Groups);
    }

    private static Stream Export()
        => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settleup-shared-payment.csv"));

    private static CsvColumnMapping Mapping() => new(
        DateColumn: 7, DescriptionColumn: 5, AmountColumn: 1,
        CurrencyColumn: 2, PaidByColumn: 0,
        ParticipantColumns: null, DateFormat: null, DecimalSeparator: null,
        ParticipantsColumn: 3, SplitAmountsColumn: 4, TypeColumn: 11);

    private static CsvCommitRequest Commit() => new(
        Guid.CreateVersion7(), null, "Shared", Mapping(),
        new Dictionary<string, Guid?>(), [], true, true, "CAD", "shared.csv");

    [Fact]
    public void The_amount_parser_refuses_a_list_instead_of_concatenating_it()
    {
        CsvValueParser.ParseAmount("40;25", null).ShouldBeNull();

        CsvValueParser.ParseAmount("40.86", null).ShouldBe(40.86m);
        CsvValueParser.ParseAmountList("40;25", null).ShouldBe([40m, 25m]);
    }

    [Fact]
    public async Task The_total_matches_the_export()
    {
        var user = await TestData.SeedUserAsync(Db);

        var result = await Imports.CommitCsvAsync(user.Id, Export(), Commit());

        var expenses = await NewContext().Expenses
            .Where(e => e.GroupId == result.GroupId).ToListAsync();

        expenses.Sum(e => e.AmountInBaseCurrency).ShouldBe(1605.86m);
    }

    [Fact]
    public async Task A_shared_payment_is_one_expense_that_two_people_paid_into()
    {
        var user = await TestData.SeedUserAsync(Db);

        var result = await Imports.CommitCsvAsync(user.Id, Export(), Commit());

        var context = NewContext();
        var members = await context.GroupMembers
            .Where(m => m.GroupId == result.GroupId)
            .ToDictionaryAsync(m => m.Id, m => m.DisplayName);

        var shared = await context.Expenses
            .Include(e => e.Payers)
            .Where(e => e.GroupId == result.GroupId && e.Description == "Frying pans")
            .ToListAsync();

        shared.ShouldHaveSingleItem();
        shared[0].Amount.ShouldBe(65m);
        shared[0].Payers.Select(y => (members[y.MemberId], y.Amount))
            .ShouldBe([("Emma", 40m), ("Nicolas", 25m)], ignoreOrder: true);

        members[shared[0].PaidByMemberId].ShouldBe("Emma");
    }

    [Fact]
    public async Task The_split_is_the_one_the_export_gave_the_whole_row()
    {
        var user = await TestData.SeedUserAsync(Db);

        var result = await Imports.CommitCsvAsync(user.Id, Export(), Commit());

        var context = NewContext();
        var shared = await context.Expenses
            .Where(e => e.GroupId == result.GroupId && e.Description == "Frying pans")
            .Select(e => e.Id).SingleAsync();

        var splits = await context.ExpenseSplits
            .Where(s => s.ExpenseId == shared).ToListAsync();

        splits.Sum(s => s.Amount).ShouldBe(65m);
        splits.Select(s => s.Amount).ShouldBe([32.50m, 32.50m], ignoreOrder: true);
    }

    [Fact]
    public async Task Nobody_ends_up_owing_a_different_amount_than_they_did_in_settle_up()
    {
        var user = await TestData.SeedUserAsync(Db);

        var result = await Imports.CommitCsvAsync(user.Id, Export(), Commit());

        var context = NewContext();
        var members = await context.GroupMembers
            .Where(m => m.GroupId == result.GroupId)
            .ToDictionaryAsync(m => m.DisplayName, m => m.Id);

        var expenses = await context.Expenses
            .Include(e => e.Payers)
            .Where(e => e.GroupId == result.GroupId && e.Description == "Frying pans")
            .ToListAsync();
        var ids = expenses.Select(e => e.Id).ToList();
        var splits = await context.ExpenseSplits.Where(s => ids.Contains(s.ExpenseId)).ToListAsync();

        decimal NetOf(Guid memberId) =>
            expenses.SelectMany(e => e.Payers).Where(y => y.MemberId == memberId).Sum(y => y.Amount)
            - splits.Where(s => s.MemberId == memberId).Sum(s => s.Amount);

        NetOf(members["Emma"]).ShouldBe(7.50m);
        NetOf(members["Nicolas"]).ShouldBe(-7.50m);
    }

    [Fact]
    public async Task Two_amounts_and_one_payer_is_flagged_rather_than_guessed()
    {
        var user = await TestData.SeedUserAsync(Db);

        var preview = await Imports.PreviewCsvAsync(user.Id, Row("Emma", "40;25"),
            new CsvPreviewRequest(null, Mapping(), new Dictionary<string, Guid?>(), "CAD"));

        preview.Rows.Single().Problems.ShouldContain(p => p.Contains("1 payers and 2 amounts"));
    }

    [Fact]
    public async Task A_comma_in_a_payer_name_is_not_two_payers()
    {
        var user = await TestData.SeedUserAsync(Db);

        var preview = await Imports.PreviewCsvAsync(user.Id, Row("Doe, John", "65"),
            new CsvPreviewRequest(null, Mapping(), new Dictionary<string, Guid?>(), "CAD"));

        var row = preview.Rows.Single();
        row.Amount.ShouldBe(65m);
        row.Problems.ShouldNotContain(p => p.Contains("amounts"));
    }

    private static Stream Row(string paidBy, string amount)
    {
        var text = "\"Who paid\",\"Amount\",\"Currency\",\"For whom\",\"Split amounts\",\"Purpose\","
                   + "\"Category\",\"Date & time\",\"Timezone\",\"Exchange rate\",\"Converted amount\","
                   + "\"Type\",\"Receipt\"\r\n"
                   + $"\"{paidBy}\",\"{amount}\",\"CAD\",\"Emma;Nicolas\",\"32.5;32.5\",\"Frying pans\",\" \","
                   + "\"2025-11-29 17:34:59\",\"\",\"\",\"65\",\"expense\",\"\"\r\n";

        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
    }
}
