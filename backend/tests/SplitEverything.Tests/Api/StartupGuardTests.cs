using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace SplitEverything.Tests.Api;

public class StartupGuardTests
{
    [Fact]
    public void A_signing_key_that_is_too_short_stops_the_app()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("Auth:JwtSigningKey", "too-short");
                builder.UseSetting("Database:MigrateOnStartup", "false");
            });

        var failure = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        failure.Message.ShouldContain("JwtSigningKey");
        failure.Message.ShouldContain("32 bytes");
        failure.Message.ShouldContain("openssl rand");
    }

    [Fact]
    public void A_key_of_exactly_the_minimum_is_accepted()
    {
        var thirtyTwo = new string('k', 32);

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("Auth:JwtSigningKey", thirtyTwo);
                builder.UseSetting("Database:MigrateOnStartup", "false");
            });

        Should.NotThrow(() => factory.CreateClient());
    }
}
