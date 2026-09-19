using System.Text.Json;
using System.Text.Json.Serialization;
using SplitEverything.Domain.Common;

namespace SplitEverything.Infrastructure.Sync;

public static class SyncPayloads
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public sealed class SplitPayload
    {
        public Guid MemberId { get; set; }
        public decimal Amount { get; set; }
        public decimal? AmountInBaseCurrency { get; set; }
        public decimal? InputValue { get; set; }
    }

    public sealed class ItemPayload
    {
        public Guid? Id { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public int Quantity { get; set; } = 1;
        public int SortOrder { get; set; }
        public List<Guid> Members { get; set; } = [];
    }

    public sealed class ExpensePayload
    {
        public Guid Id { get; set; }
        public Guid GroupId { get; set; }
        public Guid PaidByMemberId { get; set; }
        public string? Description { get; set; }
        public decimal? Amount { get; set; }
        public string? Currency { get; set; }
        public decimal? AmountInBaseCurrency { get; set; }
        public decimal? ExchangeRate { get; set; }
        public DateTimeOffset? SpentAt { get; set; }
        public SplitType? SplitType { get; set; }
        public Guid? ReceiptId { get; set; }
        public string? Notes { get; set; }

        public string? CategoryKey { get; set; }

        public List<SplitPayload> Splits { get; set; } = [];
        public List<ItemPayload> Items { get; set; } = [];

        public List<PayerPayload> Payers { get; set; } = [];
    }

    public sealed class PayerPayload
    {
        public Guid MemberId { get; set; }
        public decimal Amount { get; set; }
        public decimal? AmountInBaseCurrency { get; set; }
    }

    public sealed class SettlementPayload
    {
        public Guid Id { get; set; }
        public Guid GroupId { get; set; }
        public Guid FromMemberId { get; set; }
        public Guid ToMemberId { get; set; }
        public decimal? Amount { get; set; }
        public string? Currency { get; set; }
        public decimal? AmountInBaseCurrency { get; set; }
        public DateTimeOffset? SettledAt { get; set; }
        public string? Note { get; set; }
    }

    public sealed class CommentPayload
    {
        public Guid Id { get; set; }
        public Guid ExpenseId { get; set; }
        public Guid GroupId { get; set; }
        public Guid AuthorMemberId { get; set; }
        public Guid? ParentCommentId { get; set; }
        public string? Body { get; set; }
    }

    public static T? Parse<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
