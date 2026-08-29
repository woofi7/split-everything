using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Api.BackgroundJobs;

/// <summary>
/// Warms the daily rate cache for the currencies actually in use, so the first
/// expense of the day does not wait on Frankfurter.
/// </summary>
public sealed class ExchangeRateWorker(
    IServiceScopeFactory scopes,
    ILogger<ExchangeRateWorker> logger,
    WorkerSchedule? schedule = null) : BackgroundService
{
    private readonly WorkerSchedule _schedule = schedule ?? WorkerSchedule.ExchangeRates;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_schedule.StartupDelay > TimeSpan.Zero)
            await Task.Delay(_schedule.StartupDelay, stoppingToken);

        using var timer = new PeriodicTimer(_schedule.Interval);

        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var currency = scope.ServiceProvider.GetRequiredService<ICurrencyConverter>();

                // Only the currencies this install actually uses: no point fetching
                // the whole table for a handful of groups.
                var codes = await db.Groups.Select(g => g.BaseCurrency)
                    .Union(db.Users.Select(u => u.DefaultCurrency))
                    .Union(db.Expenses.Where(e => !e.IsDeleted).Select(e => e.Currency))
                    .Distinct()
                    .ToListAsync(stoppingToken);

                if (codes.Count > 1)
                {
                    await currency.RefreshCacheAsync(codes, stoppingToken);
                    logger.LogInformation("Refreshed exchange rates for {Count} currencies", codes.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exchange rate refresh failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
