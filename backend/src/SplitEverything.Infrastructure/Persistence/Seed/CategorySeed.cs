using SplitEverything.Domain.Entities;

namespace SplitEverything.Infrastructure.Persistence.Seed;

/// <summary>
/// System categories, plus the starter merchant ruleset the statement importer
/// begins from before it learns anything from the user's corrections.
/// </summary>
public static class CategorySeed
{
    public sealed record SeedCategory(string Key, string Name, string IconName, string ColorHex, int SortOrder);

    public static readonly IReadOnlyList<SeedCategory> Categories =
    [
        new("groceries", "Groceries", "cart-shopping", "#16a34a", 10),
        new("dining", "Dining out", "utensils", "#f97316", 20),
        new("transport", "Transport", "car", "#0ea5e9", 30),
        new("housing", "Rent and housing", "house", "#8b5cf6", 40),
        new("utilities", "Utilities", "bolt", "#eab308", 50),
        new("entertainment", "Entertainment", "ticket-simple", "#ec4899", 60),
        new("travel", "Travel", "plane", "#06b6d4", 70),
        new("health", "Health", "kit-medical", "#ef4444", 80),
        new("shopping", "Shopping", "bag-shopping", "#a855f7", 90),
        new("subscriptions", "Subscriptions", "calendar-days", "#6366f1", 100),
        new("pets", "Pets", "paw", "#84cc16", 110),
        new("gifts", "Gifts", "gift", "#f43f5e", 120),
        new("fees", "Fees and interest", "building-columns", "#64748b", 130),
        new("other", "Other", "ellipsis", "#94a3b8", 999)
    ];

    /// <summary>Keyword to category key. Matched as an upper-cased substring.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultMerchantRules =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["UBER EATS"] = "dining",
            ["DOORDASH"] = "dining",
            ["SKIP THE DISHES"] = "dining",
            ["SKIPTHEDISHES"] = "dining",
            ["STARBUCKS"] = "dining",
            ["TIM HORTONS"] = "dining",
            ["MCDONALD"] = "dining",
            ["RESTAURANT"] = "dining",
            ["METRO"] = "groceries",
            ["LOBLAW"] = "groceries",
            ["IGA"] = "groceries",
            ["SUPERSTORE"] = "groceries",
            ["COSTCO"] = "groceries",
            ["WALMART"] = "groceries",
            ["MAXI"] = "groceries",
            ["PROVIGO"] = "groceries",
            ["UBER"] = "transport",
            ["LYFT"] = "transport",
            ["PETRO"] = "transport",
            ["SHELL"] = "transport",
            ["ESSO"] = "transport",
            ["STM"] = "transport",
            ["VIA RAIL"] = "transport",
            ["HYDRO"] = "utilities",
            ["BELL"] = "utilities",
            ["VIDEOTRON"] = "utilities",
            ["ROGERS"] = "utilities",
            ["TELUS"] = "utilities",
            ["FIZZ"] = "utilities",
            ["NETFLIX"] = "subscriptions",
            ["SPOTIFY"] = "subscriptions",
            ["DISNEY"] = "subscriptions",
            ["APPLE.COM/BILL"] = "subscriptions",
            ["GOOGLE STORAGE"] = "subscriptions",
            ["AMAZON"] = "shopping",
            ["AMZN"] = "shopping",
            ["CINEPLEX"] = "entertainment",
            ["STEAM"] = "entertainment",
            ["AIR CANADA"] = "travel",
            ["AIRBNB"] = "travel",
            ["BOOKING.COM"] = "travel",
            ["PHARMAPRIX"] = "health",
            ["JEAN COUTU"] = "health",
            ["PHARMACY"] = "health",
            ["MONDOU"] = "pets",
            ["INTEREST"] = "fees",
            ["SERVICE CHARGE"] = "fees",
            ["NSF FEE"] = "fees"
        };

    public static IEnumerable<Category> BuildSystemCategories()
        => Categories.Select(c => new Category
        {
            // Deterministic ids so a re-seed does not duplicate, and so tests can
            // reference a category without a lookup.
            Id = DeterministicId(c.Key),
            Key = c.Key,
            Name = c.Name,
            IconName = c.IconName,
            ColorHex = c.ColorHex,
            SortOrder = c.SortOrder,
            OwnerUserId = null
        });

    public static Guid DeterministicId(string key)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"split-everything:category:{key}"));
        return new Guid(bytes);
    }
}
