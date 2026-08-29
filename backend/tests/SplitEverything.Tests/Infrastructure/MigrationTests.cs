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

    [Fact]
    public async Task Renaming_the_icon_columns_keeps_the_values()
    {
        await using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();

        // Stop at the schema as it was before the rename, so the data being
        // migrated is real rather than assumed.
        await migrator.MigrateAsync("20260831145838_InitialSchema");

        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO categories (id, key, name, emoji, color_hex, sort_order)
                VALUES (gen_random_uuid(), 'legacy', 'Legacy', 'cart', '#000000', 1);

                INSERT INTO users (id, google_subject, email, display_name, default_currency, locale, prefers_light_theme, created_at)
                VALUES (gen_random_uuid(), 'sub-1', 'a@b.c', 'A', 'CAD', 'en', false, now());

                INSERT INTO groups (id, name, base_currency, emoji_icon, color_hex, is_archived, created_by_user_id, sequence_counter, lineage_id, vector_clock_json, created_at, updated_at, is_deleted, server_seq)
                VALUES (gen_random_uuid(), 'Legacy group', 'CAD', 'house', '#4f46e5', false, (SELECT id FROM users LIMIT 1), 0, gen_random_uuid(), '{}', now(), now(), false, 0);
                """, connection);
            await command.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();

        // A drop-and-add, which the scaffolder wanted to generate, would have left
        // these null.
        var category = await db.Categories.FirstAsync(c => c.Key == "legacy");
        category.IconName.ShouldBe("cart");

        var group = await db.Groups.FirstAsync(g => g.Name == "Legacy group");
        group.IconName.ShouldBe("house");
    }

    [Fact]
    public async Task The_icon_columns_are_wide_enough_for_a_font_awesome_name()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        foreach (var table in new[] { "groups", "categories" })
        {
            await using var command = new NpgsqlCommand(
                $"""
                SELECT character_maximum_length FROM information_schema.columns
                WHERE table_name = '{table}' AND column_name = 'icon_name'
                """, connection);

            // "money-bill-transfer" is nineteen characters; the old sixteen would
            // have truncated it into a name that resolves to nothing.
            Convert.ToInt32(await command.ExecuteScalarAsync()).ShouldBe(48);
        }
    }

    [Fact]
    public async Task Seeding_after_a_migration_inserts_the_system_categories()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        await SeedRunner.RunAsync(db);

        (await db.Categories.CountAsync(c => c.OwnerUserId == null))
            .ShouldBe(CategorySeed.Categories.Count);
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_the_categories()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        await SeedRunner.RunAsync(db);
        await SeedRunner.RunAsync(db);

        (await db.Categories.CountAsync(c => c.OwnerUserId == null))
            .ShouldBe(CategorySeed.Categories.Count);
    }
}
