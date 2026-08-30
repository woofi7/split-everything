using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Auth;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Auth;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Application;

/// <summary>
/// A colour per member, stored on the group.
///
/// Derived from the member id before, which meant every screen computed it from
/// whatever list it happened to have, and they disagreed. Stored, a group can also
/// change it, and a person can say which colour they would like: a wish rather
/// than a guarantee, because two people the same colour in one group defeats the
/// point of having colours at all.
/// </summary>
public class MemberColorTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private static readonly string First = MemberPalette.Colors[0];
    private static readonly string Second = MemberPalette.Colors[1];

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
    public void The_palette_gives_out_a_preferred_colour_that_is_free()
        => MemberPalette.Assign(Second, [First]).ShouldBe(Second);

    [Fact]
    public void The_palette_gives_out_the_first_free_colour_when_the_wish_is_taken()
        => MemberPalette.Assign(First, [First]).ShouldBe(Second);

    [Fact]
    public void The_palette_ignores_a_colour_it_does_not_know()
        // Anything else and a group could store a value nothing knows how to draw.
        => MemberPalette.Assign("#123456", []).ShouldBe(First);

    [Fact]
    public void The_palette_matches_a_colour_whatever_its_case()
    {
        MemberPalette.Assign(Second.ToUpperInvariant(), []).ShouldBe(Second);
        MemberPalette.Assign(null, [Second.ToUpperInvariant()]).ShouldBe(First);
    }

    [Fact]
    public void The_palette_repeats_rather_than_refusing_when_it_runs_out()
    {
        // A group can hold more people than there are colours, and a member with no
        // colour at all would be worse than a repeat.
        var everything = MemberPalette.Colors.ToList();

        MemberPalette.Colors.ShouldContain(MemberPalette.Assign(null, everything));
        // Their own wish, so at least the repeat is the one they asked for.
        MemberPalette.Assign(Second, everything).ShouldBe(Second);
    }

    [Fact]
    public async Task A_new_group_gives_everyone_a_colour_of_their_own()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob", "Chloe"]));

        var colours = group.Members.Select(m => m.ColorHex).ToList();
        colours.ShouldAllBe(colour => colour != null);
        colours.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task The_owner_gets_the_colour_they_asked_for()
    {
        var user = await TestData.SeedUserAsync(Db);
        await Auth.UpdateProfileAsync(user.Id,
            new UpdateProfileRequest(null, null, null, null, Second));

        var group = await Groups.CreateAsync(user.Id, new CreateGroupRequest(
            "Roommates", "CAD", null, null, null, null));

        group.Members.Single().ColorHex.ShouldBe(Second);
    }

    [Fact]
    public async Task Someone_joining_gets_the_colour_they_asked_for_when_it_is_free()
    {
        var owner = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));

        var joiner = await TestData.SeedUserAsync(Db, "Bob", "bob@example.com", "google-bob");
        var free = MemberPalette.Colors.First(colour => colour != group.Members.Single().ColorHex);
        await Auth.UpdateProfileAsync(joiner.Id,
            new UpdateProfileRequest(null, null, null, null, free));

        var added = await Groups.AddUserMemberAsync(owner.Id, group.Id,
            new AddUserMemberRequest(joiner.Id));

        added.ColorHex.ShouldBe(free);
    }

    [Fact]
    public async Task Someone_joining_gets_another_colour_when_theirs_is_taken()
    {
        var owner = await TestData.SeedUserAsync(Db);
        await Auth.UpdateProfileAsync(owner.Id,
            new UpdateProfileRequest(null, null, null, null, First));
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));

        var joiner = await TestData.SeedUserAsync(Db, "Bob", "bob@example.com", "google-bob");
        await Auth.UpdateProfileAsync(joiner.Id,
            new UpdateProfileRequest(null, null, null, null, First));

        var added = await Groups.AddUserMemberAsync(owner.Id, group.Id,
            new AddUserMemberRequest(joiner.Id));

        // The wish loses to the group, because two the same defeats the purpose.
        added.ColorHex.ShouldNotBe(First);
        added.ColorHex.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_profile_refuses_a_colour_outside_the_palette()
    {
        var user = await TestData.SeedUserAsync(Db);

        await Should.ThrowAsync<ValidationException>(() => Auth.UpdateProfileAsync(
            user.Id, new UpdateProfileRequest(null, null, null, null, "#abcdef")));
    }

    [Fact]
    public async Task A_profile_colour_can_be_cleared()
    {
        var user = await TestData.SeedUserAsync(Db);
        await Auth.UpdateProfileAsync(user.Id, new UpdateProfileRequest(null, null, null, null, Second));

        var cleared = await Auth.UpdateProfileAsync(user.Id,
            new UpdateProfileRequest(null, null, null, null, ""));

        cleared.PreferredColorHex.ShouldBeNull();
    }

    [Fact]
    public async Task A_member_can_change_their_own_colour()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var mine = group.Members.Single().Id;
        var wanted = MemberPalette.Colors.First(colour => colour != group.Members.Single().ColorHex);

        var updated = await Groups.SetMemberColorAsync(user.Id, group.Id, mine,
            new SetMemberColorRequest(wanted));

        updated.ColorHex.ShouldBe(wanted);
    }

    [Fact]
    public async Task Taking_a_colour_someone_else_has_swaps_the_two()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob"]));

        var me = group.Members.First(m => m.UserId == user.Id);
        var bob = group.Members.First(m => m.DisplayName == "Bob");

        await Groups.SetMemberColorAsync(user.Id, group.Id, me.Id,
            new SetMemberColorRequest(bob.ColorHex!));

        // Swapped rather than refused, so everybody still has one of their own.
        var after = await Groups.GetAsync(user.Id, group.Id);
        after.Members.First(m => m.Id == me.Id).ColorHex.ShouldBe(bob.ColorHex);
        after.Members.First(m => m.Id == bob.Id).ColorHex.ShouldBe(me.ColorHex);
        after.Members.Select(m => m.ColorHex).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_plain_member_cannot_change_someone_else_colour()
    {
        var owner = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob"]));
        var bob = group.Members.First(m => m.DisplayName == "Bob");

        var other = await TestData.SeedUserAsync(Db, "Mallory", "mallory@example.com", "google-mallory");
        var fresh = NewContext();
        fresh.GroupMembers.Add(TestData.Member(group.Id, other.Id, "Mallory"));
        await fresh.SaveChangesAsync();

        // It changes what everybody in the group sees.
        await Should.ThrowAsync<ForbiddenException>(() => Groups.SetMemberColorAsync(
            other.Id, group.Id, bob.Id, new SetMemberColorRequest(First)));
    }

    [Fact]
    public async Task A_colour_change_is_offered_to_the_other_devices()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var mine = group.Members.Single();
        var wanted = MemberPalette.Colors.First(colour => colour != mine.ColorHex);

        var before = await NewContext().SyncLog.CountAsync(e => e.EntityId == mine.Id);
        await Groups.SetMemberColorAsync(user.Id, group.Id, mine.Id, new SetMemberColorRequest(wanted));

        // Otherwise the other phone keeps drawing the old colour for good.
        var after = await NewContext().SyncLog.CountAsync(e => e.EntityId == mine.Id);
        after.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task A_colour_outside_the_palette_is_refused()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));

        await Should.ThrowAsync<ValidationException>(() => Groups.SetMemberColorAsync(
            user.Id, group.Id, group.Members.Single().Id, new SetMemberColorRequest("#abcdef")));
    }
}
