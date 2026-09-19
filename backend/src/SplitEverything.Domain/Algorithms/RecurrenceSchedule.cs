using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Algorithms;

public static class RecurrenceSchedule
{
    public static DateTimeOffset Next(
        DateTimeOffset from,
        RecurrenceUnit unit,
        int interval,
        int? dayOfMonth = null,
        DayOfWeek? dayOfWeek = null)
    {
        if (interval < 1) throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be at least 1.");

        return unit switch
        {
            RecurrenceUnit.Day => from.AddDays(interval),
            RecurrenceUnit.Week => NextWeekly(from, interval, dayOfWeek),
            RecurrenceUnit.Month => ClampToDay(from.AddMonths(interval), dayOfMonth ?? from.Day),
            RecurrenceUnit.Year => ClampToDay(from.AddYears(interval), dayOfMonth ?? from.Day),
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown recurrence unit.")
        };
    }

    public static IReadOnlyList<DateTimeOffset> Occurrences(
        DateTimeOffset start,
        DateTimeOffset after,
        DateTimeOffset until,
        RecurrenceUnit unit,
        int interval,
        int? dayOfMonth = null,
        DayOfWeek? dayOfWeek = null,
        int maxOccurrences = 500)
    {
        var result = new List<DateTimeOffset>();
        var cursor = start;

        if (cursor > after && cursor <= until) result.Add(cursor);

        while (result.Count < maxOccurrences)
        {
            cursor = Next(cursor, unit, interval, dayOfMonth, dayOfWeek);
            if (cursor > until) break;
            if (cursor > after) result.Add(cursor);
        }

        return result;
    }

    private static DateTimeOffset NextWeekly(DateTimeOffset from, int interval, DayOfWeek? dayOfWeek)
    {
        var candidate = from.AddDays(7 * interval);
        if (dayOfWeek is null || candidate.DayOfWeek == dayOfWeek) return candidate;

        var shift = ((int)dayOfWeek.Value - (int)candidate.DayOfWeek + 7) % 7;
        return candidate.AddDays(shift);
    }

    private static DateTimeOffset ClampToDay(DateTimeOffset value, int day)
    {
        var lastDay = DateTime.DaysInMonth(value.Year, value.Month);
        var target = Math.Clamp(day, 1, lastDay);
        return new DateTimeOffset(value.Year, value.Month, target,
            value.Hour, value.Minute, value.Second, value.Offset);
    }
}
