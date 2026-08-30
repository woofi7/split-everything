using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Auth;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Notifications;
using SplitEverything.Application.Contracts.Settlements;
using SplitEverything.Application.Contracts.Stats;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Domain.Common;
using SplitEverything.Api;
using SplitEverything.Api.Controllers;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Api;

/// <summary>
/// End to end over HTTP: routing, JSON, auth, authorization and the error handler.
/// </summary>
public class ApiEndpointTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    // ---- health and auth -------------------------------------------------

    [Fact]
    public async Task Health_is_public()
    {
        var response = await Client.GetAsync("/api/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_reports_the_database_is_reachable()
    {
        var response = await Client.GetAsync("/api/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ready");
    }

    [Fact]
    public async Task An_unauthenticated_request_is_rejected()
    {
        var response = await Client.GetAsync("/api/groups");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_garbage_bearer_token_is_rejected()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        (await Client.GetAsync("/api/groups")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_signed_with_the_wrong_key_is_rejected()
    {
        // Forged with a different secret: the signature check has to catch it.
        var forged = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
                     "eyJzdWIiOiIwMTkyMDAwMC0wMDAwLTcwMDAtODAwMC0wMDAwMDAwMDAwMDAiLCJleHAiOjk5OTk5OTk5OTl9." +
                     "Zm9yZ2VkLXNpZ25hdHVyZQ";
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        (await Client.GetAsync("/api/groups")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_in_with_google_returns_the_user_and_tokens()
    {
        var user = await SignInAsync();

        user.Email.ShouldBe("alice@example.com");
        user.DisplayName.ShouldBe("Alice");
    }

    [Fact]
    public async Task Signing_in_sets_the_refresh_cookie()
    {
        Factory.Google.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GoogleIdentity("google-alice", "alice@example.com", true, "Alice", null)));

        var response = await Client.PostAsJsonAsync("/api/auth/google",
            new GoogleSignInRequest("token", TestData.DeviceA, null, "web"), Json);

        response.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeTrue();
        cookies!.ShouldContain(c => c.Contains("se_refresh") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_rejected_google_token_is_a_403()
    {
        Factory.Google.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<GoogleIdentity>>(_ => throw new ForbiddenException("bad token"));

        var response = await Client.PostAsJsonAsync("/api/auth/google",
            new GoogleSignInRequest("token", null, null, null), Json);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reading_my_profile_works_with_the_issued_token()
    {
        await SignInAsync();

        var me = await Client.GetFromJsonAsync<AuthenticatedUser>("/api/auth/me", Json);

        me!.Email.ShouldBe("alice@example.com");
    }

    [Fact]
    public async Task Updating_my_profile_over_http_persists()
    {
        await SignInAsync();

        var response = await Client.PatchAsJsonAsync("/api/auth/me",
            new UpdateProfileRequest("Alice A", "EUR", true, null), Json);
        response.EnsureSuccessStatusCode();

        var me = await response.Content.ReadFromJsonAsync<AuthenticatedUser>(Json);
        me!.DisplayName.ShouldBe("Alice A");
        me.DefaultCurrency.ShouldBe("EUR");
    }

    [Fact]
    public async Task Exporting_my_data_returns_a_json_file()
    {
        await SignInAsync();

        var response = await Client.GetAsync("/api/auth/me/export");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        response.Content.Headers.ContentDisposition!.FileName!.ShouldContain("split-everything-export");
    }

    [Fact]
    public async Task Refreshing_with_the_cookie_returns_a_new_access_token()
    {
        Factory.Google.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GoogleIdentity("google-alice", "alice@example.com", true, "Alice", null)));
        var signIn = await Client.PostAsJsonAsync("/api/auth/google",
            new GoogleSignInRequest("token", TestData.DeviceA, null, "web"), Json);
        var tokens = (await signIn.Content.ReadFromJsonAsync<SignInResult>(Json))!.Tokens;

        var response = await Client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(tokens.RefreshToken, TestData.DeviceA), Json);

        response.EnsureSuccessStatusCode();
        var refreshed = await response.Content.ReadFromJsonAsync<AuthTokens>(Json);
        refreshed!.AccessToken.ShouldNotBe(tokens.AccessToken);
    }

    // ---- groups ----------------------------------------------------------

    [Fact]
    public async Task Creating_a_group_returns_201_with_a_location()
    {
        await SignInAsync();

        var response = await Client.PostAsJsonAsync("/api/groups",
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob"]), Json);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();

        var group = await response.Content.ReadFromJsonAsync<GroupDto>(Json);
        group!.Members.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_invalid_group_is_a_400_problem_response()
    {
        await SignInAsync();

        var response = await Client.PostAsJsonAsync("/api/groups",
            new CreateGroupRequest("", "CAD", null, null, null, null), Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Group name");
    }

    [Fact]
    public async Task A_group_i_am_not_in_is_a_403()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var other = await SignInAsAnotherUserAsync("Bob");

        (await other.GetAsync($"/api/groups/{group.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_group_that_does_not_exist_is_a_404()
    {
        await SignInAsync();

        (await Client.GetAsync($"/api/groups/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Listing_groups_returns_mine()
    {
        await SignInAsync();
        await CreateGroupAsync("Roommates");

        var groups = await Client.GetFromJsonAsync<List<GroupSummaryDto>>("/api/groups", Json);

        groups!.ShouldHaveSingleItem().Name.ShouldBe("Roommates");
    }

    [Fact]
    public async Task Archiving_and_unarchiving_round_trips_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();

        var archived = await Client.PostAsync($"/api/groups/{group.Id}/archive", null);
        (await archived.Content.ReadFromJsonAsync<GroupDto>(Json))!.IsArchived.ShouldBeTrue();

        var unarchived = await Client.PostAsync($"/api/groups/{group.Id}/unarchive", null);
        (await unarchived.Content.ReadFromJsonAsync<GroupDto>(Json))!.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public async Task Writing_to_an_archived_group_is_a_409()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        await Client.PostAsync($"/api/groups/{group.Id}/archive", null);

        await SignInAsAnotherUserAsync("Carol");
        var carol = await NewContext().Users.FirstAsync(u => u.DisplayName == "Carol");

        var response = await Client.PostAsJsonAsync($"/api/groups/{group.Id}/members/user",
            new AddUserMemberRequest(carol.Id), Json);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ---- expenses --------------------------------------------------------

    [Fact]
    public async Task An_expense_can_be_created_and_read_back_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync("Roommates", "Bob");
        var alice = group.Members.First(m => m.UserId is not null).Id;
        var bob = group.Members.First(m => m.DisplayName == "Bob").Id;

        var created = await Client.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(
            group.Id, alice, "Dinner", 60m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null), new SplitInputDto(bob, null)], null, null, null, null, null, null), Json);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var expense = await created.Content.ReadFromJsonAsync<ExpenseDto>(Json);
        expense!.Splits.Count.ShouldBe(2);

        var fetched = await Client.GetFromJsonAsync<ExpenseDto>($"/api/expenses/{expense.Id}", Json);
        fetched!.Description.ShouldBe("Dinner");
    }

    [Fact]
    public async Task Enums_travel_as_names_not_numbers()
    {
        await SignInAsync();
        var group = await CreateGroupAsync("Roommates", "Bob");
        var alice = group.Members.First(m => m.UserId is not null).Id;

        var created = await Client.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(
            group.Id, alice, "Dinner", 60m, "CAD", TestData.Jan1, SplitType.Percentage,
            [new SplitInputDto(alice, 100m)], null, null, null, null, null, null), Json);

        (await created.Content.ReadAsStringAsync()).ShouldContain("\"Percentage\"");
    }

    [Fact]
    public async Task Listing_expenses_pages_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;
        for (var i = 0; i < 3; i++) await CreateExpenseAsync(group.Id, alice, 10m + i, $"Item {i}");

        var page = await Client.GetFromJsonAsync<Paged<ExpenseDto>>(
            $"/api/expenses?groupId={group.Id}&page=1&pageSize=2", Json);

        page!.Items.Count.ShouldBe(2);
        page.Total.ShouldBe(3);
        page.HasMore.ShouldBeTrue();
    }

    [Fact]
    public async Task Updating_an_expense_over_http_bumps_the_revision()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;
        var expense = await CreateExpenseAsync(group.Id, alice, 40m, "Original");

        var response = await Client.PatchAsJsonAsync($"/api/expenses/{expense.Id}",
            new UpdateExpenseRequest(null, "Renamed", null, null, null, null, null, null, null, null, null), Json);

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<ExpenseDto>(Json))!.Revision.ShouldBe(2);
    }

    [Fact]
    public async Task Deleting_an_expense_returns_204_and_then_404()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;
        var expense = await CreateExpenseAsync(group.Id, alice, 40m, "Doomed");

        (await Client.DeleteAsync($"/api/expenses/{expense.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Client.GetAsync($"/api/expenses/{expense.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Comments_round_trip_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;
        var expense = await CreateExpenseAsync(group.Id, alice, 40m, "Dinner");

        await Client.PostAsJsonAsync($"/api/expenses/{expense.Id}/comments",
            new CreateCommentRequest(expense.Id, "Was this the taxi?", null, null), Json);

        var comments = await Client.GetFromJsonAsync<List<CommentDto>>(
            $"/api/expenses/{expense.Id}/comments", Json);

        comments!.ShouldHaveSingleItem().Body.ShouldBe("Was this the taxi?");
    }

    [Fact]
    public async Task An_expense_history_is_readable_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;
        var expense = await CreateExpenseAsync(group.Id, alice, 40m, "Dinner");

        var history = await Client.GetFromJsonAsync<List<ExpenseRevisionDto>>(
            $"/api/expenses/{expense.Id}/history", Json);

        history!.ShouldHaveSingleItem().Revision.ShouldBe(1);
    }

    // ---- settlements and balances ---------------------------------------

    [Fact]
    public async Task A_group_balance_offers_a_simplified_plan_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync("Roommates", "Bob");
        var alice = group.Members.First(m => m.UserId is not null).Id;
        var bob = group.Members.First(m => m.DisplayName == "Bob").Id;
        await CreateExpenseAsync(group.Id, alice, 100m, "Dinner", bob);

        var balance = await Client.GetFromJsonAsync<GroupBalanceDto>($"/api/groups/{group.Id}/balance", Json);

        balance!.SimplifiedTransfers.ShouldHaveSingleItem().Amount.ShouldBe(50m);
    }

    [Fact]
    public async Task A_settlement_can_be_recorded_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync("Roommates", "Bob");
        var alice = group.Members.First(m => m.UserId is not null).Id;
        var bob = group.Members.First(m => m.DisplayName == "Bob").Id;
        await CreateExpenseAsync(group.Id, alice, 100m, "Dinner", bob);

        var response = await Client.PostAsJsonAsync("/api/settlements", new CreateSettlementRequest(
            group.Id, bob, alice, 50m, "CAD", TestData.Jan1, "Etransfer", null, null), Json);

        response.EnsureSuccessStatusCode();
        var balance = await Client.GetFromJsonAsync<GroupBalanceDto>($"/api/groups/{group.Id}/balance", Json);
        balance!.SimplifiedTransfers.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_overall_balance_is_readable_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync("Roommates", "Bob");
        var alice = group.Members.First(m => m.UserId is not null).Id;
        var bob = group.Members.First(m => m.DisplayName == "Bob").Id;
        await CreateExpenseAsync(group.Id, alice, 100m, "Dinner", bob);

        var overall = await Client.GetFromJsonAsync<OverallBalanceDto>("/api/settlements/overall", Json);

        overall!.TotalOwedToMe.ShouldBe(50m);
    }

    [Fact]
    public async Task Nudging_someone_who_owes_nothing_is_a_400()
    {
        await SignInAsync();
        var group = await CreateGroupAsync("Roommates", "Bob");
        var bob = group.Members.First(m => m.DisplayName == "Bob").Id;

        var response = await Client.PostAsJsonAsync("/api/settlements/nudge",
            new NudgeRequest(group.Id, bob, null), Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- invites ---------------------------------------------------------

    [Fact]
    public async Task An_invite_can_be_created_previewed_and_redeemed_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();

        var created = await Client.PostAsJsonAsync($"/api/groups/{group.Id}/invites",
            new CreateInviteRequest(null, null, 1, 72), Json);
        created.EnsureSuccessStatusCode();
        var invite = await created.Content.ReadFromJsonAsync<InviteDto>(Json);

        // The preview is deliberately public, so the sign-in page can name the group.
        var anonymous = Factory.CreateClient();
        var preview = await anonymous.GetFromJsonAsync<InvitePreviewDto>($"/api/invites/{invite!.Token}", Json);
        preview!.GroupName.ShouldBe("Roommates");

        var joiner = await SignInAsAnotherUserAsync("Bob");
        var redeemed = await joiner.PostAsync($"/api/invites/{invite.Token}/redeem", null);
        redeemed.EnsureSuccessStatusCode();

        (await redeemed.Content.ReadFromJsonAsync<RedeemInviteResult>(Json))!.GroupId.ShouldBe(group.Id);
    }

    [Fact]
    public async Task An_invite_qr_code_is_served_as_a_png()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var created = await Client.PostAsJsonAsync($"/api/groups/{group.Id}/invites",
            new CreateInviteRequest(null, null, 1, 72), Json);
        var invite = await created.Content.ReadFromJsonAsync<InviteDto>(Json);

        var response = await Client.GetAsync($"/api/groups/invites/{invite!.Id}/qr");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
    }

    [Fact]
    public async Task Redeeming_an_unknown_invite_is_a_404()
    {
        await SignInAsync();

        (await Client.PostAsync("/api/invites/nonsense/redeem", null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- sync ------------------------------------------------------------

    [Fact]
    public async Task An_offline_batch_can_be_pushed_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;
        var expenseId = Guid.CreateVersion7();

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            id = expenseId, groupId = group.Id, paidByMemberId = alice,
            description = "Offline dinner", amount = 25m, currency = "CAD",
            amountInBaseCurrency = 25m, spentAt = TestData.Jan1,
            splits = new[] { new { memberId = alice, amount = 25m, amountInBaseCurrency = 25m } }
        });

        var response = await Client.PostAsJsonAsync("/api/sync/push", new SyncPushRequest(
            TestData.DeviceA,
            [
                new SyncOperationDto(Guid.NewGuid(), SyncEntityType.Expense, expenseId,
                    SyncOperation.Create, group.Id, payload,
                    new Dictionary<string, long> { [TestData.DeviceA] = 1 }, TestData.Jan1)
            ]), Json);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SyncPushResult>(Json);
        result!.Accepted.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_delta_pull_returns_the_group_history_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;
        await CreateExpenseAsync(group.Id, alice, 40m, "Dinner");

        var response = await Client.PostAsJsonAsync("/api/sync/pull", new SyncPullRequest(
            TestData.DeviceA, new Dictionary<Guid, long> { [group.Id] = 0 }), Json);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SyncPullResult>(Json);
        result!.Entries.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Acknowledging_a_cursor_returns_204()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();

        var response = await Client.PostAsJsonAsync("/api/sync/ack",
            new Dictionary<Guid, long> { [group.Id] = 3 }, Json);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await NewContext().Devices.FirstAsync(d => d.Id == TestData.DeviceA))
            .LastAckedServerSeq.ShouldBe(3);
    }

    // ---- stats, activity, categories, notifications ----------------------

    [Fact]
    public async Task The_stats_dashboard_is_readable_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;
        await CreateExpenseAsync(group.Id, alice, 40m, "Dinner");

        var dashboard = await Client.GetFromJsonAsync<StatsDashboardDto>(
            $"/api/stats?groupId={group.Id}", Json);

        dashboard!.TotalSpend.ShouldBe(40m);
        dashboard.ExpenseCount.ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_granularity_is_a_400()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();

        (await Client.GetAsync($"/api/stats?groupId={group.Id}&granularity=fortnight"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_activity_feed_is_readable_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();

        var feed = await Client.GetFromJsonAsync<Paged<ActivityEntryDto>>(
            $"/api/activity?groupId={group.Id}", Json);

        feed!.Items.ShouldNotBeEmpty();
        feed.Items.ShouldContain(a => a.Kind == ActivityKind.GroupCreated);
    }

    [Fact]
    public async Task The_vapid_public_key_is_public()
    {
        var anonymous = Factory.CreateClient();

        var key = await anonymous.GetFromJsonAsync<VapidPublicKeyDto>("/api/notifications/vapid-key", Json);

        key!.PublicKey.ShouldBe("test-public-key");
    }

    [Fact]
    public async Task A_push_subscription_can_be_registered_over_http()
    {
        await SignInAsync();

        var response = await Client.PostAsJsonAsync("/api/notifications", new RegisterPushRequest(
            PushChannel.WebPush, "https://push.example/abc", "key", "auth", null), Json);

        response.EnsureSuccessStatusCode();
        var list = await Client.GetFromJsonAsync<List<PushSubscriptionDto>>("/api/notifications", Json);
        list!.ShouldHaveSingleItem().Endpoint.ShouldBe("https://push.example/abc");
    }

    // ---- receipts and imports -------------------------------------------

    [Fact]
    public async Task A_receipt_can_be_uploaded_and_downloaded_over_http()
    {
        await SignInAsync();

        using var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("jpeg-bytes"));
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(bytes, "file", "till.jpg");

        var uploaded = await Client.PostAsync("/api/receipts", content);
        uploaded.EnsureSuccessStatusCode();
        var receipt = await uploaded.Content.ReadFromJsonAsync<SplitEverything.Application.Services.ReceiptDto>(Json);

        var download = await Client.GetAsync($"/api/receipts/{receipt!.Id}");
        download.EnsureSuccessStatusCode();
        (await download.Content.ReadAsStringAsync()).ShouldBe("jpeg-bytes");
    }

    [Fact]
    public async Task A_non_image_receipt_upload_is_a_400()
    {
        await SignInAsync();

        using var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/x-msdownload");
        content.Add(bytes, "file", "bad.exe");

        (await Client.PostAsync("/api/receipts", content)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_settle_up_csv_can_be_analysed_over_http()
    {
        await SignInAsync();
        await CreateGroupAsync("Roommates", "Bob");

        using var content = new MultipartFormDataContent();
        var csv = new StringContent("Date,Purpose,Category,Currency,Amount,Who paid,For whom\n" +
                                    "2026-01-05,Groceries,Groceries,CAD,84.32,Alice,\"Alice, Bob\"\n");
        csv.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(csv, "file", "export.csv");

        var response = await Client.PostAsync("/api/import/csv/analyze", content);

        response.EnsureSuccessStatusCode();
        var analysis = await response.Content.ReadFromJsonAsync<
            SplitEverything.Application.Contracts.Import.CsvAnalysisResult>(Json);
        analysis!.RowCount.ShouldBe(1);
        analysis.DetectedMemberNames.ShouldContain("Alice");
    }

    [Fact]
    public async Task An_import_with_no_file_is_a_400()
    {
        await SignInAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(""), "notafile");

        (await Client.PostAsync("/api/import/csv/analyze", content))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Confirmed_statement_rows_can_be_committed_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;

        var response = await Client.PostAsJsonAsync("/api/import/statement/commit",
            new SplitEverything.Application.Contracts.Import.StatementCommitRequest([
                new SplitEverything.Application.Contracts.Import.ConfirmedStatementRow(
                    group.Id, alice, "UBER EATS", 42.50m, "CAD", TestData.Jan1,
                    SplitType.Equal, [new SplitInputDto(alice, null)], "fp-1", null)
            ], true, "visa.csv"), Json);

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<
            SplitEverything.Application.Contracts.Import.ImportCommitResult>(Json))!.CreatedExpenses.ShouldBe(1);
    }

    [Fact]
    public async Task Two_groups_can_be_merged_over_http()
    {
        await SignInAsync();
        var target = await CreateGroupAsync("Keep", "Bob");
        var source = await CreateGroupAsync("Fold in", "Bob");

        var response = await Client.PostAsJsonAsync("/api/groups/merge",
            new MergeGroupsRequest(source.Id, target.Id, null, "Same people"), Json);

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<MergeGroupsResult>(Json))!
            .TargetGroupId.ShouldBe(target.Id);
    }

    [Fact]
    public async Task A_group_can_be_split_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync("Everything", "Bob");
        var alice = group.Members.First(m => m.UserId is not null).Id;
        var expense = await CreateExpenseAsync(group.Id, alice, 30m, "Moves");

        var response = await Client.PostAsJsonAsync("/api/groups/split",
            new SplitGroupRequest(group.Id, "Trip", [expense.Id], null, null, null), Json);

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<SplitGroupResult>(Json))!.MovedExpenses.ShouldBe(1);
    }

    [Fact]
    public async Task An_expense_can_be_transferred_over_http()
    {
        await SignInAsync();
        var from = await CreateGroupAsync("Wrong", "Bob");
        var to = await CreateGroupAsync("Right", "Bob");
        var alice = from.Members.First(m => m.UserId is not null).Id;
        var expense = await CreateExpenseAsync(from.Id, alice, 30m, "Misfiled");

        var response = await Client.PostAsJsonAsync($"/api/expenses/{expense.Id}/transfer",
            new TransferExpenseRequest(expense.Id, to.Id, null), Json);

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<TransferExpenseResult>(Json))!.ToGroupId.ShouldBe(to.Id);
    }

    [Fact]
    public async Task A_recurring_expense_can_be_created_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;

        var response = await Client.PostAsJsonAsync("/api/expenses/recurring",
            new CreateRecurringExpenseRequest(
                group.Id, alice, "Rent", 1200m, "CAD", SplitType.Equal,
                [new SplitInputDto(alice, null)], RecurrenceUnit.Month, 1, 1, null,
                TestData.Jan1, null, null), Json);

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<RecurringExpenseDto>(Json))!.Description.ShouldBe("Rent");
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<GroupDto> CreateGroupAsync(string name = "Roommates", params string[] placeholders)
    {
        var response = await Client.PostAsJsonAsync("/api/groups",
            new CreateGroupRequest(name, "CAD", null, null, null, placeholders), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GroupDto>(Json))!;
    }

    private async Task<ExpenseDto> CreateExpenseAsync(
        Guid groupId, Guid payer, decimal amount, string description, params Guid[] others)
    {
        var participants = others.Prepend(payer).Distinct()
            .Select(id => new SplitInputDto(id, null)).ToList();

        var response = await Client.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(
            groupId, payer, description, amount, "CAD", TestData.Jan1, SplitType.Equal,
            participants, null, null, null, null, null, null), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExpenseDto>(Json))!;
    }
}
