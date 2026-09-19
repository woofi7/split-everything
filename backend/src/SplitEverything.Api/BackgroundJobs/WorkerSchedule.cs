namespace SplitEverything.Api.BackgroundJobs;

public sealed record WorkerSchedule(TimeSpan StartupDelay, TimeSpan Interval)
{
    public static WorkerSchedule Recurring { get; } = new(TimeSpan.FromSeconds(20), TimeSpan.FromHours(1));

    public static WorkerSchedule ExchangeRates { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromHours(6));

    public static WorkerSchedule Compaction { get; } = new(TimeSpan.FromMinutes(2), TimeSpan.FromHours(24));

    public static WorkerSchedule Immediate { get; } =
        new(TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
}
