using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Services;

namespace SplitEverything.Api.BackgroundJobs;

/// <summary>
/// Materialises due recurring expenses.
///
/// Runs hourly rather than daily so a container restart cannot skip a day, and the
/// service itself backfills anything missed, so the schedule is a hint rather than
/// a guarantee the data depends on.
/// </summary>
public sealed class RecurringExpenseWorker(
    IServiceScopeFactory scopes,
    IClock clock,
    ILogger<RecurringExpenseWorker> logger,
    WorkerSchedule? schedule = null) : BackgroundService
{
    private readonly WorkerSchedule _schedule = schedule ?? WorkerSchedule.Recurring;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting and migrations finish applying first.
        if (_schedule.StartupDelay > TimeSpan.Zero)
            await Task.Delay(_schedule.StartupDelay, stoppingToken);

        using var timer = new PeriodicTimer(_schedule.Interval);

        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var recurring = scope.ServiceProvider.GetRequiredService<IRecurringExpenseService>();

                var created = await recurring.RunDueAsync(clock.UtcNow, stoppingToken);
                if (created > 0) logger.LogInformation("Created {Count} recurring expenses", created);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let a bad run kill the worker: the next tick retries, and the
                // backfill means nothing is lost in the meantime.
                logger.LogError(ex, "Recurring expense run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
