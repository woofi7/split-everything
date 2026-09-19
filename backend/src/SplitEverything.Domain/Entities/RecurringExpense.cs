using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

public class RecurringExpense : SyncableEntity
{
    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    public Guid PaidByMemberId { get; set; }
    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CAD";
    public SplitType SplitType { get; set; } = SplitType.Equal;

    public string SplitTemplateJson { get; set; } = "[]";

    public RecurrenceUnit Unit { get; set; } = RecurrenceUnit.Month;
    public int Interval { get; set; } = 1;

    public int? DayOfMonth { get; set; }

    public DayOfWeek? DayOfWeek { get; set; }

    public DateTimeOffset StartsOn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndsOn { get; set; }
    public int? MaxOccurrences { get; set; }
    public int OccurrenceCount { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset NextRunAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsPaused { get; set; }
}
