using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Auth;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Tests.Support;

[Collection(PostgresCollection.Name)]
public abstract class ApiTestBase(PostgresFixture fixture) : IAsyncLifetime
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    protected ApiTestFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        Factory = new ApiTestFactory(fixture.ConnectionString);
        await Factory.InitializeDatabaseAsync();
        await ResetAsync();

        Factory.Currency
            .ConvertAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                new ConversionResult(call.Arg<decimal>(), 1m, DateTimeOffset.UtcNow)));
        Factory.Currency
            .GetRateAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1m));

        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Device-Id", TestData.DeviceA);
    }

    public virtual Task DisposeAsync()
    {
        Client.Dispose();
        Factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Signs in through the real endpoint and attaches the bearer token.</summary>
    protected async Task<AuthenticatedUser> SignInAsync(
        string name = "Alice", string? email = null, string? googleSub = null)
    {
        var identity = new GoogleIdentity(
            googleSub ?? $"google-{name.ToLowerInvariant()}",
            email ?? $"{name.ToLowerInvariant()}@example.com",
            true, name, null);

        Factory.Google.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(identity));

        var response = await Client.PostAsJsonAsync("/api/auth/google",
            new GoogleSignInRequest("google-id-token", TestData.DeviceA, "Test", "web"), Json);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SignInResult>(Json)
                     ?? throw new InvalidOperationException("Sign-in returned no body.");

        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", result.Tokens.AccessToken);

        return result.User;
    }

    /// <summary>A second, independently authenticated client, for access tests.</summary>
    protected async Task<HttpClient> SignInAsAnotherUserAsync(string name)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Id", TestData.DeviceB);

        Factory.Google.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GoogleIdentity(
                $"google-{name.ToLowerInvariant()}", $"{name.ToLowerInvariant()}@example.com",
                true, name, null)));

        var response = await client.PostAsJsonAsync("/api/auth/google",
            new GoogleSignInRequest("google-id-token", TestData.DeviceB, "Other", "web"), Json);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SignInResult>(Json)!;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", result!.Tokens.AccessToken);

        return client;
    }

    protected AppDbContext NewContext() => fixture.CreateContext();

    private async Task ResetAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tables = new[]
        {
            "expense_item_shares", "expense_items", "expense_splits", "expense_comments",
            "expense_revisions", "expenses", "recurring_expenses", "settlements",
            "sync_log", "sync_snapshots", "sync_conflicts", "activity_log",
            "group_invites", "group_lineage_links", "group_members", "groups",
            "import_batches", "receipts",
            "push_subscriptions", "devices", "refresh_tokens", "users", "exchange_rates"
        };

        // EF cannot tell a table list from user input, and this one is the constant
        // above: a parameter cannot carry a table name anyway, so there is nothing
        // to parameterise here.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"TRUNCATE TABLE {string.Join(", ", tables)} RESTART IDENTITY CASCADE;");
#pragma warning restore EF1002

    }
}
