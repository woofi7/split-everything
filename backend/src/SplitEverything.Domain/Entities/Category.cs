namespace SplitEverything.Domain.Entities;

/// <summary>
/// Expense category. Seeded globally (OwnerUserId null) and extendable per user.
/// </summary>
public class Category
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IconName { get; set; } = "*";
    public string ColorHex { get; set; } = "#64748b";
    public int SortOrder { get; set; }
    public Guid? OwnerUserId { get; set; }
    public bool IsSystem => OwnerUserId is null;
}
