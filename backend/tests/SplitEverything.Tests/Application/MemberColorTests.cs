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
            Admins, Clock);
    }

    [Fact]
    public void The_palette_gives_out_the_first_free_colour()
        => MemberPalette.Assign([First]).ShouldBe(Second);

    [Fact]
    public void The_palette_matches_a_taken_colour_whatever_its_case()
        => MemberPalette.Assign([Second.ToUpperInvariant()]).ShouldBe(First);

    [Fact]
    public void The_palette_repeats_rather_than_refusing_when_it_runs_out()
    {
        var everything = MemberPalette.Colors.ToList();

        MemberPalette.Colors.ShouldContain(MemberPalette.Assign(everything));
    }

    [Fact]
    public void The_palette_knows_its_own_colours()
    {
        MemberPalette.IsKnown(First).ShouldBeTrue();
        MemberPalette.IsKnown(First.ToUpperInvariant()).ShouldBeTrue();
        MemberPalette.IsKnown("#123456").ShouldBeFalse();
        MemberPalette.IsKnown(null).ShouldBeFalse();
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
    public async Task Someone_joining_gets_a_colour_nobody_there_has()
    {
        var owner = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var taken = group.Members.Single().ColorHex;

        var joiner = await TestData.SeedUserAsync(Db, "Bob", "bob@example.com", "google-bob");
        var added = await Groups.AddUserMemberAsync(owner.Id, group.Id,
            new AddUserMemberRequest(joiner.Id));

        added.ColorHex.ShouldNotBeNull();
        added.ColorHex.ShouldNotBe(taken);
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
