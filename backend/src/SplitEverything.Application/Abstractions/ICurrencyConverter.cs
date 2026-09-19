namespace SplitEverything.Application.Abstractions;

public sealed record ConversionResult(decimal Amount, decimal Rate, DateTimeOffset RateAsOf);

public interface ICurrencyConverter
{
    Task<ConversionResult> ConvertAsync(
        decimal amount, string fromCurrency, string toCurrency,
        DateTimeOffset? asOf = null, CancellationToken ct = default);

    Task<decimal> GetRateAsync(
        string fromCurrency, string toCurrency,
        DateTimeOffset? asOf = null, CancellationToken ct = default);

    Task RefreshCacheAsync(IEnumerable<string> currencies, CancellationToken ct = default);
}
