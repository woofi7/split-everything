using Shouldly;
using SplitEverything.Domain.Common;

namespace SplitEverything.Tests.Domain;

public class CurrencyPrecisionTests
{
    [Theory]
    [InlineData("CAD", 2)]
    [InlineData("USD", 2)]
    [InlineData("EUR", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KRW", 0)]
    [InlineData("KWD", 3)]
    [InlineData("TND", 3)]
    public void Known_currencies_report_their_minor_unit_count(string currency, int expected)
        => CurrencyPrecision.DecimalsFor(currency).ShouldBe(expected);

    [Fact]
    public void Currency_lookup_is_case_insensitive()
        => CurrencyPrecision.DecimalsFor("jpy").ShouldBe(0);

    [Theory]
    [InlineData(null)]
    [InlineData("ZZZ")]
    public void Unknown_or_missing_currencies_fall_back_to_two_decimals(string? currency)
        => CurrencyPrecision.DecimalsFor(currency).ShouldBe(CurrencyPrecision.Default);

    [Theory]
    [InlineData("CAD", 0.01)]
    [InlineData("JPY", 1)]
    [InlineData("KWD", 0.001)]
    public void The_minor_unit_matches_the_decimal_count(string currency, double expected)
        => CurrencyPrecision.MinorUnit(currency).ShouldBe((decimal)expected);

    [Fact]
    public void Rounding_respects_the_currency()
    {
        CurrencyPrecision.Round(1.005m, "CAD").ShouldBe(1.00m);
        CurrencyPrecision.Round(1.6m, "JPY").ShouldBe(2m);
        CurrencyPrecision.Round(1.0005m, "KWD").ShouldBe(1.000m);
    }
}
