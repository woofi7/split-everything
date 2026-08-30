using Microsoft.EntityFrameworkCore;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Tests.Support;

/// <summary>
/// Base for tests that touch the database. Each test starts from an empty schema
/// so no test can depend on another's leftovers.
/// </summary>
[Collection(PostgresCollection.Name)]
public abstract class DatabaseTestBase : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    protected DatabaseTestBase(PostgresFixture fixture) => _fixture = fixture;

    protected AppDbContext Db { get; private set; } = null!;

    /// <summary>A second context, for asserting what actually landed in the database.</summary>
    protected AppDbContext NewContext() => _fixture.CreateContext();

    protected string ConnectionString => _fixture.ConnectionString;

    public virtual async Task InitializeAsync()
    {
        Db = _fixture.CreateContext();
        await ResetAsync();
    }

    public virtual Task DisposeAsync()
    {
        Db.Dispose();
        return Task.CompletedTask;
    }

    private async Task ResetAsync()
    {
        // Truncate rather than drop: keeps the schema and the identity sequences in
        // one statement, and is far faster than recreating the database per test.
        var tables = new[]
        {
            "expense_item_shares", "expense_items", "expense_splits", "expense_comments",
            "expense_revisions", "expenses", "recurring_expenses", "settlements",
            "sync_log", "sync_snapshots", "sync_conflicts", "activity_log",
            "group_invites", "group_lineage_links", "group_members", "groups",
            "import_batches", "receipts",
            "push_subscriptions", "devices", "refresh_tokens", "users", "exchange_rates"
        };

        await Db.Database.ExecuteSqlRawAsync(
            $"TRUNCATE TABLE {string.Join(", ", tables)} RESTART IDENTITY CASCADE;");
    }

}
