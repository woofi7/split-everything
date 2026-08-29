using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

/// <summary>
/// Template plus schedule. A background worker materialises real expenses from it,
/// so an occurrence behaves exactly like a hand-entered expense once created.
/// </summary>
public class RecurringExpense : SyncableEntity
{
    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    public Guid PaidByMemberId { get; set; }
    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CAD";
    public Guid? CategoryId { get; set; }
    public SplitType SplitType { get; set; } = SplitType.Equal;

    /// <summary>Split template: member id -> input value, applied to every occurrence.</summary>
    public string SplitTemplateJson { get; set; } = "[]";

    public RecurrenceUnit Unit { get; set; } = RecurrenceUnit.Month;
    public int Interval { get; set; } = 1;

    /// <summary>Day of month for monthly/yearly rules, clamped to the month's length.</summary>
    public int? DayOfMonth { get; set; }

    /// <summary>Day of week for weekly rules.</summary>
    public DayOfWeek? DayOfWeek { get; set; }

    public DateTimeOffset StartsOn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndsOn { get; set; }
    public int? MaxOccurrences { get; set; }
    public int OccurrenceCount { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset NextRunAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsPaused { get; set; }
}
