namespace SplitEverything.Domain.Entities;

/// <summary>
/// What an expense was for.
///
/// Two scopes in one table. A row with no group is the server's own list, which is
/// what every group starts from and what the person who runs the server maintains.
/// A row with a group belongs to that group alone, and the moment a group edits its
/// categories it takes a copy of the whole list: from then on it is theirs, and a
/// later change to the server's list leaves it as they left it. A household that
/// renamed "Dining out" to "Resto" did not ask to have it renamed back.
///
/// The key is the identity - it is what an expense stores - so renaming a category
/// keeps every expense filed under it, and deleting one leaves those expenses
/// pointing at a name nobody recognises rather than at nothing. That is deliberate:
/// the alternative is a foreign key that either blocks the delete or silently
/// empties a year of expenses.
/// </summary>
public class Category
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Stable slug, unique within its scope. This is what an expense stores.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Font Awesome solid icon name, the same vocabulary a group's icon uses.</summary>
    public string IconName { get; set; } = "tag";

    public string ColorHex { get; set; } = "#64748b";

    public int SortOrder { get; set; }

    /// <summary>Null for the server's own list, which every group starts from.</summary>
    public Guid? GroupId { get; set; }
    public Group? Group { get; set; }

    /// <summary>
    /// Words that file an expense here without anybody choosing, as a JSON array.
    /// Matched against the description, longest first, so "uber eats" beats "uber"
    /// and a takeaway is not filed as a taxi.
    /// </summary>
    public string? KeywordsJson { get; set; }
}
