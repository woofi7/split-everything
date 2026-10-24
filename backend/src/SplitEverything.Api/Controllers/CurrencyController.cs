using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Currency;

namespace SplitEverything.Api.Controllers;

public sealed class CurrencyController(
    ICurrentUser currentUser,
    ICurrencyConverter currency) : ApiControllerBase(currentUser)
{
    [HttpGet("rate")]
    public async Task<ActionResult<ExchangeRateDto>> Rate(
        [FromQuery] string from, [FromQuery] string to, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        var on = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var asOf = new DateTimeOffset(on.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var rate = await currency.GetRateAsync(from, to, asOf, ct);

        return Ok(new ExchangeRateDto(from.Trim().ToUpperInvariant(), to.Trim().ToUpperInvariant(), on, rate));
    }
}
