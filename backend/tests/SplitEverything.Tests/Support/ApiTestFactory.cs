using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SplitEverything.Application.Abstractions;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Persistence.Seed;

namespace SplitEverything.Tests.Support;

/// <summary>
/// The real API, over a real Postgres, with only the outbound network faked.
///
/// Everything in between - routing, JSON, model binding, JWT validation,
/// authorization, the exception handler, the DI graph - is exercised as deployed.
/// That is the point: most of what an integration test can catch lives in those
/// seams, not in the services the unit tests already cover.
/// </summary>
public sealed class ApiTestFactory(string connectionString) : WebApplicationFactory<Program>
{
    public IGoogleTokenVerifier Google { get; } = Substitute.For<IGoogleTokenVerifier>();
    public IEmailSender Email { get; } = Substitute.For<IEmailSender>();
    public IPushDispatcher Push { get; } = Substitute.For<IPushDispatcher>();
    public ICurrencyConverter Currency { get; } = Substitute.For<ICurrencyConverter>();

    /// <summary>
    /// Settings are pushed through environment variables rather than an in-memory
    /// source: the app's own appsettings.json is added after anything the factory
    /// registers on the web host builder, and would otherwise win and point the
    /// tests at the development database.
    /// </summary>
    private static readonly Dictionary<string, string> Settings = new()
    {
        ["Auth__JwtSigningKey"] = "integration-test-signing-key-long-enough-for-hmac",
        ["Auth__JwtIssuer"] = "split-everything",
        ["Auth__JwtAudience"] = "split-everything",
        ["Auth__GoogleClientId"] = "test-client-id",
        ["Auth__AppBaseUrl"] = "https://split.test",
        // The API layer is under test, not the schedulers.
        ["Database__MigrateOnStartup"] = "false",
        ["Push__VapidPublicKey"] = "test-public-key"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", connectionString);
        Environment.SetEnvironmentVariable("ReceiptStorage__RootPath",
            Path.Combine(Path.GetTempPath(), "split-api-receipts"));
        foreach (var (key, value) in Settings)
            Environment.SetEnvironmentVariable(key, value);

        builder.ConfigureServices(services =>
        {
            Replace(services, Google);
            Replace(services, Email);
            Replace(services, Push);
            Replace(services, Currency);

            // Drop the background workers: their tick would interleave with tests.
            foreach (var hosted in services.Where(s => s.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(hosted);
        });
    }

    /// <summary>Creates the schema and seeds it, once per factory.</summary>
    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        await SeedRunner.RunAsync(db);
    }

    private static void Replace<T>(IServiceCollection services, T instance) where T : class
    {
        foreach (var existing in services.Where(s => s.ServiceType == typeof(T)).ToList())
            services.Remove(existing);
        services.AddSingleton(instance);
    }
}
