using Microsoft.EntityFrameworkCore;

namespace SplitEverything.Infrastructure.Persistence;

public static class DbContextOptionsExtensions
{
    /// <summary>
    /// Single place the provider is configured, so app startup and the test fixture
    /// cannot drift apart on naming or enum mapping.
    /// </summary>
    public static TBuilder UseSplitEverythingPostgres<TBuilder>(
        this TBuilder builder, string connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        builder
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
                .EnableRetryOnFailure(3))
            // Enums land as ints and columns as snake_case; the partial index filters
            // in the configurations are written against those column names.
            .UseSnakeCaseNamingConvention();
        return builder;
    }
}
