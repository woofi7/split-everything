using Microsoft.EntityFrameworkCore;

namespace SplitEverything.Infrastructure.Persistence;

public static class DbContextOptionsExtensions
{
    public static TBuilder UseSplitEverythingPostgres<TBuilder>(
        this TBuilder builder, string connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        builder
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
                .EnableRetryOnFailure(3))
            .UseSnakeCaseNamingConvention();
        return builder;
    }
}
