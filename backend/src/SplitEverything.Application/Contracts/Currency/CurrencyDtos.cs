namespace SplitEverything.Application.Contracts.Currency;

public sealed record ExchangeRateDto(string From, string To, DateOnly Date, decimal Rate);
