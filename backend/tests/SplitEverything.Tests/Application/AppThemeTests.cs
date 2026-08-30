using NSubstitute;
using Shouldly;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Auth;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Auth;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

/// <summary>
/// The accent colour the whole application wears.
///
/// A name rather than a colour, because a theme is three shades and the client is
/// what knows them. On the account rather than on the device: someone who picks a
/// colour means it wherever they sign in.
/// </summary>
public class AppThemeTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
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
            Clock);
    }

    [Fact]
    public async Task A_new_account_has_no_theme_of_its_own()
    {
        var user = await TestData.SeedUserAsync(Db);

        var me = await Auth.GetMeAsync(user.Id);

        // Nothing said, which the client reads as the default rather than as an
        // absence it has to handle.
        me.ThemeName.ShouldBeNull();
    }

    [Fact]
    public async Task A_theme_can_be_chosen()
    {
        var user = await TestData.SeedUserAsync(Db);

        var updated = await Auth.UpdateProfileAsync(user.Id,
            new UpdateProfileRequest(null, null, null, ThemeName: "teal"));

        updated.ThemeName.ShouldBe("teal");
        (await Auth.GetMeAsync(user.Id)).ThemeName.ShouldBe("teal");
    }

    [Fact]
    public async Task A_theme_is_stored_the_way_this_app_spells_it()
    {
        var user = await TestData.SeedUserAsync(Db);

        var updated = await Auth.UpdateProfileAsync(user.Id,
            new UpdateProfileRequest(null, null, null, ThemeName: "  Rose "));

        // Case and whitespace must not fork the value, or a client comparing names
        // would not recognise its own choice.
        updated.ThemeName.ShouldBe("rose");
    }

    [Fact]
    public async Task A_theme_outside_the_eight_is_refused()
    {
        var user = await TestData.SeedUserAsync(Db);

        // Anything else would be a colour the client cannot draw.
        await Should.ThrowAsync<ValidationException>(() => Auth.UpdateProfileAsync(
            user.Id, new UpdateProfileRequest(null, null, null, ThemeName: "chartreuse")));
    }

    [Fact]
    public async Task A_theme_can_be_put_back_to_the_default()
    {
        var user = await TestData.SeedUserAsync(Db);
        await Auth.UpdateProfileAsync(user.Id,
            new UpdateProfileRequest(null, null, null, ThemeName: "amber"));

        var cleared = await Auth.UpdateProfileAsync(user.Id,
            new UpdateProfileRequest(null, null, null, ThemeName: ""));

        // Empty clears it, as with every other clearable field on this API.
        cleared.ThemeName.ShouldBeNull();
    }

    [Fact]
    public async Task Saying_nothing_about_the_theme_leaves_it_alone()
    {
        var user = await TestData.SeedUserAsync(Db);
        await Auth.UpdateProfileAsync(user.Id,
            new UpdateProfileRequest(null, null, null, ThemeName: "sky"));

        var renamed = await Auth.UpdateProfileAsync(user.Id,
            new UpdateProfileRequest("Alice A", null, null, null));

        renamed.ThemeName.ShouldBe("sky");
    }

    [Fact]
    public void The_themes_are_the_eight_the_client_offers()
    {
        // themes.ts, in the same order. A name one side has and the other refuses
        // is a colour that cannot be saved.
        AppThemes.Names.ShouldBe(new[]
        {
            "indigo", "violet", "sky", "teal", "green", "amber", "rose", "slate"
        });
        AppThemes.Default.ShouldBe("indigo");
    }

    [Fact]
    public void An_unknown_theme_is_not_known()
    {
        AppThemes.IsKnown("indigo").ShouldBeTrue();
        AppThemes.IsKnown("INDIGO").ShouldBeTrue();
        AppThemes.IsKnown("chartreuse").ShouldBeFalse();
        AppThemes.IsKnown(null).ShouldBeFalse();
    }

    [Fact]
    public async Task A_new_account_reads_the_app_in_english()
    {
        var user = await TestData.SeedUserAsync(Db);

        (await Auth.GetMeAsync(user.Id)).Locale.ShouldBe("en");
    }

    [Fact]
    public async Task A_language_can_be_chosen()
    {
        var user = await TestData.SeedUserAsync(Db);

        var updated = await Auth.UpdateProfileAsync(user.Id,
            new UpdateProfileRequest(null, null, null, Locale: "fr"));

        updated.Locale.ShouldBe("fr");
        (await Auth.GetMeAsync(user.Id)).Locale.ShouldBe("fr");
    }

    [Fact]
    public async Task A_regional_language_is_taken_for_its_language()
    {
        var user = await TestData.SeedUserAsync(Db);

        // A browser offering fr-CA means French, and refusing it would be pedantry.
        var updated = await Auth.UpdateProfileAsync(user.Id,
            new UpdateProfileRequest(null, null, null, Locale: "fr-CA"));

        updated.Locale.ShouldBe("fr");
    }

    [Fact]
    public async Task A_language_the_app_is_not_written_in_is_refused()
    {
        var user = await TestData.SeedUserAsync(Db);

        // Half a screen in a language nobody asked for has no way out of it.
        await Should.ThrowAsync<ValidationException>(() => Auth.UpdateProfileAsync(
            user.Id, new UpdateProfileRequest(null, null, null, Locale: "de")));
    }

    [Fact]
    public async Task A_language_can_be_put_back_to_the_default()
    {
        var user = await TestData.SeedUserAsync(Db);
        await Auth.UpdateProfileAsync(user.Id, new UpdateProfileRequest(null, null, null, Locale: "fr"));

        var cleared = await Auth.UpdateProfileAsync(user.Id,
            new UpdateProfileRequest(null, null, null, Locale: ""));

        cleared.Locale.ShouldBe("en");
    }

    [Fact]
    public void The_languages_are_the_two_the_app_is_written_in()
    {
        AppLocales.Tags.ShouldBe(new[] { "en", "fr" });
        AppLocales.Default.ShouldBe("en");
        AppLocales.IsKnown("FR").ShouldBeTrue();
        AppLocales.IsKnown("de").ShouldBeFalse();
        AppLocales.IsKnown(null).ShouldBeFalse();
        AppLocales.Resolve("fr_CA").ShouldBe("fr");
        AppLocales.Resolve("de").ShouldBeNull();
        // Reading a stored value never leaves a screen with no language at all.
        AppLocales.Normalize("es").ShouldBe("en");
    }
}
