using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Currency;

/// <summary>
/// Frankfurter-backed conversion with a daily cache in Postgres.
///
/// Rates are cached per (base, quote, date), so a group's expenses hit the network
/// once a day at most. If Frankfurter is unreachable and we hold any earlier rate,
/// that is used instead: a slightly stale rate is far better than refusing to
/// record an expense, and the rate used is frozen onto the expense anyway.
/// </summary>
public sealed class FrankfurterCurrencyConverter(
    HttpClient http,
    AppDbContext db,
    IClock clock,
    ILogger<FrankfurterCurrencyConverter> logger) : ICurrencyConverter
{
    public async Task<ConversionResult> ConvertAsync(
        decimal amount, string fromCurrency, string toCurrency,
        DateTimeOffset? asOf = null, CancellationToken ct = default)
    {
        var from = Normalize(fromCurrency);
        var to = Normalize(toCurrency);

        if (from == to) return new ConversionResult(amount, 1m, clock.UtcNow);

        var rate = await GetRateAsync(from, to, asOf, ct);

        return new ConversionResult(
            CurrencyPrecision.Round(amount * rate, to),
            rate,
            asOf ?? clock.UtcNow);
    }

    public async Task<decimal> GetRateAsync(
        string fromCurrency, string toCurrency,
        DateTimeOffset? asOf = null, CancellationToken ct = default)
    {
        var from = Normalize(fromCurrency);
        var to = Normalize(toCurrency);
        if (from == to) return 1m;

        var date = DateOnly.FromDateTime((asOf ?? clock.UtcNow).UtcDateTime);

        var cached = await db.ExchangeRates
            .Where(r => r.BaseCurrency == from && r.QuoteCurrency == to && r.RateDate == date)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync(ct);
        if (cached is not null) return cached.Value;

        try
        {
            var rates = await FetchAsync(from, [to], date, ct);
            if (!rates.TryGetValue(to, out var rate))
                throw new ValidationException($"No {from} to {to} rate is available.");

            await CacheAsync(from, rates, date, ct);
            return rate;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Frankfurter lookup failed for {From}->{To}", from, to);

            var fallback = await MostRecentAsync(from, to, ct);
            if (fallback is not null)
            {
                logger.LogInformation("Using a cached {From}->{To} rate from an earlier day", from, to);
                return fallback.Value;
            }

            throw new CurrencyUnavailableException(
                $"Could not get a {from} to {to} exchange rate. Try again shortly.");
        }
    }

    public async Task RefreshCacheAsync(IEnumerable<string> currencies, CancellationToken ct = default)
    {
        var codes = currencies
            .Select(Normalize)
            .Distinct()
            .ToList();

        var date = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        foreach (var from in codes)
        {
            var wanted = codes.Where(c => c != from).ToList();
            if (wanted.Count == 0) continue;

            try
            {
                var rates = await FetchAsync(from, wanted, date, ct);
                await CacheAsync(from, rates, date, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // A nightly warm-up is best effort; the on-demand path still works.
                logger.LogWarning(ex, "Could not refresh rates based on {From}", from);
            }
        }
    }

    private async Task<Dictionary<string, decimal>> FetchAsync(
        string from, IReadOnlyList<string> to, DateOnly date, CancellationToken ct)
    {
        var path = $"v1/{date:yyyy-MM-dd}?base={from}&symbols={string.Join(',', to)}";

        using var response = await http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Frankfurter returned {(int)response.StatusCode}.");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!document.RootElement.TryGetProperty("rates", out var rates)
            || rates.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Frankfurter response had no rates.");
        }

        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in rates.EnumerateObject())
        {
            if (property.Value.TryGetDecimal(out var value))
                result[property.Name.ToUpperInvariant()] = value;
        }

        return result;
    }

    private async Task CacheAsync(
        string from, Dictionary<string, decimal> rates, DateOnly date, CancellationToken ct)
    {
        foreach (var (to, rate) in rates)
        {
            var exists = await db.ExchangeRates.AnyAsync(r =>
                r.BaseCurrency == from && r.QuoteCurrency == to && r.RateDate == date, ct);
            if (exists) continue;

            db.ExchangeRates.Add(new ExchangeRateSnapshot
            {
                BaseCurrency = from,
                QuoteCurrency = to,
                Rate = rate,
                RateDate = date,
                FetchedAt = clock.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task<decimal?> MostRecentAsync(string from, string to, CancellationToken ct)
        => await db.ExchangeRates
            .Where(r => r.BaseCurrency == from && r.QuoteCurrency == to)
            .OrderByDescending(r => r.RateDate)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync(ct);

    private static string Normalize(string currency)
    {
        var trimmed = currency?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetterUpper))
            throw new ValidationException($"'{currency}' is not a currency code.");
        return trimmed;
    }
}

/// <summary>Raised when no rate can be obtained and none is cached.</summary>
public sealed class CurrencyUnavailableException(string message) : AppException(message)
{
    public override int StatusCode => 503;
    public override string Code => "CurrencyUnavailable";
}
