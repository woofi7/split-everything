namespace SplitEverything.Domain.Entities;

/// <summary>
/// Merchant-to-category rule used by the client-side statement importer.
///
/// This is user preference data, not statement content: only the keyword and the
/// category it maps to are stored, and it syncs through the ordinary sync log so
/// corrections made on one device improve categorisation on the others.
/// </summary>
public class CategoryRule : SyncableEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Upper-cased substring matched against the statement description.</summary>
    public string Keyword { get; set; } = string.Empty;

    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Suggested group for matches, when the user keeps assigning them to one group.</summary>
    public Guid? SuggestedGroupId { get; set; }

    /// <summary>Higher wins when several rules match one line.</summary>
    public int Weight { get; set; } = 1;

    /// <summary>Times the user accepted this rule's suggestion; feeds the weight.</summary>
    public int HitCount { get; set; }

    /// <summary>False once the user has corrected it, so we stop re-applying a bad guess.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>True for the built-in starter set, false for rules learned from corrections.</summary>
    public bool IsBuiltIn { get; set; }
}
