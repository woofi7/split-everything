using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SplitEverything.Application.Contracts.Auth;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Import;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Domain.Common;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Api;

/// <summary>
/// The endpoints the main suite does not reach: sign-out, account deletion, the
/// full CSV wizard over multipart, conflicts, and the smaller read endpoints.
/// </summary>
public class ApiCoverageTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task The_development_sign_in_is_refused_outside_development()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/dev",
            new DevelopmentSignInRequest("attacker@example.com", "Attacker", null), Json);

        // The environment variable asks for it to be on; startup forces it off
        // because the host is not Development. Without that, setting one variable
        // on a production box would be a complete authentication bypass.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Capabilities_do_not_advertise_a_bypass_that_is_forced_off()
    {
        var anonymous = Factory.CreateClient();

        var capabilities = await anonymous.GetFromJsonAsync<AuthCapabilities>(
            "/api/auth/capabilities", Json);

        capabilities!.DevelopmentSignIn.ShouldBeFalse();
        capabilities.GoogleConfigured.ShouldBeTrue();
    }

    [Fact]
    public async Task Capabilities_are_public_so_the_sign_in_page_can_explain_itself()
    {
        var anonymous = Factory.CreateClient();

        (await anonymous.GetAsync("/api/auth/capabilities")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_out_clears_the_refresh_cookie()
    {
        await SignInAsync();

        var response = await Client.PostAsJsonAsync("/api/auth/signout", new RefreshRequest("", null), Json);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Signing_out_everywhere_revokes_every_token()
    {
        var user = await SignInAsync();

        var response = await Client.PostAsync("/api/auth/signout-all", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await NewContext().RefreshTokens.CountAsync(t => t.UserId == user.Id && t.RevokedAt == null))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_my_account_over_http_removes_me()
    {
        var user = await SignInAsync();

        var response = await Client.DeleteAsync("/api/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await NewContext().Users.CountAsync(u => u.Id == user.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task A_group_lineage_is_readable_after_a_merge()
    {
        await SignInAsync();
        var target = await CreateGroupAsync("Keep");
        var source = await CreateGroupAsync("Fold in");
        await Client.PostAsJsonAsync("/api/groups/merge",
            new MergeGroupsRequest(source.Id, target.Id, null, "Same people"), Json);

        var lineage = await Client.GetFromJsonAsync<List<GroupLineageDto>>(
            $"/api/groups/{target.Id}/lineage", Json);

        lineage!.ShouldHaveSingleItem().Kind.ShouldBe(GroupLineageKind.Merge);
    }

    [Fact]
    public async Task A_member_can_be_added_and_removed_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();

        await SignInAsAnotherUserAsync("Carol");
        var carol = await NewContext().Users.FirstAsync(u => u.DisplayName == "Carol");

        var added = await Client.PostAsJsonAsync($"/api/groups/{group.Id}/members/user",
            new AddUserMemberRequest(carol.Id), Json);
        added.EnsureSuccessStatusCode();
        var member = await added.Content.ReadFromJsonAsync<GroupMemberDto>(Json);

        var removed = await Client.DeleteAsync($"/api/groups/{group.Id}/members/{member!.Id}");

        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_group_can_be_renamed_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();

        var response = await Client.PatchAsJsonAsync($"/api/groups/{group.Id}",
            new UpdateGroupRequest("Renamed", "New description", null, null, null), Json);

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<GroupDto>(Json))!.Name.ShouldBe("Renamed");
    }

    [Fact]
    public async Task Invites_can_be_listed_and_revoked_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var created = await Client.PostAsJsonAsync($"/api/groups/{group.Id}/invites",
            new CreateInviteRequest(null, null, 1, 72), Json);
        var invite = await created.Content.ReadFromJsonAsync<InviteDto>(Json);

        var listed = await Client.GetFromJsonAsync<List<InviteDto>>($"/api/groups/{group.Id}/invites", Json);
        listed!.ShouldHaveSingleItem();

        var revoked = await Client.DeleteAsync($"/api/groups/invites/{invite!.Id}");
        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Client.GetFromJsonAsync<List<InviteDto>>($"/api/groups/{group.Id}/invites", Json))!
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task The_full_csv_wizard_runs_over_multipart()
    {
        await SignInAsync();
        var group = await CreateGroupAsync("Roommates", "Bob");
        var mapping = new CsvColumnMapping(0, 1, 4, 3, 5, null, null, null);
        var names = group.Members.ToDictionary(m => m.DisplayName, m => (Guid?)m.Id);

        var preview = await PostCsvAsync("/api/import/csv/preview",
            new CsvPreviewRequest(group.Id, mapping, names, "CAD"));
        preview.EnsureSuccessStatusCode();
        var previewed = await preview.Content.ReadFromJsonAsync<CsvPreviewResult>(Json);
        previewed!.Rows.Count.ShouldBe(2);
        previewed.CommittableCount.ShouldBe(2);

        var commit = await PostCsvAsync("/api/import/csv/commit",
            new CsvCommitRequest(Guid.NewGuid(), group.Id, null, mapping, names, [], true, true, "CAD", null));
        commit.EnsureSuccessStatusCode();
        var committed = await commit.Content.ReadFromJsonAsync<ImportCommitResult>(Json);
        committed!.CreatedExpenses.ShouldBe(2);

        var rollback = await Client.PostAsync($"/api/import/batches/{committed.ImportBatchId}/rollback", null);
        rollback.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_malformed_import_request_field_is_a_400()
    {
        await SignInAsync();
        await CreateGroupAsync();

        using var content = new MultipartFormDataContent();
        var csv = new StringContent(CsvBody);
        csv.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(csv, "file", "export.csv");
        content.Add(new StringContent("{not json"), "request");

        (await Client.PostAsync("/api/import/csv/preview", content))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Duplicate_checks_and_split_suggestions_are_reachable_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;
        await Client.PostAsJsonAsync("/api/import/statement/commit",
            new StatementCommitRequest([
                new ConfirmedStatementRow(group.Id, alice, "UBER EATS", 42.50m, "CAD",
                    TestData.Jan1, SplitType.Equal, [new SplitInputDto(alice, null)], "fp-1", null)
            ], true, null), Json);

        var duplicates = await Client.PostAsJsonAsync("/api/import/duplicates",
            new DuplicateCheckRequest(["fp-1"], null), Json);
        duplicates.EnsureSuccessStatusCode();
        (await duplicates.Content.ReadFromJsonAsync<DuplicateCheckResult>(Json))!
            .Matches.ShouldHaveSingleItem();

        var suggestions = await Client.PostAsJsonAsync("/api/import/split-suggestions",
            new SplitSuggestionRequest(["UBER EATS TORONTO"]), Json);
        suggestions.EnsureSuccessStatusCode();
        (await suggestions.Content.ReadFromJsonAsync<SplitSuggestionResult>(Json))!
            .Suggestions.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Open_conflicts_are_readable_and_resolvable_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;

        var created = await Client.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(
            group.Id, alice, "Original", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, null, null, null, null), Json);
        var expense = await created.Content.ReadFromJsonAsync<ExpenseDto>(Json);

        // Two divergent offline edits, both branching from the stored revision.
        foreach (var (device, description) in new[] { ("device-x", "Edit X"), ("device-y", "Edit Y") })
        {
            var clock = new Dictionary<string, long>(expense!.VectorClock) { [device] = 9 };
            await Client.PostAsJsonAsync("/api/sync/push", new SyncPushRequest(device, [
                new SyncOperationDto(Guid.NewGuid(), SyncEntityType.Expense, expense.Id,
                    SyncOperation.Update, group.Id,
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        id = expense.Id, groupId = group.Id, paidByMemberId = alice,
                        description, amount = 40m, currency = "CAD", amountInBaseCurrency = 40m,
                        spentAt = TestData.Jan1,
                        splits = new[] { new { memberId = alice, amount = 40m, amountInBaseCurrency = 40m } }
                    }), clock, TestData.Jan1)
            ]), Json);
        }

        var conflicts = await Client.GetFromJsonAsync<List<SyncConflictDto>>("/api/sync/conflicts", Json);
        conflicts!.ShouldHaveSingleItem();

        var resolved = await Client.PostAsJsonAsync("/api/sync/conflicts/resolve",
            new ResolveConflictRequest(conflicts[0].ConflictId, ConflictResolution.KeepLocal, null), Json);
        resolved.EnsureSuccessStatusCode();

        (await Client.GetFromJsonAsync<List<SyncConflictDto>>("/api/sync/conflicts", Json))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_recurring_rule_can_be_paused_and_deleted_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;

        var created = await Client.PostAsJsonAsync("/api/expenses/recurring",
            new CreateRecurringExpenseRequest(
                group.Id, alice, "Rent", 1200m, "CAD", SplitType.Equal,
                [new SplitInputDto(alice, null)], RecurrenceUnit.Month, 1, 1, null,
                TestData.Jan1, null, null), Json);
        var rule = await created.Content.ReadFromJsonAsync<RecurringExpenseDto>(Json);

        var paused = await Client.PostAsync($"/api/expenses/recurring/{rule!.Id}/pause?paused=true", null);
        paused.EnsureSuccessStatusCode();
        (await paused.Content.ReadFromJsonAsync<RecurringExpenseDto>(Json))!.IsPaused.ShouldBeTrue();

        var listed = await Client.GetFromJsonAsync<List<RecurringExpenseDto>>(
            $"/api/expenses/recurring?groupId={group.Id}", Json);
        listed!.ShouldHaveSingleItem();

        (await Client.DeleteAsync($"/api/expenses/recurring/{rule.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_comment_can_be_deleted_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync();
        var alice = group.Members.Single().Id;
        var created = await Client.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest(
            group.Id, alice, "Dinner", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, null, null, null, null), Json);
        var expense = await created.Content.ReadFromJsonAsync<ExpenseDto>(Json);

        var comment = await Client.PostAsJsonAsync($"/api/expenses/{expense!.Id}/comments",
            new CreateCommentRequest(expense.Id, "Mine", null, null), Json);
        var posted = await comment.Content.ReadFromJsonAsync<CommentDto>(Json);

        (await Client.DeleteAsync($"/api/expenses/comments/{posted!.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_settlement_can_be_listed_and_deleted_over_http()
    {
        await SignInAsync();
        var group = await CreateGroupAsync("Roommates", "Bob");
        var alice = group.Members.First(m => m.UserId is not null).Id;
        var bob = group.Members.First(m => m.DisplayName == "Bob").Id;

        var created = await Client.PostAsJsonAsync("/api/settlements",
            new SplitEverything.Application.Contracts.Settlements.CreateSettlementRequest(
                group.Id, bob, alice, 25m, "CAD", TestData.Jan1, null, null, null), Json);
        var settlement = await created.Content.ReadFromJsonAsync<
            SplitEverything.Application.Contracts.Settlements.SettlementDto>(Json);

        var listed = await Client.GetFromJsonAsync<List<SplitEverything.Application.Contracts.Settlements.SettlementDto>>(
            $"/api/settlements?groupId={group.Id}", Json);
        listed!.ShouldHaveSingleItem();

        (await Client.DeleteAsync($"/api/settlements/{settlement!.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_push_subscription_can_be_unregistered_over_http()
    {
        await SignInAsync();
        await Client.PostAsJsonAsync("/api/notifications",
            new SplitEverything.Application.Contracts.Notifications.RegisterPushRequest(
                PushChannel.Fcm, "fcm-token", null, null, null), Json);

        var response = await Client.DeleteAsync("/api/notifications?endpoint=fcm-token");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private const string CsvBody =
        "Date,Purpose,Category,Currency,Amount,Who paid,For whom\n" +
        "2026-01-05,Groceries,Groceries,CAD,84.32,Alice,\"Alice, Bob\"\n" +
        "2026-01-07,Hydro,Utilities,CAD,120.00,Bob,\"Alice, Bob\"\n";

    private async Task<HttpResponseMessage> PostCsvAsync(string url, object request)
    {
        using var content = new MultipartFormDataContent();
        var csv = new StringContent(CsvBody);
        csv.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(csv, "file", "export.csv");
        content.Add(new StringContent(System.Text.Json.JsonSerializer.Serialize(request, Json)), "request");

        return await Client.PostAsync(url, content);
    }

    private async Task<GroupDto> CreateGroupAsync(string name = "Roommates", params string[] placeholders)
    {
        var response = await Client.PostAsJsonAsync("/api/groups",
            new CreateGroupRequest(name, "CAD", null, null, null, placeholders), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GroupDto>(Json))!;
    }
}
