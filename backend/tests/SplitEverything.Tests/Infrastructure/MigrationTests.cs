using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Persistence.Seed;
using SplitEverything.Tests.Support;
using Testcontainers.PostgreSql;

namespace SplitEverything.Tests.Infrastructure;

/// <summary>
/// The rest of the suite builds its schema from the model. These tests check the
/// other path, the one production actually uses: applying the migrations to an
/// empty database, and confirming the result still matches the model.
/// </summary>
public class MigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("split_everything_migrations")
        .WithUsername("test")
        .WithPassword("test")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private AppDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSplitEverythingPostgres(_container.GetConnectionString())
            .Options);

    [Fact]
    public async Task The_migrations_apply_to_an_empty_database()
    {
        await using var db = CreateContext();

        await db.Database.MigrateAsync();

        (await db.Database.GetAppliedMigrationsAsync()).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_migrated_schema_matches_the_model()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        // A pending model change means someone edited an entity without adding a
        // migration, and the next deploy would run against the wrong schema.
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task The_migrated_schema_carries_the_jsonb_and_partial_index_details()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using (var command = new NpgsqlCommand(
            """
            SELECT data_type FROM information_schema.columns
            WHERE table_name = 'sync_log' AND column_name = 'payload_json'
            """, connection))
        {
            (await command.ExecuteScalarAsync())?.ToString().ShouldBe("jsonb");
        }

        await using (var command = new NpgsqlCommand(
            """
            SELECT indexdef FROM pg_indexes
            WHERE tablename = 'group_members' AND indexdef LIKE '%user_id IS NOT NULL%'
            """, connection))
        {
            (await command.ExecuteScalarAsync()).ShouldNotBeNull();
        }
    }

}
