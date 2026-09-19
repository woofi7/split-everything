using SplitEverything.Domain.Entities;

namespace SplitEverything.Infrastructure.Persistence.Seed;

public static class CategorySeed
{
    public sealed record SeedCategory(
        string Key, string Name, string IconName, string ColorHex, int SortOrder, string[] Keywords);

    public static readonly IReadOnlyList<SeedCategory> Categories =
    [
        new("groceries", "Groceries", "cart-shopping", "#16a34a", 10,
            ["epicerie", "grocery", "metro", "iga", "maxi", "provigo", "loblaw", "superstore",
             "costco", "walmart", "supermarche", "marche", "lufa"]),
        new("dining", "Dining out", "utensils", "#f97316", 20,
            ["restaurant", "resto", "cafe", "bar ", "pub", "uber eats", "doordash",
             "skip the dishes", "skipthedishes", "starbucks", "tim hortons", "mcdonald",
             "pizza", "sushi", "brasserie", "depanneur"]),
        new("transport", "Transport", "car", "#0ea5e9", 30,
            ["uber", "lyft", "taxi", "communauto", "bixi", "stm", "exo", "via rail",
             "essence", "petro", "shell", "esso", "ultramar", "parking", "stationnement",
             "garage", "pneus"]),
        new("housing", "Rent and housing", "house", "#8b5cf6", 40,
            ["loyer", "rent", "hypotheque", "mortgage", "bail", "assurance habitation",
             "concierge", "reno", "quincaillerie", "rona", "home depot", "ikea"]),
        new("utilities", "Bills", "bolt", "#eab308", 50,
            ["hydro", "gaz metro", "energir", "bell", "videotron", "rogers", "telus",
             "fizz", "koodo", "internet", "telephone", "facture"]),
        new("entertainment", "Fun", "ticket-simple", "#ec4899", 60,
            ["cinema", "cineplex", "spectacle", "billet", "ticket", "steam", "jeu",
             "musee", "theatre", "concert", "escalade", "ski"]),
        new("travel", "Travel", "plane", "#06b6d4", 70,
            ["air canada", "airbnb", "booking.com", "hotel", "auberge", "vol ", "flight",
             "west jet", "westjet", "porter", "camping", "sepaq"]),
        new("health", "Health", "kit-medical", "#ef4444", 80,
            ["pharmaprix", "jean coutu", "pharmacie", "pharmacy", "clinique", "dentiste",
             "optometriste", "physio", "lunettes"]),
        new("shopping", "Shopping", "bag-shopping", "#a855f7", 90,
            ["amazon", "amzn", "simons", "winners", "decathlon", "sports experts",
             "vetements", "chaussures", "canadian tire"]),
        new("subscriptions", "Subscriptions", "calendar-days", "#6366f1", 100,
            ["netflix", "spotify", "disney", "crave", "apple.com/bill", "icloud",
             "google storage", "abonnement", "patreon", "youtube premium"]),
        new("pets", "Pets", "paw", "#84cc16", 110,
            ["mondou", "veterinaire", "veto", "animalerie", "croquettes"]),
        new("gifts", "Gifts", "gift", "#f43f5e", 120,
            ["cadeau", "gift", "anniversaire", "noel"]),
        new("fees", "Fees", "building-columns", "#64748b", 130,
            ["frais", "interet", "interest", "service charge", "nsf", "penalite"]),
        new("other", "Other", "ellipsis", "#94a3b8", 999, []),
    ];

    public static IEnumerable<Category> BuildGlobalCategories()
        => Categories.Select(category => new Category
        {
            Id = DeterministicId(category.Key),
            Key = category.Key,
            Name = category.Name,
            IconName = category.IconName,
            ColorHex = category.ColorHex,
            SortOrder = category.SortOrder,
            GroupId = null,
            KeywordsJson = System.Text.Json.JsonSerializer.Serialize(category.Keywords),
        });

    public static Guid DeterministicId(string key)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"split-everything:category:{key}"));
        return new Guid(bytes);
    }
}
