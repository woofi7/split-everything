using SplitEverything.Application.Abstractions;

namespace SplitEverything.Tests.Support;

/// <summary>Settable clock, so schedules and expiries are asserted rather than slept through.</summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
