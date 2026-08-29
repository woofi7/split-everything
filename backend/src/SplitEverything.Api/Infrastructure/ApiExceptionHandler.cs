using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Common;

namespace SplitEverything.Api.Infrastructure;

/// <summary>
/// Turns the application's deliberate failures into problem responses, so a
/// validation mistake is a 400 with a readable message rather than a 500, and
/// anything unexpected is a 500 that says nothing about internals.
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        var problem = exception switch
        {
            AppException app => new ProblemDetails
            {
                Status = app.StatusCode,
                Title = app.Code,
                Detail = app.Message
            },
            OperationCanceledException => null,
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "ServerError",
                Detail = "Something went wrong. Try again."
            }
        };

        if (problem is null) return false;

        if (problem.Status >= 500)
            logger.LogError(exception, "Unhandled error on {Method} {Path}",
                context.Request.Method, context.Request.Path);
        else
            logger.LogInformation("{Title} on {Method} {Path}: {Detail}",
                problem.Title, context.Request.Method, context.Request.Path, problem.Detail);

        problem.Instance = context.Request.Path;
        context.Response.StatusCode = problem.Status!.Value;
        await context.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}
