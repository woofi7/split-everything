using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Domain.Sync;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Infrastructure;

/// <summary>
/// The schema guarantees the sync engine relies on. These are asserted against real
/// Postgres because they are all constraints the in-memory provider silently ignores.
/// </summary>
public class SchemaTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task Clock_columns_are_jsonb_so_they_can_be_queried_and_indexed()
    {
        var type = await ScalarAsync(
            """
            SELECT data_type FROM information_schema.columns
            WHERE table_name = 'expenses' AND column_name = 'vector_clock_json'
            """);

        type.ShouldBe("jsonb");
    }

    [Fact]
    public async Task Columns_are_snake_cased()
    {
        var exists = await ScalarAsync(
            """
            SELECT column_name FROM information_schema.columns
            WHERE table_name = 'expenses' AND column_name = 'amount_in_base_currency'
            """);

        exists.ShouldBe("amount_in_base_currency");
    }

    [Fact]
    public async Task Two_users_cannot_share_a_google_subject()
    {
        Db.Users.Add(TestData.User("Alice", googleSub: "same-sub"));
        Db.Users.Add(TestData.User("Bob", email: "bob@example.com", googleSub: "same-sub"));

        var ex = await Should.ThrowAsync<DbUpdateException>(() => Db.SaveChangesAsync());
        ex.InnerException.ShouldBeOfType<PostgresException>()
            .SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task A_user_cannot_join_the_same_group_twice()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, _) = await TestData.SeedGroupAsync(Db, user, "Alice");

        Db.GroupMembers.Add(TestData.Member(group.Id, user.Id, "Alice again"));

        var ex = await Should.ThrowAsync<DbUpdateException>(() => Db.SaveChangesAsync());
        ex.InnerException.ShouldBeOfType<PostgresException>()
            .SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task A_group_can_hold_several_unclaimed_placeholder_members()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, _) = await TestData.SeedGroupAsync(Db, user, "Alice");

        // The partial index must exempt null user ids, or a Settle Up import of
        // several names-only members would fail on the second row.
        Db.GroupMembers.Add(TestData.Member(group.Id, null, "Bob"));
        Db.GroupMembers.Add(TestData.Member(group.Id, null, "Carol"));
        await Db.SaveChangesAsync();

        (await NewContext().GroupMembers.CountAsync(m => m.GroupId == group.Id)).ShouldBe(3);
    }

    [Fact]
    public async Task A_member_is_never_hard_deleted_out_from_under_an_expense()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, members) = await TestData.SeedGroupAsync(Db, user, "Alice", "Bob");
        Db.Expenses.Add(TestData.Expense(group.Id, members["Bob"], 50m));
        await Db.SaveChangesAsync();

        // The database itself must refuse this, not only EF's change tracker: a
        // member with history has to survive as a row or balances lose their payer.
        var ex = await Should.ThrowAsync<PostgresException>(() =>
            Db.Database.ExecuteSqlAsync(
                $"DELETE FROM group_members WHERE id = {members["Bob"]}"));

        ex.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Deleting_a_group_takes_its_expenses_and_log_with_it()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, members) = await TestData.SeedGroupAsync(Db, user, "Alice");
        var expense = TestData.Expense(group.Id, members["Alice"], 10m);
        Db.Expenses.Add(expense);
        Db.SyncLog.Add(new SyncLogEntry
        {
            GroupId = group.Id, ServerSeq = 1, EntityType = SyncEntityType.Expense,
            EntityId = expense.Id, Operation = SyncOperation.Create,
            DeviceId = TestData.DeviceA, LineageId = group.LineageId
        });
        await Db.SaveChangesAsync();

        Db.Groups.Remove(await Db.Groups.FirstAsync(g => g.Id == group.Id));
        await Db.SaveChangesAsync();

        var fresh = NewContext();
        (await fresh.Expenses.CountAsync()).ShouldBe(0);
        (await fresh.SyncLog.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_group_cannot_reuse_a_sequence_number()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, _) = await TestData.SeedGroupAsync(Db, user, "Alice");

        for (var i = 0; i < 2; i++)
        {
            Db.SyncLog.Add(new SyncLogEntry
            {
                GroupId = group.Id, ServerSeq = 7,
                EntityType = SyncEntityType.Expense, EntityId = Guid.NewGuid(),
                Operation = SyncOperation.Create, DeviceId = TestData.DeviceA,
                LineageId = group.LineageId
            });
        }

        var ex = await Should.ThrowAsync<DbUpdateException>(() => Db.SaveChangesAsync());
        ex.InnerException.ShouldBeOfType<PostgresException>()
            .SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Two_groups_number_their_own_sequences_independently()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (first, _) = await TestData.SeedGroupAsync(Db, user, "Alice");
        var second = TestData.Group(user.Id, "Trip");
        Db.Groups.Add(second);
        await Db.SaveChangesAsync();

        foreach (var groupId in new[] { first.Id, second.Id })
        {
            Db.SyncLog.Add(new SyncLogEntry
            {
                GroupId = groupId, ServerSeq = 1,
                EntityType = SyncEntityType.Group, EntityId = groupId,
                Operation = SyncOperation.Create, DeviceId = TestData.DeviceA,
                LineageId = Guid.NewGuid()
            });
        }

        await Db.SaveChangesAsync();
        (await NewContext().SyncLog.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task The_same_member_cannot_appear_twice_on_one_expense()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, members) = await TestData.SeedGroupAsync(Db, user, "Alice");
        var expense = TestData.Expense(group.Id, members["Alice"], 20m);
        Db.Expenses.Add(expense);
        Db.ExpenseSplits.Add(TestData.Split(expense.Id, group.Id, members["Alice"], 10m));
        Db.ExpenseSplits.Add(TestData.Split(expense.Id, group.Id, members["Alice"], 10m));

        var ex = await Should.ThrowAsync<DbUpdateException>(() => Db.SaveChangesAsync());
        ex.InnerException.ShouldBeOfType<PostgresException>()
            .SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Two_receipts_cannot_store_the_same_bytes_twice()
    {
        for (var i = 0; i < 2; i++)
        {
            Db.Receipts.Add(new Receipt
            {
                StorageKey = $"receipts/{i}.jpg",
                ContentHash = "identical-hash",
                UploadedByUserId = Guid.NewGuid(),
                SizeBytes = 100
            });
        }

        var ex = await Should.ThrowAsync<DbUpdateException>(() => Db.SaveChangesAsync());
        ex.InnerException.ShouldBeOfType<PostgresException>()
            .SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task One_exchange_rate_is_cached_per_pair_per_day()
    {
        for (var i = 0; i < 2; i++)
        {
            Db.ExchangeRates.Add(new ExchangeRateSnapshot
            {
                BaseCurrency = "CAD", QuoteCurrency = "EUR",
                Rate = 0.68m + i, RateDate = new DateOnly(2026, 8, 31)
            });
        }

        var ex = await Should.ThrowAsync<DbUpdateException>(() => Db.SaveChangesAsync());
        ex.InnerException.ShouldBeOfType<PostgresException>()
            .SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task An_expense_revision_number_is_unique_per_expense()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, members) = await TestData.SeedGroupAsync(Db, user, "Alice");
        var expense = TestData.Expense(group.Id, members["Alice"], 20m);
        Db.Expenses.Add(expense);

        for (var i = 0; i < 2; i++)
        {
            Db.ExpenseRevisions.Add(new ExpenseRevision
            {
                ExpenseId = expense.Id, GroupId = group.Id, Revision = 1,
                SnapshotJson = "{}", VectorClockJson = "{}"
            });
        }

        var ex = await Should.ThrowAsync<DbUpdateException>(() => Db.SaveChangesAsync());
        ex.InnerException.ShouldBeOfType<PostgresException>()
            .SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Sync_log_ids_come_from_an_identity_sequence()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, _) = await TestData.SeedGroupAsync(Db, user, "Alice");

        for (var seq = 1; seq <= 3; seq++)
        {
            Db.SyncLog.Add(new SyncLogEntry
            {
                GroupId = group.Id, ServerSeq = seq,
                EntityType = SyncEntityType.Expense, EntityId = Guid.NewGuid(),
                Operation = SyncOperation.Create, DeviceId = TestData.DeviceA,
                LineageId = group.LineageId
            });
        }
        await Db.SaveChangesAsync();

        var ids = await NewContext().SyncLog.OrderBy(e => e.Id).Select(e => e.Id).ToListAsync();
        ids.ShouldBe(new long[] { 1, 2, 3 });
    }

    [Fact]
    public async Task A_clock_survives_a_database_round_trip()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, members) = await TestData.SeedGroupAsync(Db, user, "Alice");
        var clock = VectorClock.Empty.Tick(TestData.DeviceA).Tick(TestData.DeviceB).Tick(TestData.DeviceA);
        var expense = TestData.Expense(group.Id, members["Alice"], 20m);
        expense.Clock = clock;
        Db.Expenses.Add(expense);
        await Db.SaveChangesAsync();

        var reloaded = await NewContext().Expenses.FirstAsync(e => e.Id == expense.Id);

        reloaded.Clock.ShouldBe(clock);
        reloaded.Clock[TestData.DeviceA].ShouldBe(2);
    }

    [Fact]
    public async Task Amounts_keep_four_decimal_places_for_rate_converted_values()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, members) = await TestData.SeedGroupAsync(Db, user, "Alice");
        var expense = TestData.Expense(group.Id, members["Alice"], 100m, currency: "EUR");
        expense.ExchangeRate = 1.47382915m;
        expense.AmountInBaseCurrency = 147.3829m;
        Db.Expenses.Add(expense);
        await Db.SaveChangesAsync();

        var reloaded = await NewContext().Expenses.FirstAsync(e => e.Id == expense.Id);

        reloaded.AmountInBaseCurrency.ShouldBe(147.3829m);
        reloaded.ExchangeRate.ShouldBe(1.47382915m);
    }

    [Fact]
    public async Task Timestamps_round_trip_as_utc()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, members) = await TestData.SeedGroupAsync(Db, user, "Alice");
        var spentAt = new DateTimeOffset(2026, 3, 15, 18, 30, 0, TimeSpan.FromHours(-4));
        var expense = TestData.Expense(group.Id, members["Alice"], 20m, spentAt: spentAt);
        Db.Expenses.Add(expense);
        await Db.SaveChangesAsync();

        var reloaded = await NewContext().Expenses.FirstAsync(e => e.Id == expense.Id);

        reloaded.SpentAt.ToUniversalTime().ShouldBe(spentAt.ToUniversalTime());
    }


    private async Task<string?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (await command.ExecuteScalarAsync())?.ToString();
    }
}
