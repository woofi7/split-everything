using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SplitEverything.Api.Infrastructure;
using SplitEverything.Application.Common;
using SplitEverything.Infrastructure.Currency;

namespace SplitEverything.Tests.Api;

/// <summary>
/// The mapping from the application's deliberate failures to status codes. Getting
/// this wrong turns a validation mistake into a 500 and hands internals to callers.
/// </summary>
public class ExceptionHandlerTests
{
    private static readonly ApiExceptionHandler Handler = new(NullLogger<ApiExceptionHandler>.Instance);

    private static (DefaultHttpContext Context, MemoryStream Body) NewContext()
    {
        var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        context.Request.Path = "/api/groups";
        context.Request.Method = "POST";
        return (context, body);
    }

    private static async Task<(int Status, string Body)> HandleAsync(Exception exception)
    {
        var (context, body) = NewContext();

        (await Handler.TryHandleAsync(context, exception, CancellationToken.None)).ShouldBeTrue();

        body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(body).ReadToEndAsync());
    }

    [Fact]
    public async Task A_validation_failure_is_a_400_with_its_message()
    {
        var (status, body) = await HandleAsync(new ValidationException("Group name is required."));

        status.ShouldBe(StatusCodes.Status400BadRequest);
        body.ShouldContain("Group name is required.");
        body.ShouldContain("Validation");
    }

    [Fact]
    public async Task A_missing_resource_is_a_404()
        => (await HandleAsync(new NotFoundException("Group 1"))).Status.ShouldBe(StatusCodes.Status404NotFound);

    [Fact]
    public async Task An_access_failure_is_a_403()
        => (await HandleAsync(new ForbiddenException())).Status.ShouldBe(StatusCodes.Status403Forbidden);

    [Fact]
    public async Task An_archived_group_write_is_a_409()
        => (await HandleAsync(new GroupArchivedException())).Status.ShouldBe(StatusCodes.Status409Conflict);

    [Fact]
    public async Task A_sync_conflict_is_a_409()
        => (await HandleAsync(new SyncConflictException())).Status.ShouldBe(StatusCodes.Status409Conflict);

    [Fact]
    public async Task An_unavailable_exchange_rate_is_a_503()
        => (await HandleAsync(new CurrencyUnavailableException("no rate")))
            .Status.ShouldBe(StatusCodes.Status503ServiceUnavailable);

    [Fact]
    public async Task An_unexpected_error_is_a_500_that_reveals_nothing()
    {
        var (status, body) = await HandleAsync(
            new InvalidOperationException("connection string: Password=hunter2"));

        status.ShouldBe(StatusCodes.Status500InternalServerError);
        body.ShouldNotContain("hunter2");
        body.ShouldContain("Something went wrong");
    }

    [Fact]
    public async Task A_cancelled_request_is_left_alone()
    {
        var (context, _) = NewContext();

        // The client hung up; writing a response body would be pointless and can
        // itself throw.
        (await Handler.TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task The_problem_response_names_the_path_that_failed()
    {
        var (_, body) = await HandleAsync(new NotFoundException("Group 1"));

        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("instance").GetString().ShouldBe("/api/groups");
    }
}
