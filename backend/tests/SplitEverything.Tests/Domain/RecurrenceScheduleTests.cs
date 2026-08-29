using Shouldly;
using SplitEverything.Domain.Algorithms;
using SplitEverything.Domain.Common;

namespace SplitEverything.Tests.Domain;

public class RecurrenceScheduleTests
{
    private static DateTimeOffset Date(int year, int month, int day, int hour = 9)
        => new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Daily_advances_by_the_interval()
        => RecurrenceSchedule.Next(Date(2026, 1, 1), RecurrenceUnit.Day, 3)
            .ShouldBe(Date(2026, 1, 4));

    [Fact]
    public void Weekly_advances_a_whole_number_of_weeks()
        => RecurrenceSchedule.Next(Date(2026, 1, 1), RecurrenceUnit.Week, 2)
            .ShouldBe(Date(2026, 1, 15));

    [Fact]
    public void Weekly_with_a_target_weekday_lands_on_that_day()
    {
        var next = RecurrenceSchedule.Next(Date(2026, 1, 1), RecurrenceUnit.Week, 1, dayOfWeek: DayOfWeek.Monday);

        next.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        next.ShouldBeGreaterThan(Date(2026, 1, 1));
    }

    [Fact]
    public void Monthly_keeps_the_same_day_of_month()
        => RecurrenceSchedule.Next(Date(2026, 1, 15), RecurrenceUnit.Month, 1)
            .ShouldBe(Date(2026, 2, 15));

    [Fact]
    public void Monthly_from_the_thirty_first_clamps_into_February_instead_of_skipping_it()
    {
        var next = RecurrenceSchedule.Next(Date(2026, 1, 31), RecurrenceUnit.Month, 1, dayOfMonth: 31);

        next.Month.ShouldBe(2);
        next.Day.ShouldBe(28);
    }

    [Fact]
    public void A_clamped_rule_returns_to_its_real_day_the_following_month()
    {
        // Rent on the 31st: February pays on the 28th, March goes back to the 31st.
        var february = RecurrenceSchedule.Next(Date(2026, 1, 31), RecurrenceUnit.Month, 1, dayOfMonth: 31);
        var march = RecurrenceSchedule.Next(february, RecurrenceUnit.Month, 1, dayOfMonth: 31);

        march.Month.ShouldBe(3);
        march.Day.ShouldBe(31);
    }

    [Fact]
    public void Monthly_handles_a_leap_year_February()
        => RecurrenceSchedule.Next(Date(2028, 1, 31), RecurrenceUnit.Month, 1, dayOfMonth: 31)
            .Day.ShouldBe(29);

    [Fact]
    public void Yearly_advances_by_the_interval()
        => RecurrenceSchedule.Next(Date(2026, 3, 10), RecurrenceUnit.Year, 1)
            .ShouldBe(Date(2027, 3, 10));

    [Fact]
    public void An_interval_below_one_is_rejected()
        => Should.Throw<ArgumentOutOfRangeException>(() =>
            RecurrenceSchedule.Next(Date(2026, 1, 1), RecurrenceUnit.Day, 0));

    [Fact]
    public void An_unknown_unit_is_rejected()
        => Should.Throw<ArgumentOutOfRangeException>(() =>
            RecurrenceSchedule.Next(Date(2026, 1, 1), (RecurrenceUnit)42, 1));

    [Fact]
    public void Occurrences_backfills_every_run_missed_while_the_app_was_down()
    {
        // Monthly rule starting in January, nothing ran until May.
        var occurrences = RecurrenceSchedule.Occurrences(
            start: Date(2026, 1, 1),
            after: Date(2025, 12, 31),
            until: Date(2026, 5, 2),
            RecurrenceUnit.Month, 1, dayOfMonth: 1);

        occurrences.Count.ShouldBe(5);
        occurrences.Select(o => o.Month).ShouldBe(new[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void Occurrences_excludes_runs_already_processed()
    {
        var occurrences = RecurrenceSchedule.Occurrences(
            start: Date(2026, 1, 1),
            after: Date(2026, 3, 1),
            until: Date(2026, 5, 2),
            RecurrenceUnit.Month, 1, dayOfMonth: 1);

        occurrences.Select(o => o.Month).ShouldBe(new[] { 4, 5 });
    }

    [Fact]
    public void Occurrences_stops_at_the_until_bound()
        => RecurrenceSchedule.Occurrences(
            Date(2026, 1, 1), Date(2025, 1, 1), Date(2026, 1, 1),
            RecurrenceUnit.Month, 1).Count.ShouldBe(1);

    [Fact]
    public void Occurrences_is_empty_when_the_rule_starts_in_the_future()
        => RecurrenceSchedule.Occurrences(
            Date(2027, 1, 1), Date(2026, 1, 1), Date(2026, 6, 1),
            RecurrenceUnit.Month, 1).ShouldBeEmpty();

    [Fact]
    public void Occurrences_respects_the_safety_cap()
        => RecurrenceSchedule.Occurrences(
            Date(2020, 1, 1), Date(2019, 1, 1), Date(2030, 1, 1),
            RecurrenceUnit.Day, 1, maxOccurrences: 10).Count.ShouldBe(10);
}
