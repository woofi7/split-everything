namespace SplitEverything.Domain.Entities;

/// <summary>Daily Frankfurter rate, cached so conversions do not hit the network per expense.</summary>
public class ExchangeRateSnapshot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string BaseCurrency { get; set; } = string.Empty;
    public string QuoteCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateOnly RateDate { get; set; }
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;
}
