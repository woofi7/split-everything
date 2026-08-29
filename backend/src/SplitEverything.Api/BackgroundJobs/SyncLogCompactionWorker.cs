using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Services;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Api.BackgroundJobs;

/// <summary>
/// Yearly sync-log compaction, as the spec requires: settled history older than a
/// year is collapsed into a snapshot and trimmed from the live log, so the log does
/// not grow without bound.
///
/// Checked daily but only acts on a group whose last snapshot is over a year old,
/// which makes the schedule idempotent and cheap.
/// </summary>
public sealed class SyncLogCompactionWorker(
    IServiceScopeFactory scopes,
    IClock clock,
    ILogger<SyncLogCompactionWorker> logger,
    WorkerSchedule? schedule = null) : BackgroundService
{
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(365);

    private readonly WorkerSchedule _schedule = schedule ?? WorkerSchedule.Compaction;

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
                var lifecycle = scope.ServiceProvider.GetRequiredService<IGroupLifecycleService>();

                var cutoff = clock.UtcNow - RetentionWindow;

                var candidates = await db.Groups
                    .Where(g => db.SyncLog.Any(e => e.GroupId == g.Id && e.CreatedAt < cutoff))
                    .Select(g => g.Id)
                    .ToListAsync(stoppingToken);

                foreach (var groupId in candidates)
                {
                    var result = await lifecycle.CompactAsync(groupId, cutoff, stoppingToken);
                    if (result.CompactedEntries > 0)
                    {
                        logger.LogInformation(
                            "Compacted {Entries} sync log entries for group {GroupId} up to seq {Seq}",
                            result.CompactedEntries, groupId, result.UpToServerSeq);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sync log compaction failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
