using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Notifications;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Currency;
using SplitEverything.Infrastructure.Notifications;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Infrastructure.Storage;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

public class RecurringExpenseServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private RecurringExpenseService Recurring { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Recurring = new RecurringExpenseService(Db, Writer, Activity, Currency, Push, Clock);
    }

    private async Task<(Guid UserId, GroupDto Group, Guid Alice, Guid Bob)> SetupAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob"]));
        return (user.Id, group,
            group.Members.First(m => m.UserId == user.Id).Id,
            group.Members.First(m => m.DisplayName == "Bob").Id);
    }

    private static CreateRecurringExpenseRequest Rule(
        Guid groupId, Guid payer, IReadOnlyList<Guid> participants,
        decimal amount = 1200m, RecurrenceUnit unit = RecurrenceUnit.Month,
        int interval = 1, int? dayOfMonth = 1, DateTimeOffset? startsOn = null,
        DateTimeOffset? endsOn = null, int? maxOccurrences = null)
        => new(groupId, payer, "Rent", amount, "CAD", SplitType.Equal,
            participants.Select(p => new SplitInputDto(p, null)).ToList(),
            unit, interval, dayOfMonth, null,
            startsOn ?? TestData.Jan1, endsOn, maxOccurrences);

    [Fact]
    public async Task A_rule_can_be_created_and_read_back()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        var rule = await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob]));

        rule.Description.ShouldBe("Rent");
        rule.Amount.ShouldBe(1200m);
        rule.Unit.ShouldBe(RecurrenceUnit.Month);
        (await Recurring.ListAsync(userId, group.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_rule_needs_a_positive_amount()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(
            () => Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob], amount: 0m)));
    }

    [Fact]
    public async Task A_rule_needs_participants_who_are_members()
    {
        var (userId, group, alice, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(
            () => Recurring.CreateAsync(userId, Rule(group.Id, alice, [Guid.NewGuid()])));
    }

    [Fact]
    public async Task Running_the_worker_creates_the_occurrence_that_is_due()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob]));

        var created = await Recurring.RunDueAsync(TestData.Jan1);

        created.ShouldBe(1);
        var expense = await NewContext().Expenses.Include(e => e.Splits).SingleAsync();
        expense.Description.ShouldBe("Rent");
        expense.Amount.ShouldBe(1200m);
        expense.Splits.Count.ShouldBe(2);
        expense.RecurringExpenseId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Running_the_worker_twice_does_not_double_charge()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob]));

        await Recurring.RunDueAsync(TestData.Jan1);
        var second = await Recurring.RunDueAsync(TestData.Jan1);

        second.ShouldBe(0);
        (await NewContext().Expenses.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_rule_not_yet_due_creates_nothing()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob],
            startsOn: TestData.Jan1.AddMonths(2)));

        (await Recurring.RunDueAsync(TestData.Jan1)).ShouldBe(0);
    }

    [Fact]
    public async Task The_worker_backfills_the_occurrences_it_missed_while_down()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob]));

        // Nothing ran between January and April.
        var created = await Recurring.RunDueAsync(TestData.Jan1.AddMonths(3));

        created.ShouldBe(4);
        var months = await NewContext().Expenses.Select(e => e.SpentAt.Month).ToListAsync();
        months.OrderBy(m => m).ShouldBe(new[] { 1, 2, 3, 4 });
    }

    [Fact]
    public async Task A_paused_rule_creates_nothing()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var rule = await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob]));
        await Recurring.SetPausedAsync(userId, rule.Id, true);

        (await Recurring.RunDueAsync(TestData.Jan1.AddMonths(3))).ShouldBe(0);
    }

    [Fact]
    public async Task A_resumed_rule_starts_creating_again()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var rule = await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob]));
        await Recurring.SetPausedAsync(userId, rule.Id, true);
        await Recurring.SetPausedAsync(userId, rule.Id, false);

        (await Recurring.RunDueAsync(TestData.Jan1)).ShouldBe(1);
    }

    [Fact]
    public async Task A_rule_stops_at_its_end_date()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob],
            endsOn: TestData.Jan1.AddMonths(1)));

        (await Recurring.RunDueAsync(TestData.Jan1.AddMonths(6))).ShouldBe(2);
    }

    [Fact]
    public async Task A_rule_stops_after_its_occurrence_limit()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob], maxOccurrences: 3));

        (await Recurring.RunDueAsync(TestData.Jan1.AddMonths(12))).ShouldBe(3);
    }

    [Fact]
    public async Task An_archived_group_stops_generating_occurrences()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob]));
        await Groups.ArchiveAsync(userId, group.Id);

        (await Recurring.RunDueAsync(TestData.Jan1.AddMonths(3))).ShouldBe(0);
    }

    [Fact]
    public async Task A_generated_occurrence_is_recorded_in_the_sync_log()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob]));

        await Recurring.RunDueAsync(TestData.Jan1);

        (await NewContext().SyncLog.AnyAsync(e =>
            e.GroupId == group.Id && e.EntityType == SyncEntityType.Expense)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_generated_occurrence_notifies_the_group()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob]));

        await Recurring.RunDueAsync(TestData.Jan1);

        await Push.Received().SendToGroupAsync(
            group.Id, Arg.Any<PushMessage>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deleting_a_rule_leaves_the_expenses_it_already_created()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var rule = await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob]));
        await Recurring.RunDueAsync(TestData.Jan1);

        await Recurring.DeleteAsync(userId, rule.Id);

        (await NewContext().Expenses.CountAsync(e => !e.IsDeleted)).ShouldBe(1);
        (await Recurring.ListAsync(userId, group.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_a_member_can_manage_the_rules_of_a_group()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var rule = await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob]));
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        await Should.ThrowAsync<ForbiddenException>(() => Recurring.DeleteAsync(stranger.Id, rule.Id));
    }

    [Fact]
    public async Task A_weekly_rule_generates_weekly()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await Recurring.CreateAsync(userId, Rule(group.Id, alice, [alice, bob],
            unit: RecurrenceUnit.Week, dayOfMonth: null));

        (await Recurring.RunDueAsync(TestData.Jan1.AddDays(21))).ShouldBe(4);
    }
}

