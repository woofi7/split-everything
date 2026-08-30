using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace SplitEverything.Tests.Api;

/// <summary>
/// What the app refuses to start without.
///
/// A signing key shorter than 256 bits cannot sign an HS256 token, and this used to
/// be padded out with full stops to make it fit: sixteen characters of secret and
/// sixteen of punctuation, every token in the app signed with the result. Refusing
/// to start is the only honest answer, and it fails on the deploy rather than
/// quietly.
/// </summary>
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

        // The message has to say what to do about it, because whoever reads it is
        // in the middle of a deploy that has just stopped.
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

        // Boundaries are where a guard is wrong, and off by one here means either a
        // broken deploy or a key nobody checked.
        Should.NotThrow(() => factory.CreateClient());
    }
}
