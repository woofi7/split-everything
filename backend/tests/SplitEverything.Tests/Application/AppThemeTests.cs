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
            Admins, Clock);
    }

    [Fact]
    public async Task A_new_account_has_no_theme_of_its_own()
    {
        var user = await TestData.SeedUserAsync(Db);

        var me = await Auth.GetMeAsync(user.Id);

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

        updated.ThemeName.ShouldBe("rose");
    }

    [Fact]
    public async Task A_theme_outside_the_eight_is_refused()
    {
        var user = await TestData.SeedUserAsync(Db);

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

        var updated = await Auth.UpdateProfileAsync(user.Id,
            new UpdateProfileRequest(null, null, null, Locale: "fr-CA"));

        updated.Locale.ShouldBe("fr");
    }

    [Fact]
    public async Task A_language_the_app_is_not_written_in_is_refused()
    {
        var user = await TestData.SeedUserAsync(Db);

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
        AppLocales.Normalize("es").ShouldBe("en");
    }
}