public class ReceiptServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private string _root = string.Empty;
    private LocalDiskReceiptStorage Storage { get; set; } = null!;
    private ReceiptService Receipts { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _root = Path.Combine(Path.GetTempPath(), $"split-receipts-{Guid.NewGuid():N}");
        Storage = new LocalDiskReceiptStorage(new ReceiptStorageOptions { RootPath = _root });
        Receipts = new ReceiptService(Db, Storage, Clock);
    }

    public override Task DisposeAsync()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        return base.DisposeAsync();
    }

    private static Stream Image(string content = "fake-jpeg-bytes")
        => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task A_receipt_can_be_uploaded_and_read_back()
    {
        var user = await TestData.SeedUserAsync(Db);

        var receipt = await Receipts.UploadAsync(user.Id, Image(), "image/jpeg", "till.jpg");

        receipt.ContentType.ShouldBe("image/jpeg");
        receipt.SizeBytes.ShouldBeGreaterThan(0);

        var download = await Receipts.DownloadAsync(user.Id, receipt.Id);
        using var reader = new StreamReader(download.Content);
        (await reader.ReadToEndAsync()).ShouldBe("fake-jpeg-bytes");
    }

    [Fact]
    public async Task The_same_image_uploaded_twice_is_stored_once()
    {
        var user = await TestData.SeedUserAsync(Db);

        var first = await Receipts.UploadAsync(user.Id, Image(), "image/jpeg", "a.jpg");
        var second = await Receipts.UploadAsync(user.Id, Image(), "image/jpeg", "b.jpg");

        second.Id.ShouldBe(first.Id);
        (await NewContext().Receipts.CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData("application/x-msdownload")]
    [InlineData("text/html")]
    public async Task A_non_image_upload_is_refused(string contentType)
    {
        var user = await TestData.SeedUserAsync(Db);

        await Should.ThrowAsync<ValidationException>(
            () => Receipts.UploadAsync(user.Id, Image(), contentType, "bad.exe"));
    }

    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        var user = await TestData.SeedUserAsync(Db);

        await Should.ThrowAsync<ValidationException>(
            () => Receipts.UploadAsync(user.Id, new MemoryStream(), "image/jpeg", "empty.jpg"));
    }

    [Fact]
    public async Task An_oversized_upload_is_refused()
    {
        var user = await TestData.SeedUserAsync(Db);
        var big = new MemoryStream(new byte[ReceiptService.MaxBytes + 1]);

        await Should.ThrowAsync<ValidationException>(
            () => Receipts.UploadAsync(user.Id, big, "image/jpeg", "huge.jpg"));
    }

    [Fact]
    public async Task An_unattached_receipt_is_readable_only_by_its_uploader()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var other = await TestData.SeedUserAsync(Db, "Other");
        var receipt = await Receipts.UploadAsync(owner.Id, Image(), "image/jpeg", "till.jpg");

        await Should.ThrowAsync<ForbiddenException>(() => Receipts.DownloadAsync(other.Id, receipt.Id));
    }

    [Fact]
    public async Task A_receipt_attached_to_an_expense_is_readable_by_the_group()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var member = await TestData.SeedUserAsync(Db, "Member");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        Db.GroupMembers.Add(TestData.Member(group.Id, member.Id, "Member"));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var receipt = await Receipts.UploadAsync(owner.Id, Image(), "image/jpeg", "till.jpg");
        var payer = group.Members.First(m => m.UserId == owner.Id).Id;
        await Expenses.CreateAsync(owner.Id, new CreateExpenseRequest(
            group.Id, payer, "Dinner", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(payer, null)], null, receipt.Id, null, null, null, null));

        var download = await Receipts.DownloadAsync(member.Id, receipt.Id);

        download.ContentType.ShouldBe("image/jpeg");
    }

    [Fact]
    public async Task A_receipt_attached_to_another_groups_expense_stays_private()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");
        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var receipt = await Receipts.UploadAsync(owner.Id, Image(), "image/jpeg", "till.jpg");
        var payer = group.Members.Single().Id;
        await Expenses.CreateAsync(owner.Id, new CreateExpenseRequest(
            group.Id, payer, "Dinner", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(payer, null)], null, receipt.Id, null, null, null, null));

        await Should.ThrowAsync<ForbiddenException>(() => Receipts.DownloadAsync(stranger.Id, receipt.Id));
    }

    [Fact]
    public async Task Downloading_an_unknown_receipt_is_a_not_found()
    {
        var user = await TestData.SeedUserAsync(Db);

        await Should.ThrowAsync<NotFoundException>(() => Receipts.DownloadAsync(user.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Blobs_are_written_under_the_configured_root()
    {
        var user = await TestData.SeedUserAsync(Db);
        await Receipts.UploadAsync(user.Id, Image(), "image/jpeg", "till.jpg");

        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_storage_key_cannot_escape_the_root()
    {
        // A traversal key would otherwise read arbitrary files off the host.
        await Should.ThrowAsync<ValidationException>(
            () => Storage.OpenAsync("../../etc/passwd"));
    }

    [Fact]
    public async Task Deleting_a_blob_removes_it_from_disk()
    {
        var user = await TestData.SeedUserAsync(Db);
        var receipt = await Receipts.UploadAsync(user.Id, Image(), "image/jpeg", "till.jpg");
        var key = (await NewContext().Receipts.FirstAsync(r => r.Id == receipt.Id)).StorageKey;

        await Storage.DeleteAsync(key);

        (await Storage.ExistsAsync(key)).ShouldBeFalse();
        (await Storage.OpenAsync(key)).ShouldBeNull();
    }
}

public class NotificationServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private static readonly PushOptions Options = new()
    {
        VapidPublicKey = "BJ_test_public_key",
        VapidPrivateKey = "test-private-key",
        VapidSubject = "mailto:owner@example.com"
    };

    private NotificationService Notifications { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Notifications = new NotificationService(Db, Options, Clock);
    }

    [Fact]
    public async Task A_web_push_subscription_can_be_registered()
    {
        var user = await TestData.SeedUserAsync(Db);

        var subscription = await Notifications.RegisterAsync(user.Id, new RegisterPushRequest(
            PushChannel.WebPush, "https://push.example/abc", "p256dh-value", "auth-value", TestData.DeviceA));

        subscription.Channel.ShouldBe(PushChannel.WebPush);
        subscription.Endpoint.ShouldBe("https://push.example/abc");
    }

    [Fact]
    public async Task A_native_token_can_be_registered()
    {
        var user = await TestData.SeedUserAsync(Db);

        var subscription = await Notifications.RegisterAsync(user.Id, new RegisterPushRequest(
            PushChannel.Fcm, "fcm-device-token", null, null, TestData.DeviceA));

        subscription.Channel.ShouldBe(PushChannel.Fcm);
    }

    [Fact]
    public async Task Registering_the_same_endpoint_twice_updates_it_rather_than_duplicating()
    {
        var user = await TestData.SeedUserAsync(Db);
        var request = new RegisterPushRequest(
            PushChannel.WebPush, "https://push.example/abc", "key-1", "auth-1", TestData.DeviceA);

        var first = await Notifications.RegisterAsync(user.Id, request);
        var second = await Notifications.RegisterAsync(user.Id, request with { P256dh = "key-2" });

        second.Id.ShouldBe(first.Id);
        (await NewContext().PushSubscriptions.CountAsync()).ShouldBe(1);
        (await NewContext().PushSubscriptions.SingleAsync()).P256dh.ShouldBe("key-2");
    }

    [Fact]
    public async Task A_web_push_subscription_needs_its_keys()
    {
        var user = await TestData.SeedUserAsync(Db);

        await Should.ThrowAsync<ValidationException>(() => Notifications.RegisterAsync(
            user.Id, new RegisterPushRequest(PushChannel.WebPush, "https://push.example/abc", null, null, null)));
    }

    [Fact]
    public async Task An_endpoint_is_required()
    {
        var user = await TestData.SeedUserAsync(Db);

        await Should.ThrowAsync<ValidationException>(() => Notifications.RegisterAsync(
            user.Id, new RegisterPushRequest(PushChannel.Fcm, "   ", null, null, null)));
    }

    [Fact]
    public async Task Unregistering_removes_the_subscription()
    {
        var user = await TestData.SeedUserAsync(Db);
        await Notifications.RegisterAsync(user.Id, new RegisterPushRequest(
            PushChannel.Fcm, "fcm-token", null, null, null));

        await Notifications.UnregisterAsync(user.Id, "fcm-token");

        (await Notifications.ListAsync(user.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Unregistering_someone_elses_endpoint_is_forbidden()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var other = await TestData.SeedUserAsync(Db, "Other");
        await Notifications.RegisterAsync(owner.Id, new RegisterPushRequest(
            PushChannel.Fcm, "fcm-token", null, null, null));

        await Should.ThrowAsync<ForbiddenException>(() => Notifications.UnregisterAsync(other.Id, "fcm-token"));
    }

    [Fact]
    public async Task Listing_shows_only_my_subscriptions()
    {
        var owner = await TestData.SeedUserAsync(Db, "Owner");
        var other = await TestData.SeedUserAsync(Db, "Other");
        await Notifications.RegisterAsync(owner.Id, new RegisterPushRequest(
            PushChannel.Fcm, "mine", null, null, null));
        await Notifications.RegisterAsync(other.Id, new RegisterPushRequest(
            PushChannel.Fcm, "theirs", null, null, null));

        (await Notifications.ListAsync(owner.Id)).ShouldHaveSingleItem().Endpoint.ShouldBe("mine");
    }

    [Fact]
    public void The_vapid_public_key_is_exposed_for_the_browser()
        => Notifications.GetVapidPublicKey().PublicKey.ShouldBe("BJ_test_public_key");

    [Fact]
    public async Task The_dispatcher_fans_out_to_every_channel_a_user_registered()
    {
        var user = await TestData.SeedUserAsync(Db);
        await Notifications.RegisterAsync(user.Id, new RegisterPushRequest(
            PushChannel.WebPush, "https://push.example/abc", "key", "auth", null));
        await Notifications.RegisterAsync(user.Id, new RegisterPushRequest(
            PushChannel.Fcm, "fcm-token", null, null, null));

        var web = Sender(PushChannel.WebPush, succeeds: true);
        var fcm = Sender(PushChannel.Fcm, succeeds: true);
        var dispatcher = new PushDispatcher(Db, [web, fcm], Clock, NullLogger<PushDispatcher>.Instance);

        await dispatcher.SendToUsersAsync([user.Id], new PushMessage("Title", "Body"));

        await web.Received(1).SendAsync(Arg.Any<PushTarget>(), Arg.Any<PushMessage>(), Arg.Any<CancellationToken>());
        await fcm.Received(1).SendAsync(Arg.Any<PushTarget>(), Arg.Any<PushMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_dispatcher_prunes_a_subscription_the_provider_rejected()
    {
        var user = await TestData.SeedUserAsync(Db);
        await Notifications.RegisterAsync(user.Id, new RegisterPushRequest(
            PushChannel.Fcm, "stale-token", null, null, null));
        var fcm = Sender(PushChannel.Fcm, succeeds: false);
        var dispatcher = new PushDispatcher(Db, [fcm], Clock, NullLogger<PushDispatcher>.Instance);

        await dispatcher.SendToUsersAsync([user.Id], new PushMessage("Title", "Body"));

        (await NewContext().PushSubscriptions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_dispatcher_notifies_a_group_except_the_person_who_acted()
    {
        var actor = await TestData.SeedUserAsync(Db, "Actor");
        var other = await TestData.SeedUserAsync(Db, "Other");
        var group = await Groups.CreateAsync(actor.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        Db.GroupMembers.Add(TestData.Member(group.Id, other.Id, "Other"));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        await Notifications.RegisterAsync(actor.Id, new RegisterPushRequest(
            PushChannel.Fcm, "actor-token", null, null, null));
        await Notifications.RegisterAsync(other.Id, new RegisterPushRequest(
            PushChannel.Fcm, "other-token", null, null, null));

        var fcm = Sender(PushChannel.Fcm, succeeds: true);
        var dispatcher = new PushDispatcher(Db, [fcm], Clock, NullLogger<PushDispatcher>.Instance);

        await dispatcher.SendToGroupAsync(group.Id, new PushMessage("Title", "Body"), exceptUserId: actor.Id);

        await fcm.Received(1).SendAsync(
            Arg.Is<PushTarget>(t => t.Endpoint == "other-token"),
            Arg.Any<PushMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_user_with_no_subscription_is_simply_skipped()
    {
        var user = await TestData.SeedUserAsync(Db);
        var fcm = Sender(PushChannel.Fcm, succeeds: true);
        var dispatcher = new PushDispatcher(Db, [fcm], Clock, NullLogger<PushDispatcher>.Instance);

        await dispatcher.SendToUsersAsync([user.Id], new PushMessage("Title", "Body"));

        await fcm.DidNotReceive().SendAsync(
            Arg.Any<PushTarget>(), Arg.Any<PushMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task One_failing_channel_does_not_stop_the_others()
    {
        var user = await TestData.SeedUserAsync(Db);
        await Notifications.RegisterAsync(user.Id, new RegisterPushRequest(
            PushChannel.WebPush, "https://push.example/abc", "key", "auth", null));
        await Notifications.RegisterAsync(user.Id, new RegisterPushRequest(
            PushChannel.Fcm, "fcm-token", null, null, null));

        var web = Substitute.For<IPushSender>();
        web.Channel.Returns(PushChannel.WebPush);
        web.SendAsync(Arg.Any<PushTarget>(), Arg.Any<PushMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new HttpRequestException("boom"));
        var fcm = Sender(PushChannel.Fcm, succeeds: true);
        var dispatcher = new PushDispatcher(Db, [web, fcm], Clock, NullLogger<PushDispatcher>.Instance);

        await dispatcher.SendToUsersAsync([user.Id], new PushMessage("Title", "Body"));

        await fcm.Received(1).SendAsync(Arg.Any<PushTarget>(), Arg.Any<PushMessage>(), Arg.Any<CancellationToken>());
    }

    private static IPushSender Sender(PushChannel channel, bool succeeds)
    {
        var sender = Substitute.For<IPushSender>();
        sender.Channel.Returns(channel);
        sender.SendAsync(Arg.Any<PushTarget>(), Arg.Any<PushMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(succeeds));
        return sender;
    }
}

public class FrankfurterCurrencyConverterTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);

    private FrankfurterCurrencyConverter Create(StubHttpHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev/") },
            Db, new FixedClock(Now), NullLogger<FrankfurterCurrencyConverter>.Instance);

    [Fact]
    public async Task Converting_between_the_same_currency_is_a_no_op()
    {
        var handler = new StubHttpHandler("{}");

        var result = await Create(handler).ConvertAsync(100m, "CAD", "CAD");

        result.Amount.ShouldBe(100m);
        result.Rate.ShouldBe(1m);
        handler.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_rate_is_fetched_and_applied()
    {
        var handler = new StubHttpHandler("""{"amount":1,"base":"EUR","date":"2026-08-31","rates":{"CAD":1.48}}""");

        var result = await Create(handler).ConvertAsync(100m, "EUR", "CAD");

        result.Rate.ShouldBe(1.48m);
        result.Amount.ShouldBe(148m);
    }

    [Fact]
    public async Task A_converted_amount_is_rounded_to_the_target_currency()
    {
        var handler = new StubHttpHandler("""{"amount":1,"base":"EUR","date":"2026-08-31","rates":{"JPY":161.377}}""");

        var result = await Create(handler).ConvertAsync(10m, "EUR", "JPY");

        result.Amount.ShouldBe(Math.Round(1613.77m, 0, MidpointRounding.ToEven));
    }

    [Fact]
    public async Task A_fetched_rate_is_cached_for_the_day()
    {
        var handler = new StubHttpHandler("""{"amount":1,"base":"EUR","date":"2026-08-31","rates":{"CAD":1.48}}""");
        var converter = Create(handler);

        await converter.ConvertAsync(100m, "EUR", "CAD");
        await converter.ConvertAsync(50m, "EUR", "CAD");

        handler.CallCount.ShouldBe(1);
        (await NewContext().ExchangeRates.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_cached_rate_survives_a_new_converter_instance()
    {
        var first = new StubHttpHandler("""{"amount":1,"base":"EUR","date":"2026-08-31","rates":{"CAD":1.48}}""");
        await Create(first).ConvertAsync(100m, "EUR", "CAD");

        var second = new StubHttpHandler("{}");
        var result = await Create(second).ConvertAsync(100m, "EUR", "CAD");

        result.Rate.ShouldBe(1.48m);
        second.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_stale_cached_rate_is_used_when_the_service_is_unreachable()
    {
        var first = new StubHttpHandler("""{"amount":1,"base":"EUR","date":"2026-08-31","rates":{"CAD":1.48}}""");
        await Create(first).ConvertAsync(100m, "EUR", "CAD");

        // Two days later the service is down: an old rate beats refusing the expense.
        var offline = new StubHttpHandler(string.Empty, HttpStatusCode.ServiceUnavailable);
        var later = new FrankfurterCurrencyConverter(
            new HttpClient(offline) { BaseAddress = new Uri("https://api.frankfurter.dev/") },
            Db, new FixedClock(Now.AddDays(2)), NullLogger<FrankfurterCurrencyConverter>.Instance);

        (await later.ConvertAsync(100m, "EUR", "CAD")).Rate.ShouldBe(1.48m);
    }

    [Fact]
    public async Task An_unreachable_service_with_no_cached_rate_is_an_error()
    {
        var handler = new StubHttpHandler(string.Empty, HttpStatusCode.ServiceUnavailable);

        await Should.ThrowAsync<AppException>(() => Create(handler).ConvertAsync(100m, "EUR", "CAD"));
    }

    [Fact]
    public async Task A_response_missing_the_requested_currency_is_an_error()
    {
        var handler = new StubHttpHandler("""{"amount":1,"base":"EUR","date":"2026-08-31","rates":{"USD":1.1}}""");

        await Should.ThrowAsync<AppException>(() => Create(handler).ConvertAsync(100m, "EUR", "CAD"));
    }

    [Fact]
    public async Task Refreshing_the_cache_stores_every_pair_it_was_asked_for()
    {
        var handler = new StubHttpHandler(
            """{"amount":1,"base":"CAD","date":"2026-08-31","rates":{"EUR":0.68,"USD":0.74}}""");

        await Create(handler).RefreshCacheAsync(["CAD", "EUR", "USD"]);

        (await NewContext().ExchangeRates.CountAsync()).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task An_invalid_currency_code_is_rejected()
        => await Should.ThrowAsync<ValidationException>(
            () => Create(new StubHttpHandler("{}")).ConvertAsync(10m, "EUROS", "CAD"));
}

/// <summary>Canned HTTP responses, so the currency tests never touch the network.</summary>
public sealed class StubHttpHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public int CallCount { get; private set; }
    public string? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequestUri = request.RequestUri?.ToString();

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}
