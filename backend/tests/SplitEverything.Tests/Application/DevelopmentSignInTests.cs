using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Auth;
using SplitEverything.Infrastructure.Auth;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

/// <summary>
/// The development sign-in exists so the app can be used locally without a Google
/// OAuth client. That makes it an authentication bypass, so what matters is that
/// it is impossible to reach unless someone deliberately turned it on in a
/// non-production environment.
/// </summary>
public class DevelopmentSignInTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private static AuthOptions Options(bool allow) => new()
    {
        JwtSigningKey = "test-signing-key-that-is-long-enough-for-hmac-sha256",
        JwtIssuer = "split-everything-tests",
        JwtAudience = "split-everything-tests",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 30,
        GoogleClientId = "test-client-id",
        AppBaseUrl = "https://split.example.com",
        AllowDevelopmentSignIn = allow
    };

    private AuthService CreateAuth(bool allow)
    {
        var options = Options(allow);
        var invites = new InviteService(Db, Writer, Activity, Email, options, Clock);

        return new AuthService(
            Db,
            new JwtTokenService(options, Clock),
            Substitute.For<IGoogleTokenVerifier>(),
            invites,
            options,
            Clock);
    }

    [Fact]
    public async Task It_is_refused_when_it_has_not_been_turned_on()
    {
        var auth = CreateAuth(allow: false);

        // Off by default: a deployment that never sets the flag has no bypass.
        await Should.ThrowAsync<ForbiddenException>(
            () => auth.SignInAsDeveloperAsync(new DevelopmentSignInRequest("alice@example.com", "Alice", null)));
    }

    [Fact]
    public async Task It_creates_a_user_when_it_is_turned_on()
    {
        var auth = CreateAuth(allow: true);

        var result = await auth.SignInAsDeveloperAsync(
            new DevelopmentSignInRequest("alice@example.com", "Alice", TestData.DeviceA));

        result.IsNewUser.ShouldBeTrue();
        result.User.Email.ShouldBe("alice@example.com");
        result.User.DisplayName.ShouldBe("Alice");
    }

    [Fact]
    public async Task It_issues_the_same_tokens_a_google_sign_in_would()
    {
        var auth = CreateAuth(allow: true);

        var result = await auth.SignInAsDeveloperAsync(
            new DevelopmentSignInRequest("alice@example.com", "Alice", null));

        result.Tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
        result.Tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        result.Tokens.AccessTokenExpiresAt.ShouldBe(Clock.UtcNow.AddMinutes(15));
    }

    [Fact]
    public async Task Signing_in_twice_reuses_the_same_user()
    {
        var auth = CreateAuth(allow: true);
        var first = await auth.SignInAsDeveloperAsync(
            new DevelopmentSignInRequest("alice@example.com", "Alice", null));

        var second = await auth.SignInAsDeveloperAsync(
            new DevelopmentSignInRequest("alice@example.com", "Alice", null));

        second.User.Id.ShouldBe(first.User.Id);
        second.IsNewUser.ShouldBeFalse();
        (await NewContext().Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Two_addresses_are_two_people()
    {
        var auth = CreateAuth(allow: true);

        await auth.SignInAsDeveloperAsync(new DevelopmentSignInRequest("alice@example.com", "Alice", null));
        await auth.SignInAsDeveloperAsync(new DevelopmentSignInRequest("bob@example.com", "Bob", null));

        // Testing a shared group needs more than one account on one machine.
        (await NewContext().Users.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task The_subject_is_marked_as_a_development_account()
    {
        var auth = CreateAuth(allow: true);

        var result = await auth.SignInAsDeveloperAsync(
            new DevelopmentSignInRequest("alice@example.com", "Alice", null));

        // Namespaced so a development account can never collide with, or be
        // mistaken for, a real Google subject.
        var user = await NewContext().Users.FirstAsync(u => u.Id == result.User.Id);
        user.GoogleSubject.ShouldStartWith("dev:");
    }

    [Fact]
    public async Task A_real_google_account_is_not_taken_over_by_a_development_sign_in()
    {
        var existing = TestData.User("Alice", "alice@example.com", "google-real-subject");
        Db.Users.Add(existing);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var auth = CreateAuth(allow: true);
        var result = await auth.SignInAsDeveloperAsync(
            new DevelopmentSignInRequest("alice@example.com", "Alice", null));

        // Matching on email would hand a development sign-in somebody's real
        // account; the subject is the identity, so this is a separate user.
        result.User.Id.ShouldNotBe(existing.Id);
        (await NewContext().Users.CountAsync()).ShouldBe(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public async Task It_needs_a_plausible_email(string email)
    {
        var auth = CreateAuth(allow: true);

        await Should.ThrowAsync<ValidationException>(
            () => auth.SignInAsDeveloperAsync(new DevelopmentSignInRequest(email, "Alice", null)));
    }

    [Fact]
    public async Task It_falls_back_to_the_address_when_no_name_is_given()
    {
        var auth = CreateAuth(allow: true);

        var result = await auth.SignInAsDeveloperAsync(
            new DevelopmentSignInRequest("alice@example.com", null, null));

        result.User.DisplayName.ShouldBe("alice");
    }

    [Fact]
    public async Task It_registers_the_device_like_any_other_sign_in()
    {
        var auth = CreateAuth(allow: true);

        await auth.SignInAsDeveloperAsync(
            new DevelopmentSignInRequest("alice@example.com", "Alice", TestData.DeviceA));

        (await NewContext().Devices.AnyAsync(d => d.Id == TestData.DeviceA)).ShouldBeTrue();
    }

    [Fact]
    public async Task It_redeems_a_pending_invite_for_that_address()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var group = await Groups.CreateAsync(owner.Id,
            new SplitEverything.Application.Contracts.Groups.CreateGroupRequest(
                "Roommates", "CAD", null, null, null, null));

        var options = Options(allow: true);
        var invites = new InviteService(Db, Writer, Activity, Email, options, Clock);
        await invites.CreateAsync(owner.Id, group.Id,
            new SplitEverything.Application.Contracts.Groups.CreateInviteRequest(
                "alice@example.com", null, 1, 72));

        var auth = new AuthService(
            Db, new JwtTokenService(options, Clock),
            Substitute.For<IGoogleTokenVerifier>(), invites, options, Clock);

        var result = await auth.SignInAsDeveloperAsync(
            new DevelopmentSignInRequest("alice@example.com", "Alice", null));

        result.AutoJoinedGroupIds.ShouldContain(group.Id);
    }

    [Fact]
    public void Capabilities_report_whether_google_is_configured()
    {
        var auth = CreateAuth(allow: false);

        var capabilities = auth.GetCapabilities();

        capabilities.GoogleConfigured.ShouldBeTrue();
        capabilities.DevelopmentSignIn.ShouldBeFalse();
    }

    [Fact]
    public void Capabilities_report_the_development_sign_in_when_it_is_on()
    {
        var auth = CreateAuth(allow: true);

        auth.GetCapabilities().DevelopmentSignIn.ShouldBeTrue();
    }

    [Fact]
    public void Capabilities_report_google_as_unconfigured_when_there_is_no_client_id()
    {
        var options = Options(allow: false);
        options.GoogleClientId = string.Empty;

        var auth = new AuthService(
            Db, new JwtTokenService(options, Clock),
            Substitute.For<IGoogleTokenVerifier>(),
            new InviteService(Db, Writer, Activity, Email, options, Clock),
            options, Clock);

        // The sign-in page needs to tell "not set up" apart from "broken".
        auth.GetCapabilities().GoogleConfigured.ShouldBeFalse();
    }
}
