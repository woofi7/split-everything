using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Auth;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

public class InviteServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private static readonly AuthOptions Options = new()
    {
        JwtSigningKey = "test-signing-key-that-is-long-enough-for-hmac-sha256",
        AppBaseUrl = "https://split.example.com"
    };

    private InviteService Invites { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Invites = new InviteService(Db, Writer, Activity, Email, Options, Clock);
    }

    private async Task<(Guid OwnerId, GroupDto Group)> SetupAsync(params string[] placeholders)
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, placeholders));
        return (owner.Id, group);
    }

    private static CreateInviteRequest Request(string? email = null, Guid? claims = null, int maxUses = 1, int hours = 72)
        => new(email, claims, maxUses, hours);

    [Fact]
    public async Task Creating_an_invite_returns_a_link_the_recipient_can_open()
    {
        var (ownerId, group) = await SetupAsync();

        var invite = await Invites.CreateAsync(ownerId, group.Id, Request());

        invite.Token.ShouldNotBeNullOrWhiteSpace();
        invite.Url.ShouldStartWith("https://split.example.com/join/");
        invite.Url.ShouldEndWith(invite.Token);
        invite.GroupName.ShouldBe("Roommates");
    }

    [Fact]
    public async Task The_invite_token_is_stored_only_as_a_hash()
    {
        var (ownerId, group) = await SetupAsync();

        var invite = await Invites.CreateAsync(ownerId, group.Id, Request());

        var stored = await NewContext().GroupInvites.SingleAsync();
        stored.TokenHash.ShouldNotBe(invite.Token);
        stored.TokenHash.Length.ShouldBe(64);
    }

    [Fact]
    public async Task An_invite_with_an_email_is_sent_by_mail()
    {
        var (ownerId, group) = await SetupAsync();

        await Invites.CreateAsync(ownerId, group.Id, Request(email: "bob@example.com"));

        await Email.Received(1).SendAsync("bob@example.com",
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_invite_without_an_email_sends_nothing_and_is_shared_by_link_or_qr()
    {
        var (ownerId, group) = await SetupAsync();

        await Invites.CreateAsync(ownerId, group.Id, Request());

        await Email.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_invite_expires_after_the_window_it_was_given()
    {
        var (ownerId, group) = await SetupAsync();

        var invite = await Invites.CreateAsync(ownerId, group.Id, Request(hours: 24));

        invite.ExpiresAt.ShouldBe(Clock.UtcNow.AddHours(24));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(24 * 400)]
    public async Task An_invite_window_must_be_sensible(int hours)
    {
        var (ownerId, group) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(
            () => Invites.CreateAsync(ownerId, group.Id, Request(hours: hours)));
    }

    [Fact]
    public async Task Only_a_member_can_invite()
    {
        var (_, group) = await SetupAsync();
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        await Should.ThrowAsync<ForbiddenException>(
            () => Invites.CreateAsync(stranger.Id, group.Id, Request()));
    }

    [Fact]
    public async Task An_archived_group_cannot_be_invited_into()
    {
        var (ownerId, group) = await SetupAsync();
        await Groups.ArchiveAsync(ownerId, group.Id);

        await Should.ThrowAsync<GroupArchivedException>(
            () => Invites.CreateAsync(ownerId, group.Id, Request()));
    }

    [Fact]
    public async Task Previewing_a_token_says_which_group_is_being_joined()
    {
        var (ownerId, group) = await SetupAsync("Bob");
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request());

        var preview = await Invites.PreviewAsync(invite.Token);

        preview.GroupId.ShouldBe(group.Id);
        preview.GroupName.ShouldBe("Roommates");
        preview.InvitedByName.ShouldBe("Owner");
        preview.MemberCount.ShouldBe(2);
        preview.IsRedeemable.ShouldBeTrue();
    }

    [Fact]
    public async Task Previewing_an_unknown_token_is_a_not_found()
        => await Should.ThrowAsync<NotFoundException>(() => Invites.PreviewAsync("nonsense"));

    [Fact]
    public async Task Previewing_an_expired_token_says_it_is_no_longer_redeemable()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request(hours: 1));
        Clock.Advance(TimeSpan.FromHours(2));

        (await Invites.PreviewAsync(invite.Token)).IsRedeemable.ShouldBeFalse();
    }

    [Fact]
    public async Task Redeeming_an_invite_joins_the_group()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request());
        var joiner = await TestData.SeedUserAsync(Db, "Bob");

        var result = await Invites.RedeemAsync(joiner.Id, invite.Token);

        result.GroupId.ShouldBe(group.Id);
        result.AlreadyMember.ShouldBeFalse();
        (await NewContext().GroupMembers.AnyAsync(m =>
            m.GroupId == group.Id && m.UserId == joiner.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Redeeming_an_invite_that_names_a_placeholder_claims_that_person()
    {
        var (ownerId, group) = await SetupAsync("Bob");
        var placeholder = group.Members.First(m => m.DisplayName == "Bob");
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request(claims: placeholder.Id));
        var joiner = await TestData.SeedUserAsync(Db, "Bob");

        var result = await Invites.RedeemAsync(joiner.Id, invite.Token);

        // Claiming rather than adding is what keeps the imported history attached to
        // the right person instead of splitting them in two.
        result.MemberId.ShouldBe(placeholder.Id);
        var claimed = await NewContext().GroupMembers.FirstAsync(m => m.Id == placeholder.Id);
        claimed.UserId.ShouldBe(joiner.Id);
        (await NewContext().GroupMembers.CountAsync(m => m.GroupId == group.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task A_claimed_placeholder_keeps_the_history_it_already_had()
    {
        var (ownerId, group) = await SetupAsync("Bob");
        var placeholder = group.Members.First(m => m.DisplayName == "Bob");
        var owner = group.Members.First(m => m.UserId == ownerId);
        await Expenses.CreateAsync(ownerId, new SplitEverything.Application.Contracts.Expenses.CreateExpenseRequest(
            group.Id, owner.Id, "Dinner", 100m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitEverything.Application.Contracts.Expenses.SplitInputDto(owner.Id, null),
             new SplitEverything.Application.Contracts.Expenses.SplitInputDto(placeholder.Id, null)],
            null, null, null, null, null, null));

        var invite = await Invites.CreateAsync(ownerId, group.Id, Request(claims: placeholder.Id));
        var joiner = await TestData.SeedUserAsync(Db, "Bob");
        await Invites.RedeemAsync(joiner.Id, invite.Token);

        (await Groups.GetAsync(joiner.Id, group.Id)).MyNetBalance.ShouldBe(-50m);
    }

    [Fact]
    public async Task Redeeming_twice_reports_that_you_are_already_in()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request(maxUses: 2));
        var joiner = await TestData.SeedUserAsync(Db, "Bob");
        await Invites.RedeemAsync(joiner.Id, invite.Token);

        var second = await Invites.RedeemAsync(joiner.Id, invite.Token);

        second.AlreadyMember.ShouldBeTrue();
        (await NewContext().GroupMembers.CountAsync(m => m.GroupId == group.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task A_single_use_invite_cannot_be_used_by_a_second_person()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request(maxUses: 1));
        var bob = await TestData.SeedUserAsync(Db, "Bob");
        var carol = await TestData.SeedUserAsync(Db, "Carol");
        await Invites.RedeemAsync(bob.Id, invite.Token);

        await Should.ThrowAsync<ValidationException>(() => Invites.RedeemAsync(carol.Id, invite.Token));
    }

    [Fact]
    public async Task A_multi_use_invite_lets_several_people_in()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request(maxUses: 3));
        var bob = await TestData.SeedUserAsync(Db, "Bob");
        var carol = await TestData.SeedUserAsync(Db, "Carol");

        await Invites.RedeemAsync(bob.Id, invite.Token);
        await Invites.RedeemAsync(carol.Id, invite.Token);

        (await NewContext().GroupMembers.CountAsync(m => m.GroupId == group.Id)).ShouldBe(3);
    }

    [Fact]
    public async Task An_expired_invite_cannot_be_redeemed()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request(hours: 1));
        Clock.Advance(TimeSpan.FromHours(2));
        var joiner = await TestData.SeedUserAsync(Db, "Bob");

        await Should.ThrowAsync<ValidationException>(() => Invites.RedeemAsync(joiner.Id, invite.Token));
    }

    [Fact]
    public async Task An_invite_pinned_to_an_email_refuses_anyone_else()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request(email: "bob@example.com"));
        var carol = await TestData.SeedUserAsync(Db, "Carol");

        // Otherwise a forwarded or leaked link would be as good as the invite.
        await Should.ThrowAsync<ForbiddenException>(() => Invites.RedeemAsync(carol.Id, invite.Token));
    }

    [Fact]
    public async Task An_invite_pinned_to_an_email_accepts_that_person()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request(email: "bob@example.com"));
        var bob = await TestData.SeedUserAsync(Db, "Bob");

        var result = await Invites.RedeemAsync(bob.Id, invite.Token);

        result.GroupId.ShouldBe(group.Id);
    }

    [Fact]
    public async Task A_revoked_invite_cannot_be_redeemed()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request());
        await Invites.RevokeAsync(ownerId, invite.Id);
        var joiner = await TestData.SeedUserAsync(Db, "Bob");

        await Should.ThrowAsync<ValidationException>(() => Invites.RedeemAsync(joiner.Id, invite.Token));
    }

    [Fact]
    public async Task Only_a_member_can_revoke_an_invite()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request());
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        await Should.ThrowAsync<ForbiddenException>(() => Invites.RevokeAsync(stranger.Id, invite.Id));
    }

    [Fact]
    public async Task Redeeming_an_unknown_token_is_a_not_found()
    {
        var joiner = await TestData.SeedUserAsync(Db, "Bob");

        await Should.ThrowAsync<NotFoundException>(() => Invites.RedeemAsync(joiner.Id, "nonsense"));
    }

    [Fact]
    public async Task Joining_writes_the_activity_feed()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request());
        var joiner = await TestData.SeedUserAsync(Db, "Bob");

        await Invites.RedeemAsync(joiner.Id, invite.Token);

        (await NewContext().ActivityLog.AnyAsync(a => a.Kind == ActivityKind.MemberJoined)).ShouldBeTrue();
    }

    [Fact]
    public async Task Joining_is_recorded_in_the_sync_log_so_other_devices_see_the_new_member()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request());
        var joiner = await TestData.SeedUserAsync(Db, "Bob");

        await Invites.RedeemAsync(joiner.Id, invite.Token);

        (await NewContext().SyncLog.AnyAsync(e =>
            e.GroupId == group.Id && e.EntityType == SyncEntityType.GroupMember)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_qr_code_renders_as_a_png()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request());

        var png = await Invites.RenderQrCodeAsync(ownerId, invite.Id);

        png.Length.ShouldBeGreaterThan(0);
        // PNG magic number, so this is really an image and not an error page.
        png[..4].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }

    [Fact]
    public async Task A_qr_code_can_only_be_rendered_by_a_member()
    {
        var (ownerId, group) = await SetupAsync();
        var invite = await Invites.CreateAsync(ownerId, group.Id, Request());
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        await Should.ThrowAsync<ForbiddenException>(() => Invites.RenderQrCodeAsync(stranger.Id, invite.Id));
    }

    [Fact]
    public async Task Listing_shows_the_live_invites_for_a_group()
    {
        var (ownerId, group) = await SetupAsync();
        await Invites.CreateAsync(ownerId, group.Id, Request());
        var revoked = await Invites.CreateAsync(ownerId, group.Id, Request());
        await Invites.RevokeAsync(ownerId, revoked.Id);

        var live = await Invites.ListForGroupAsync(ownerId, group.Id);

        live.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_listed_invite_does_not_leak_its_token()
    {
        var (ownerId, group) = await SetupAsync();
        await Invites.CreateAsync(ownerId, group.Id, Request());

        // Only the creation response can show the token; it is a hash from then on.
        (await Invites.ListForGroupAsync(ownerId, group.Id))
            .ShouldHaveSingleItem().Token.ShouldBeEmpty();
    }
}
