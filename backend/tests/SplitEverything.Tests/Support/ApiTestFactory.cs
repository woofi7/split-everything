using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SplitEverything.Application.Abstractions;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Tests.Support;

public sealed class ApiTestFactory(string connectionString) : WebApplicationFactory<Program>
{
    public IGoogleTokenVerifier Google { get; } = Substitute.For<IGoogleTokenVerifier>();
    public IEmailSender Email { get; } = Substitute.For<IEmailSender>();
    public IPushDispatcher Push { get; } = Substitute.For<IPushDispatcher>();
    public ICurrencyConverter Currency { get; } = Substitute.For<ICurrencyConverter>();

    private static readonly Dictionary<string, string> Settings = new()
    {
        ["Auth__JwtSigningKey"] = "integration-test-signing-key-long-enough-for-hmac",
        ["Auth__JwtIssuer"] = "split-everything",
        ["Auth__JwtAudience"] = "split-everything",
        ["Auth__GoogleClientId"] = "test-client-id",
        ["Auth__AppBaseUrl"] = "https://split.test",
        ["Database__MigrateOnStartup"] = "false",
        ["Push__VapidPublicKey"] = "BDLIpARp5poJEsnhCHwluND9bDbYwZX2nMc3rKpQbPAjRDnLFQUFKyr3av2mffIbsNoWZc0D7UL6kQjxBwcIwTw",
        ["Auth__AllowDevelopmentSignIn"] = "true",
        ["Admin__Emails__0"] = "admin@example.com"
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

            foreach (var hosted in services.Where(s => s.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(hosted);
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    private static void Replace<T>(IServiceCollection services, T instance) where T : class
    {
        foreach (var existing in services.Where(s => s.ServiceType == typeof(T)).ToList())
            services.Remove(existing);
        services.AddSingleton(instance);
    }
}
