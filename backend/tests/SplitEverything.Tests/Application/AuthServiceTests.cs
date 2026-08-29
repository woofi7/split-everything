using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Auth;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Auth;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

public class AuthServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private IGoogleTokenVerifier Google { get; set; } = null!;
    private AuthService Auth { get; set; } = null!;
    private InviteService Invites { get; set; } = null!;

    private static readonly AuthOptions Options = new()
    {
        JwtSigningKey = "test-signing-key-that-is-long-enough-for-hmac-sha256",
        JwtIssuer = "split-everything-tests",
        JwtAudience = "split-everything-tests",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 30,
        GoogleClientId = "test-client-id",
        AppBaseUrl = "https://split.example.com"
    };

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        Google = Substitute.For<IGoogleTokenVerifier>();
        GoogleReturns("google-sub-alice", "alice@example.com", "Alice");

        var tokens = new JwtTokenService(Options, Clock);
        Invites = new InviteService(Db, Writer, Activity, Email, Options, Clock);
        Auth = new AuthService(Db, tokens, Google, Invites, Options, Clock);
    }

    private void GoogleReturns(string sub, string email, string name, bool verified = true)
        => Google.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GoogleIdentity(sub, email, verified, name, "https://pic.example/a.png")));

    private static GoogleSignInRequest SignIn(string? deviceId = TestData.DeviceA)
        => new("google-id-token", deviceId, "Test device", "web");

    [Fact]
    public async Task A_first_sign_in_creates_the_user_from_the_google_identity()
    {
        var result = await Auth.SignInWithGoogleAsync(SignIn());

        result.IsNewUser.ShouldBeTrue();
        result.User.Email.ShouldBe("alice@example.com");
        result.User.DisplayName.ShouldBe("Alice");
        result.User.AvatarUrl.ShouldBe("https://pic.example/a.png");
    }

    [Fact]
    public async Task Signing_in_again_reuses_the_same_user()
    {
        var first = await Auth.SignInWithGoogleAsync(SignIn());

        var second = await Auth.SignInWithGoogleAsync(SignIn());

        second.IsNewUser.ShouldBeFalse();
        second.User.Id.ShouldBe(first.User.Id);
        (await NewContext().Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task The_google_subject_identifies_the_user_even_if_the_email_changes()
    {
        var first = await Auth.SignInWithGoogleAsync(SignIn());
        GoogleReturns("google-sub-alice", "alice.new@example.com", "Alice");

        var second = await Auth.SignInWithGoogleAsync(SignIn());

        second.User.Id.ShouldBe(first.User.Id);
        second.User.Email.ShouldBe("alice.new@example.com");
    }

    [Fact]
    public async Task An_unverified_google_email_is_refused()
    {
        GoogleReturns("google-sub-x", "unverified@example.com", "X", verified: false);

        await Should.ThrowAsync<ForbiddenException>(() => Auth.SignInWithGoogleAsync(SignIn()));
    }

    [Fact]
    public async Task A_sign_in_issues_an_access_token_and_a_refresh_token()
    {
        var result = await Auth.SignInWithGoogleAsync(SignIn());

        result.Tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
        result.Tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        result.Tokens.AccessTokenExpiresAt.ShouldBe(Clock.UtcNow.AddMinutes(15));
        result.Tokens.RefreshTokenExpiresAt.ShouldBe(Clock.UtcNow.AddDays(30));
    }

    [Fact]
    public async Task The_refresh_token_is_stored_only_as_a_hash()
    {
        var result = await Auth.SignInWithGoogleAsync(SignIn());

        var stored = await NewContext().RefreshTokens.SingleAsync();
        stored.TokenHash.ShouldNotBe(result.Tokens.RefreshToken);
        stored.TokenHash.Length.ShouldBe(64);
    }

    [Fact]
    public async Task A_sign_in_registers_the_device()
    {
        await Auth.SignInWithGoogleAsync(SignIn());

        (await NewContext().Devices.AnyAsync(d => d.Id == TestData.DeviceA)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_sign_in_without_a_device_id_still_works_for_a_plain_browser()
    {
        var result = await Auth.SignInWithGoogleAsync(SignIn(deviceId: null));

        result.Tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
        (await NewContext().Devices.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Refreshing_returns_a_new_pair_and_rotates_the_old_token()
    {
        var signIn = await Auth.SignInWithGoogleAsync(SignIn());

        var refreshed = await Auth.RefreshAsync(new RefreshRequest(signIn.Tokens.RefreshToken, TestData.DeviceA));

        refreshed.RefreshToken.ShouldNotBe(signIn.Tokens.RefreshToken);
        var tokens = await NewContext().RefreshTokens.ToListAsync();
        tokens.Count.ShouldBe(2);
        tokens.Count(t => t.RevokedAt != null).ShouldBe(1);
    }

    [Fact]
    public async Task A_rotated_refresh_token_cannot_be_used_again()
    {
        var signIn = await Auth.SignInWithGoogleAsync(SignIn());
        await Auth.RefreshAsync(new RefreshRequest(signIn.Tokens.RefreshToken, TestData.DeviceA));

        await Should.ThrowAsync<ForbiddenException>(
            () => Auth.RefreshAsync(new RefreshRequest(signIn.Tokens.RefreshToken, TestData.DeviceA)));
    }

    [Fact]
    public async Task Reusing_a_rotated_token_kills_the_whole_chain()
    {
        var signIn = await Auth.SignInWithGoogleAsync(SignIn());
        var second = await Auth.RefreshAsync(new RefreshRequest(signIn.Tokens.RefreshToken, TestData.DeviceA));

        // Replay of the old token means it leaked; the live one goes too.
        await Should.ThrowAsync<ForbiddenException>(
            () => Auth.RefreshAsync(new RefreshRequest(signIn.Tokens.RefreshToken, TestData.DeviceA)));

        await Should.ThrowAsync<ForbiddenException>(
            () => Auth.RefreshAsync(new RefreshRequest(second.RefreshToken, TestData.DeviceA)));
    }

    [Fact]
    public async Task An_expired_refresh_token_is_refused()
    {
        var signIn = await Auth.SignInWithGoogleAsync(SignIn());
        Clock.Advance(TimeSpan.FromDays(31));

        await Should.ThrowAsync<ForbiddenException>(
            () => Auth.RefreshAsync(new RefreshRequest(signIn.Tokens.RefreshToken, TestData.DeviceA)));
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_refused()
        => await Should.ThrowAsync<ForbiddenException>(
            () => Auth.RefreshAsync(new RefreshRequest("not-a-real-token", TestData.DeviceA)));

    [Fact]
    public async Task Signing_out_revokes_only_that_device()
    {
        var first = await Auth.SignInWithGoogleAsync(SignIn());
        var second = await Auth.SignInWithGoogleAsync(SignIn(deviceId: TestData.DeviceB));

        await Auth.SignOutAsync(first.Tokens.RefreshToken);

        await Should.ThrowAsync<ForbiddenException>(
            () => Auth.RefreshAsync(new RefreshRequest(first.Tokens.RefreshToken, TestData.DeviceA)));
        var stillValid = await Auth.RefreshAsync(new RefreshRequest(second.Tokens.RefreshToken, TestData.DeviceB));
        stillValid.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Signing_out_one_device_does_not_look_like_a_stolen_token()
    {
        var first = await Auth.SignInWithGoogleAsync(SignIn());
        var second = await Auth.SignInWithGoogleAsync(SignIn(deviceId: TestData.DeviceB));
        await Auth.SignOutAsync(first.Tokens.RefreshToken);

        // Presenting a deliberately signed-out token twice must not be mistaken for
        // reuse of a rotated one, or one sign-out would log the user out everywhere.
        await Should.ThrowAsync<ForbiddenException>(
            () => Auth.RefreshAsync(new RefreshRequest(first.Tokens.RefreshToken, TestData.DeviceA)));
        await Should.ThrowAsync<ForbiddenException>(
            () => Auth.RefreshAsync(new RefreshRequest(first.Tokens.RefreshToken, TestData.DeviceA)));

        var stillValid = await Auth.RefreshAsync(new RefreshRequest(second.Tokens.RefreshToken, TestData.DeviceB));
        stillValid.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Signing_out_everywhere_revokes_every_token()
    {
        var first = await Auth.SignInWithGoogleAsync(SignIn());
        var second = await Auth.SignInWithGoogleAsync(SignIn(deviceId: TestData.DeviceB));

        await Auth.SignOutAllDevicesAsync(first.User.Id);

        (await NewContext().RefreshTokens.CountAsync(t => t.RevokedAt == null)).ShouldBe(0);
    }

    [Fact]
    public async Task Signing_out_with_an_unknown_token_is_harmless()
        => await Auth.SignOutAsync("never-existed");

    [Fact]
    public async Task Signing_in_redeems_a_magic_link_pinned_to_the_same_email()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        await Invites.CreateAsync(owner.Id, group.Id,
            new CreateInviteRequest("alice@example.com", null, 1, 72));

        var result = await Auth.SignInWithGoogleAsync(SignIn());

        result.AutoJoinedGroupIds.ShouldContain(group.Id);
        (await NewContext().GroupMembers.AnyAsync(m =>
            m.GroupId == group.Id && m.UserId == result.User.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_invite_pinned_to_a_different_email_is_not_redeemed_on_sign_in()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        await Invites.CreateAsync(owner.Id, group.Id,
            new CreateInviteRequest("someone.else@example.com", null, 1, 72));

        var result = await Auth.SignInWithGoogleAsync(SignIn());

        result.AutoJoinedGroupIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reading_my_profile_returns_my_settings()
    {
        var signIn = await Auth.SignInWithGoogleAsync(SignIn());

        var me = await Auth.GetMeAsync(signIn.User.Id);

        me.Email.ShouldBe("alice@example.com");
        me.DefaultCurrency.ShouldBe("CAD");
    }

    [Fact]
    public async Task Reading_an_unknown_profile_is_a_not_found()
        => await Should.ThrowAsync<NotFoundException>(() => Auth.GetMeAsync(Guid.NewGuid()));

    [Fact]
    public async Task Updating_my_profile_changes_only_what_i_sent()
    {
        var signIn = await Auth.SignInWithGoogleAsync(SignIn());

        var updated = await Auth.UpdateProfileAsync(signIn.User.Id,
            new UpdateProfileRequest("Alice A", "EUR", true, null));

        updated.DisplayName.ShouldBe("Alice A");
        updated.DefaultCurrency.ShouldBe("EUR");
        updated.PrefersLightTheme.ShouldBeTrue();
    }

    [Fact]
    public async Task A_profile_currency_must_be_a_real_code()
    {
        var signIn = await Auth.SignInWithGoogleAsync(SignIn());

        await Should.ThrowAsync<ValidationException>(() => Auth.UpdateProfileAsync(
            signIn.User.Id, new UpdateProfileRequest(null, "EUROS", null, null)));
    }

    [Fact]
    public async Task Renaming_myself_renames_me_in_my_groups()
    {
        var signIn = await Auth.SignInWithGoogleAsync(SignIn());
        var group = await Groups.CreateAsync(signIn.User.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));

        await Auth.UpdateProfileAsync(signIn.User.Id, new UpdateProfileRequest("Alice A", null, null, null));

        (await Groups.GetAsync(signIn.User.Id, group.Id))
            .Members.Single().DisplayName.ShouldBe("Alice A");
    }

    [Fact]
    public async Task Exporting_my_data_includes_my_groups_and_expenses()
    {
        var signIn = await Auth.SignInWithGoogleAsync(SignIn());
        var group = await Groups.CreateAsync(signIn.User.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var member = group.Members.Single().Id;
        await Expenses.CreateAsync(signIn.User.Id, new SplitEverything.Application.Contracts.Expenses.CreateExpenseRequest(
            group.Id, member, "Exportable dinner", 42m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitEverything.Application.Contracts.Expenses.SplitInputDto(member, null)],
            null, null, null, null, null, null, null));

        var json = await Auth.ExportMyDataAsync(signIn.User.Id);

        json.ShouldContain("alice@example.com");
        json.ShouldContain("Roommates");
        json.ShouldContain("Exportable dinner");
    }

    [Fact]
    public async Task Deleting_my_account_removes_my_identity()
    {
        var signIn = await Auth.SignInWithGoogleAsync(SignIn());

        await Auth.DeleteMyAccountAsync(signIn.User.Id);

        (await NewContext().Users.CountAsync(u => u.Id == signIn.User.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_my_account_leaves_the_groups_balances_intact()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var signIn = await Auth.SignInWithGoogleAsync(SignIn());
        var aliceMember = TestData.Member(group.Id, signIn.User.Id, "Alice");
        Db.GroupMembers.Add(aliceMember);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var ownerMember = group.Members.First(m => m.UserId == owner.Id).Id;
        await Expenses.CreateAsync(owner.Id, new SplitEverything.Application.Contracts.Expenses.CreateExpenseRequest(
            group.Id, ownerMember, "Dinner", 100m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitEverything.Application.Contracts.Expenses.SplitInputDto(ownerMember, null),
             new SplitEverything.Application.Contracts.Expenses.SplitInputDto(aliceMember.Id, null)],
            null, null, null, null, null, null, null));

        await Auth.DeleteMyAccountAsync(signIn.User.Id);

        // Alice's row survives as a placeholder, so the owner is still owed 50.
        var balance = await Settlements.GetGroupBalanceAsync(owner.Id, group.Id);
        balance.Balances.First(b => b.MemberId == ownerMember).Net.ShouldBe(50m);
        var placeholder = await NewContext().GroupMembers.FirstAsync(m => m.Id == aliceMember.Id);
        placeholder.UserId.ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_my_account_revokes_my_tokens()
    {
        var signIn = await Auth.SignInWithGoogleAsync(SignIn());

        await Auth.DeleteMyAccountAsync(signIn.User.Id);

        (await NewContext().RefreshTokens.CountAsync()).ShouldBe(0);
    }
}
