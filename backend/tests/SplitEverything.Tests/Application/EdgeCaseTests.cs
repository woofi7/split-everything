using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SplitEverything.Api.Infrastructure;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Auth;
using SplitEverything.Infrastructure.Currency;
using SplitEverything.Infrastructure.Notifications;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Infrastructure.Storage;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

/// <summary>
/// The branches the happy-path suites do not reach: filters, optional fields,
/// unusual inputs and the error paths of the outbound adapters.
/// </summary>
public class EdgeCaseTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private async Task<(Guid UserId, GroupDto Group, Guid Alice, Guid Bob)> SetupAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob"]));
        return (user.Id, group,
            group.Members.First(m => m.UserId == user.Id).Id,
            group.Members.First(m => m.DisplayName == "Bob").Id);
    }

    private Task<ExpenseDto> AddAsync(
        Guid userId, Guid groupId, Guid payer, decimal amount, string description,
        Guid? categoryId = null, DateTimeOffset? spentAt = null, params Guid[] others)
        => Expenses.CreateAsync(userId, new CreateExpenseRequest(
            groupId, payer, description, amount, "CAD", spentAt ?? TestData.Jan1, SplitType.Equal,
            others.Prepend(payer).Distinct().Select(id => new SplitInputDto(id, null)).ToList(),
            categoryId, null, null, null, null, null, null));

    [Fact]
    public async Task Expenses_can_be_filtered_by_category()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await AddAsync(userId, group.Id, alice, 10m, "Food", TestData.CategoryId("groceries"), null, bob);
        await AddAsync(userId, group.Id, alice, 20m, "Bus", TestData.CategoryId("transport"), null, bob);

        var page = await Expenses.ListAsync(userId, new ExpenseQuery(
            GroupId: group.Id, CategoryId: TestData.CategoryId("groceries")));

        page.Items.ShouldHaveSingleItem().Description.ShouldBe("Food");
    }

    [Fact]
    public async Task Expenses_can_be_filtered_by_member()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await AddAsync(userId, group.Id, alice, 10m, "Only Alice");
        await AddAsync(userId, group.Id, bob, 20m, "Bob paid", null, null, alice);

        var page = await Expenses.ListAsync(userId, new ExpenseQuery(GroupId: group.Id, MemberId: bob));

        page.Items.ShouldHaveSingleItem().Description.ShouldBe("Bob paid");
    }

    [Fact]
    public async Task Expenses_can_be_filtered_by_an_upper_date_bound()
    {
        var (userId, group, alice, _) = await SetupAsync();
        await AddAsync(userId, group.Id, alice, 10m, "Early", null, TestData.Jan1);
        await AddAsync(userId, group.Id, alice, 20m, "Late", null, TestData.Jan1.AddMonths(6));

        var page = await Expenses.ListAsync(userId, new ExpenseQuery(
            GroupId: group.Id, To: TestData.Jan1.AddMonths(1)));

        page.Items.ShouldHaveSingleItem().Description.ShouldBe("Early");
    }

    [Fact]
    public async Task An_expense_can_carry_notes_and_a_receipt()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var root = Path.Combine(Path.GetTempPath(), $"split-edge-{Guid.NewGuid():N}");
        var receipts = new ReceiptService(Db,
            new LocalDiskReceiptStorage(new ReceiptStorageOptions { RootPath = root }), Clock);
        var receipt = await receipts.UploadAsync(userId,
            new MemoryStream(Encoding.UTF8.GetBytes("bytes")), "image/png", "till.png");

        var expense = await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, alice, "With receipt", 10m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, receipt.Id, "Paid in cash",
            null, null, null));

        expense.ReceiptId.ShouldBe(receipt.Id);
        expense.Notes.ShouldBe("Paid in cash");
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Updating_an_itemized_expense_replaces_its_items()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, alice, "Restaurant", 30m, "CAD", TestData.Jan1, SplitType.Itemized,
            [new SplitInputDto(alice, null), new SplitInputDto(bob, null)], null,
            [new ExpenseItemDto(null, "Starter", 10m, 1, 0, [bob]),
             new ExpenseItemDto(null, "Main", 20m, 1, 1, [alice])],
            null, null, null, null, null));

        var updated = await Expenses.UpdateAsync(userId, expense.Id, new UpdateExpenseRequest(
            null, null, null, null, null, SplitType.Itemized,
            [new SplitInputDto(alice, null), new SplitInputDto(bob, null)], null,
            [new ExpenseItemDto(null, "Shared platter", 30m, 1, 0, [alice, bob])],
            null, null, null));

        updated.Items.ShouldHaveSingleItem().Description.ShouldBe("Shared platter");
        updated.Splits.ShouldAllBe(s => s.Amount == 15m);
    }

    [Fact]
    public async Task Updating_an_expense_can_change_its_payer_and_notes()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expense = await AddAsync(userId, group.Id, alice, 40m, "Dinner", null, null, bob);

        var updated = await Expenses.UpdateAsync(userId, expense.Id, new UpdateExpenseRequest(
            bob, null, null, null, TestData.Jan1.AddDays(2), null, null,
            TestData.CategoryId("dining"), null, null, "Bob actually paid", null));

        updated.PaidByMemberId.ShouldBe(bob);
        updated.Notes.ShouldBe("Bob actually paid");
        updated.CategoryKey.ShouldBe("dining");
    }

    [Fact]
    public async Task Updating_an_expense_currency_reconverts_it()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await AddAsync(userId, group.Id, alice, 100m, "Hotel");
        Currency.ConvertAsync(100m, "EUR", "CAD", Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SplitEverything.Application.Abstractions.ConversionResult(
                148m, 1.48m, Clock.UtcNow)));

        var updated = await Expenses.UpdateAsync(userId, expense.Id, new UpdateExpenseRequest(
            null, null, null, "EUR", null, null, null, null, null, null, null, null));

        updated.AmountInBaseCurrency.ShouldBe(148m);
    }

    [Fact]
    public async Task Updating_an_expense_to_a_non_positive_amount_is_rejected()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await AddAsync(userId, group.Id, alice, 40m, "Dinner");

        await Should.ThrowAsync<ValidationException>(() => Expenses.UpdateAsync(
            userId, expense.Id, new UpdateExpenseRequest(
                null, null, -5m, null, null, null, null, null, null, null, null, null)));
    }

    [Fact]
    public async Task An_admin_can_delete_someone_elses_comment()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var member = await TestData.SeedUserAsync(Db, "Member");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        Db.GroupMembers.Add(TestData.Member(group.Id, member.Id, "Member"));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var payer = group.Members.Single().Id;
        var expense = await AddAsync(owner.Id, group.Id, payer, 40m, "Dinner");
        var comment = await Expenses.AddCommentAsync(member.Id,
            new CreateCommentRequest(expense.Id, "Theirs", null, null));

        // Moderation has to be possible, or an owner cannot clean up their own group.
        await Expenses.DeleteCommentAsync(owner.Id, comment.Id);

        (await Expenses.GetCommentsAsync(owner.Id, expense.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_reply_to_a_reply_attaches_to_the_top_level_comment()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await AddAsync(userId, group.Id, alice, 40m, "Dinner");
        var parent = await Expenses.AddCommentAsync(userId,
            new CreateCommentRequest(expense.Id, "Question", null, null));
        var reply = await Expenses.AddCommentAsync(userId,
            new CreateCommentRequest(expense.Id, "Answer", parent.Id, null));

        var nested = await Expenses.AddCommentAsync(userId,
            new CreateCommentRequest(expense.Id, "Follow-up", reply.Id, null));

        // Threading is deliberately one level deep, so it flattens rather than nests.
        nested.ParentCommentId.ShouldBe(parent.Id);
    }

    [Fact]
    public async Task Replaying_a_comment_with_the_same_client_id_is_idempotent()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await AddAsync(userId, group.Id, alice, 40m, "Dinner");
        var clientId = Guid.CreateVersion7();
        var request = new CreateCommentRequest(expense.Id, "Once", null, clientId);

        var first = await Expenses.AddCommentAsync(userId, request);
        var second = await Expenses.AddCommentAsync(userId, request);

        second.Id.ShouldBe(first.Id);
        (await Expenses.GetCommentsAsync(userId, expense.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_comment_on_a_parent_from_another_expense_is_a_not_found()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var first = await AddAsync(userId, group.Id, alice, 40m, "First");
        var second = await AddAsync(userId, group.Id, alice, 20m, "Second");
        var parent = await Expenses.AddCommentAsync(userId,
            new CreateCommentRequest(first.Id, "On first", null, null));

        await Should.ThrowAsync<NotFoundException>(() => Expenses.AddCommentAsync(
            userId, new CreateCommentRequest(second.Id, "Mismatched", parent.Id, null)));
    }

    [Fact]
    public async Task Every_group_field_can_be_updated_at_once()
    {
        var (userId, group, _, _) = await SetupAsync();

        var updated = await Groups.UpdateAsync(userId, group.Id, new UpdateGroupRequest(
            "Renamed", "A description", "house", "#ff0000", "eur"));

        updated.Name.ShouldBe("Renamed");
        updated.Description.ShouldBe("A description");
        updated.IconName.ShouldBe("house");
        updated.ColorHex.ShouldBe("#ff0000");
        updated.BaseCurrency.ShouldBe("EUR");
    }

    [Fact]
    public async Task A_group_can_be_created_with_an_icon_and_colour()
    {
        var user = await TestData.SeedUserAsync(Db);

        var group = await Groups.CreateAsync(user.Id, new CreateGroupRequest(
            "Trip", "cad", "Ski week", "mountain", "#00ff00", ["Bob", "bob", "  "]));

        group.IconName.ShouldBe("mountain");
        group.ColorHex.ShouldBe("#00ff00");
        // Duplicate and blank placeholder names are dropped rather than creating junk.
        group.Members.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Acknowledging_from_a_device_registered_to_someone_else_is_forbidden()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var other = await TestData.SeedUserAsync(Db, "Other");
        var sync = new SyncService(Db, Writer, Broadcaster, Clock, Activity);
        await sync.AcknowledgeAsync(owner.Id, TestData.DeviceA, new Dictionary<Guid, long>());

        await Should.ThrowAsync<ForbiddenException>(() => sync.AcknowledgeAsync(
            other.Id, TestData.DeviceA, new Dictionary<Guid, long>()));
    }

    [Fact]
    public async Task Acknowledging_without_a_device_id_is_rejected()
    {
        var user = await TestData.SeedUserAsync(Db);
        var sync = new SyncService(Db, Writer, Broadcaster, Clock, Activity);

        await Should.ThrowAsync<ValidationException>(() => sync.AcknowledgeAsync(
            user.Id, "  ", new Dictionary<Guid, long>()));
    }

    [Fact]
    public async Task Resolving_a_conflict_that_does_not_exist_is_a_not_found()
    {
        var user = await TestData.SeedUserAsync(Db);
        var sync = new SyncService(Db, Writer, Broadcaster, Clock, Activity);

        await Should.ThrowAsync<NotFoundException>(() => sync.ResolveConflictAsync(user.Id,
            new SplitEverything.Application.Contracts.Sync.ResolveConflictRequest(
                Guid.NewGuid(), ConflictResolution.KeepLocal, null)));
    }

    [Fact]
    public async Task Conflicts_of_a_group_you_are_not_in_are_forbidden()
    {
        var (_, group, _, _) = await SetupAsync();
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");
        var sync = new SyncService(Db, Writer, Broadcaster, Clock, Activity);

        await Should.ThrowAsync<ForbiddenException>(
            () => sync.GetOpenConflictsAsync(stranger.Id, group.Id));
    }

    [Fact]
    public async Task An_invite_can_be_found_by_its_id_as_well_as_its_token()
    {
        var (userId, group, _, _) = await SetupAsync();
        var invites = new InviteService(Db, Writer, Activity, Email,
            new AuthOptions { AppBaseUrl = "https://split.test" }, Clock);
        var invite = await invites.CreateAsync(userId, group.Id, new CreateInviteRequest(null, null, 1, 72));

        // The QR form carries the id, since the plaintext token is never stored.
        var preview = await invites.PreviewAsync(invite.Id.ToString("N"));

        preview.GroupId.ShouldBe(group.Id);
    }

    [Fact]
    public async Task An_invite_cannot_claim_a_member_who_already_signed_in()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var member = await TestData.SeedUserAsync(Db, "Member");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var claimed = TestData.Member(group.Id, member.Id, "Member");
        Db.GroupMembers.Add(claimed);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var invites = new InviteService(Db, Writer, Activity, Email,
            new AuthOptions { AppBaseUrl = "https://split.test" }, Clock);

        await Should.ThrowAsync<ValidationException>(() => invites.CreateAsync(
            owner.Id, group.Id, new CreateInviteRequest(null, claimed.Id, 1, 72)));
    }

    [Fact]
    public async Task An_invite_can_only_name_a_member_of_its_own_group()
    {
        var (userId, group, _, _) = await SetupAsync();
        var invites = new InviteService(Db, Writer, Activity, Email,
            new AuthOptions { AppBaseUrl = "https://split.test" }, Clock);

        await Should.ThrowAsync<NotFoundException>(() => invites.CreateAsync(
            userId, group.Id, new CreateInviteRequest(null, Guid.NewGuid(), 1, 72)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task An_invite_use_count_must_be_sensible(int maxUses)
    {
        var (userId, group, _, _) = await SetupAsync();
        var invites = new InviteService(Db, Writer, Activity, Email,
            new AuthOptions { AppBaseUrl = "https://split.test" }, Clock);

        await Should.ThrowAsync<ValidationException>(() => invites.CreateAsync(
            userId, group.Id, new CreateInviteRequest(null, null, maxUses, 72)));
    }

    [Fact]
    public async Task A_removed_member_who_redeems_an_invite_is_reactivated()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var member = await TestData.SeedUserAsync(Db, "Member");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var membership = TestData.Member(group.Id, member.Id, "Member");
        Db.GroupMembers.Add(membership);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        await Groups.RemoveMemberAsync(owner.Id, group.Id, membership.Id);

        var invites = new InviteService(Db, Writer, Activity, Email,
            new AuthOptions { AppBaseUrl = "https://split.test" }, Clock);
        var invite = await invites.CreateAsync(owner.Id, group.Id, new CreateInviteRequest(null, null, 1, 72));

        var result = await invites.RedeemAsync(member.Id, invite.Token);

        result.AlreadyMember.ShouldBeTrue();
        (await NewContext().GroupMembers.FirstAsync(m => m.Id == membership.Id))
            .Status.ShouldBe(MembershipStatus.Active);
    }

    [Fact]
    public async Task Storage_maps_each_content_type_to_a_sensible_extension()
    {
        var root = Path.Combine(Path.GetTempPath(), $"split-ext-{Guid.NewGuid():N}");
        var storage = new LocalDiskReceiptStorage(new ReceiptStorageOptions { RootPath = root });

        foreach (var (contentType, extension) in new[]
                 {
                     ("image/jpeg", ".jpg"), ("image/png", ".png"), ("image/webp", ".webp"),
                     ("image/heic", ".heic"), ("application/pdf", ".pdf")
                 })
        {
            var stored = await storage.SaveAsync(
                new MemoryStream(Encoding.UTF8.GetBytes($"bytes-{extension}")), contentType, "file");
            stored.StorageKey.ShouldEndWith(extension);
        }

        var unknown = await storage.SaveAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("odd")), "application/octet-stream", "thing.dat");
        unknown.StorageKey.ShouldEndWith(".dat");

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Storage_refuses_an_empty_blob()
    {
        var root = Path.Combine(Path.GetTempPath(), $"split-empty-{Guid.NewGuid():N}");
        var storage = new LocalDiskReceiptStorage(new ReceiptStorageOptions { RootPath = root });

        await Should.ThrowAsync<ValidationException>(
            () => storage.SaveAsync(new MemoryStream(), "image/jpeg", "empty.jpg"));

        Directory.Delete(root, recursive: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Storage_refuses_a_blank_key(string key)
    {
        var root = Path.Combine(Path.GetTempPath(), $"split-blank-{Guid.NewGuid():N}");
        var storage = new LocalDiskReceiptStorage(new ReceiptStorageOptions { RootPath = root });

        await Should.ThrowAsync<ValidationException>(() => storage.ExistsAsync(key));

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task An_oversized_non_seekable_upload_is_refused_after_buffering()
    {
        var root = Path.Combine(Path.GetTempPath(), $"split-big-{Guid.NewGuid():N}");
        var user = await TestData.SeedUserAsync(Db);
        var receipts = new ReceiptService(Db,
            new LocalDiskReceiptStorage(new ReceiptStorageOptions { RootPath = root }), Clock);

        // A request stream does not report its length, so the size check can only
        // happen after the bytes have been read.
        var stream = new NonSeekableStream(new byte[ReceiptService.MaxBytes + 16]);

        await Should.ThrowAsync<ValidationException>(
            () => receipts.UploadAsync(user.Id, stream, "image/jpeg", "huge.jpg"));

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Refreshing_rates_survives_a_failing_pair()
    {
        var handler = new StubHttpHandler(string.Empty, HttpStatusCode.ServiceUnavailable);
        var converter = new FrankfurterCurrencyConverter(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev/") },
            Db, Clock, NullLogger<FrankfurterCurrencyConverter>.Instance);

        // Best effort: a failed warm-up must not throw, since the on-demand path
        // still works.
        await converter.RefreshCacheAsync(["CAD", "EUR"]);
    }

    [Fact]
    public async Task Refreshing_a_single_currency_does_nothing()
    {
        var handler = new StubHttpHandler("{}");
        var converter = new FrankfurterCurrencyConverter(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev/") },
            Db, Clock, NullLogger<FrankfurterCurrencyConverter>.Instance);

        await converter.RefreshCacheAsync(["CAD"]);

        handler.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_rate_lookup_between_the_same_currency_is_one()
    {
        var converter = new FrankfurterCurrencyConverter(
            new HttpClient(new StubHttpHandler("{}")) { BaseAddress = new Uri("https://api.frankfurter.dev/") },
            Db, Clock, NullLogger<FrankfurterCurrencyConverter>.Instance);

        (await converter.GetRateAsync("CAD", "cad")).ShouldBe(1m);
    }

    [Fact]
    public async Task A_response_that_is_not_json_is_an_error()
    {
        var handler = new StubHttpHandler("<html>gateway error</html>");
        var converter = new FrankfurterCurrencyConverter(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev/") },
            Db, Clock, NullLogger<FrankfurterCurrencyConverter>.Instance);

        await Should.ThrowAsync<AppException>(() => converter.ConvertAsync(10m, "EUR", "CAD"));
    }

    [Fact]
    public async Task The_fcm_token_provider_surfaces_an_unusable_service_account()
    {
        var provider = new FcmAccessTokenProvider(
            new PushOptions { FcmServiceAccountJson = "{not-valid-json" }, Clock);

        await Should.ThrowAsync<Exception>(() => provider.GetAsync());
    }

    [Fact]
    public void The_current_user_reads_the_device_from_the_token_when_no_header_is_sent()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity([
                        new System.Security.Claims.Claim("sub", Guid.NewGuid().ToString()),
                        new System.Security.Claims.Claim("device", "device-from-token")
                    ], "test"))
            }
        };

        new CurrentUserAccessor(accessor).DeviceId.ShouldBe("device-from-token");
    }

    [Fact]
    public void The_current_user_prefers_the_device_header()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CurrentUserAccessor.DeviceHeader] = "device-from-header";
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity([
                new System.Security.Claims.Claim("device", "device-from-token")
            ], "test"));

        new CurrentUserAccessor(new HttpContextAccessor { HttpContext = context })
            .DeviceId.ShouldBe("device-from-header");
    }

    [Fact]
    public void An_anonymous_caller_has_no_identity()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var currentUser = new CurrentUserAccessor(accessor);

        currentUser.UserId.ShouldBeNull();
        currentUser.Email.ShouldBeNull();
        currentUser.IsAuthenticated.ShouldBeFalse();
        Should.Throw<ForbiddenException>(() => currentUser.RequireUserId());
    }

    [Fact]
    public void A_caller_with_no_http_context_has_no_identity()
        => new CurrentUserAccessor(new HttpContextAccessor()).IsAuthenticated.ShouldBeFalse();

    [Fact]
    public async Task A_receipt_attached_only_to_a_settlement_is_readable_by_the_group()
    {
        var root = Path.Combine(Path.GetTempPath(), $"split-settle-{Guid.NewGuid():N}");
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var member = await TestData.SeedUserAsync(Db, "Member");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var memberRow = TestData.Member(group.Id, member.Id, "Member");
        Db.GroupMembers.Add(memberRow);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var receipts = new ReceiptService(Db,
            new LocalDiskReceiptStorage(new ReceiptStorageOptions { RootPath = root }), Clock);
        var receipt = await receipts.UploadAsync(owner.Id,
            new MemoryStream(Encoding.UTF8.GetBytes("transfer proof")), "image/png", "proof.png");

        await Settlements.CreateAsync(owner.Id,
            new SplitEverything.Application.Contracts.Settlements.CreateSettlementRequest(
                group.Id, memberRow.Id, group.Members.Single().Id, 20m, "CAD",
                TestData.Jan1, null, receipt.Id, null));

        var download = await receipts.DownloadAsync(member.Id, receipt.Id);

        download.ContentType.ShouldBe("image/png");
        Directory.Delete(root, recursive: true);
    }

    /// <summary>Mimics a request body: readable once, with no length.</summary>
    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
