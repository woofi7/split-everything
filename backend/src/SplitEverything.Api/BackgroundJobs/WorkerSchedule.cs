namespace SplitEverything.Api.BackgroundJobs;

/// <summary>
/// How often a background worker ticks, and how long it waits before its first
/// tick.
///
/// Injectable rather than hardcoded so the loop body itself is testable: with a
/// real 20-second startup delay a test can only ever observe the worker starting
/// and stopping, never the work it exists to do.
/// </summary>
public sealed record WorkerSchedule(TimeSpan StartupDelay, TimeSpan Interval)
{
    /// <summary>Hourly, so a container restart cannot skip a day of occurrences.</summary>
    public static WorkerSchedule Recurring { get; } = new(TimeSpan.FromSeconds(20), TimeSpan.FromHours(1));

    public static WorkerSchedule ExchangeRates { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromHours(6));

    /// <summary>Checked daily; only acts on history older than the retention window.</summary>
    public static WorkerSchedule Compaction { get; } = new(TimeSpan.FromMinutes(2), TimeSpan.FromHours(24));

    /// <summary>Ticks immediately and often. Tests only.</summary>
    public static WorkerSchedule Immediate { get; } =
        new(TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
}
