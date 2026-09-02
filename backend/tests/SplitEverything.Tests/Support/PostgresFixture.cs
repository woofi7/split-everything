using Microsoft.EntityFrameworkCore;
using SplitEverything.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SplitEverything.Tests.Support;

/// <summary>
/// One throwaway Postgres for the whole run.
///
/// A real database rather than the in-memory provider, because the schema leans on
/// jsonb columns, partial unique indexes and identity sequences - none of which
/// the in-memory provider enforces, so tests would pass against behaviour Postgres
/// does not actually have.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("split_everything_test")
        .WithUsername("test")
        .WithPassword("test")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Create the schema once from the model, then every test class truncates.
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSplitEverythingPostgres(ConnectionString)
            .EnableSensitiveDataLogging()
            .Options;
        return new AppDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
