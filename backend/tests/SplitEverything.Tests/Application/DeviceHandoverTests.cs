using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Auth;
using SplitEverything.Infrastructure.Auth;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Application;

public class DeviceHandoverTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private static readonly AuthOptions Options = new()
    {
        JwtSigningKey = "test-signing-key-that-is-long-enough-for-hmac-sha256",
        JwtIssuer = "split-everything-tests",
        JwtAudience = "split-everything-tests",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 30,
        AllowDevelopmentSignIn = true,
        AppBaseUrl = "https://split.example.com"
    };

    private AuthService Auth { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        Auth = new AuthService(
            Db,
            new JwtTokenService(Options, Clock),
            Substitute.For<IGoogleTokenVerifier>(),
            new InviteService(Db, Writer, Activity, Email, Options, Clock),
            Options,
            Admins, Clock);
    }

    [Fact]
    public async Task A_device_can_sign_its_own_account_in_again()
    {
        await Auth.SignInAsDeveloperAsync(new DevelopmentSignInRequest("alice@example.com", "Alice", "phone-1"));

        var again = await Auth.SignInAsDeveloperAsync(
            new DevelopmentSignInRequest("alice@example.com", "Alice", "phone-1"));

        again.User.Email.ShouldBe("alice@example.com");
    }

    [Fact]
    public async Task Another_account_on_the_same_device_id_is_refused_by_name()
    {
        await Auth.SignInAsDeveloperAsync(new DevelopmentSignInRequest("alice@example.com", "Alice", "phone-1"));

        var refusal = await Should.ThrowAsync<ForbiddenException>(() =>
            Auth.SignInAsDeveloperAsync(new DevelopmentSignInRequest("bob@example.com", "Bob", "phone-1")));

        refusal.Message.ShouldContain("registered to another account");
    }

    [Fact]
    public async Task A_fresh_device_id_lets_the_second_account_in()
    {
        await Auth.SignInAsDeveloperAsync(new DevelopmentSignInRequest("alice@example.com", "Alice", "phone-1"));

        var result = await Auth.SignInAsDeveloperAsync(
            new DevelopmentSignInRequest("bob@example.com", "Bob", "phone-1-rotated"));

        result.User.Email.ShouldBe("bob@example.com");
    }

    [Fact]
    public async Task The_first_account_still_has_its_own_device()
    {
        await Auth.SignInAsDeveloperAsync(new DevelopmentSignInRequest("alice@example.com", "Alice", "phone-1"));
        await Auth.SignInAsDeveloperAsync(new DevelopmentSignInRequest("bob@example.com", "Bob", "phone-1-rotated"));

        var again = await Auth.SignInAsDeveloperAsync(
            new DevelopmentSignInRequest("alice@example.com", "Alice", "phone-1"));

        again.User.Email.ShouldBe("alice@example.com");
    }

    [Fact]
    public async Task Signing_in_without_a_device_id_registers_nothing()
    {
        var result = await Auth.SignInAsDeveloperAsync(
            new DevelopmentSignInRequest("alice@example.com", "Alice", null));

        result.User.Email.ShouldBe("alice@example.com");
        (await NewContext().Devices.CountAsync()).ShouldBe(0);
    }
}
