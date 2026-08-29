namespace SplitEverything.Application.Abstractions;

/// <summary>Injectable clock, so schedule and expiry logic is testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
