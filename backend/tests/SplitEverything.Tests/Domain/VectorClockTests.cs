using Shouldly;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Sync;

namespace SplitEverything.Tests.Domain;

public class VectorClockTests
{
    private static VectorClock Clock(params (string Device, long Value)[] entries)
        => VectorClock.From(entries.Select(e => new KeyValuePair<string, long>(e.Device, e.Value)));

    [Fact]
    public void Empty_clock_has_no_entries()
    {
        VectorClock.Empty.IsEmpty.ShouldBeTrue();
        VectorClock.Empty["anything"].ShouldBe(0);
    }

    [Fact]
    public void Tick_increments_only_the_named_device()
    {
        var clock = Clock(("a", 3), ("b", 7)).Tick("a");

        clock["a"].ShouldBe(4);
        clock["b"].ShouldBe(7);
    }

    [Fact]
    public void Tick_on_unknown_device_starts_at_one()
        => Clock(("a", 3)).Tick("new")["new"].ShouldBe(1);

    [Fact]
    public void Tick_does_not_mutate_the_original()
    {
        var original = Clock(("a", 1));
        original.Tick("a");

        original["a"].ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Tick_rejects_a_blank_device_id(string deviceId)
        => Should.Throw<ArgumentException>(() => VectorClock.Empty.Tick(deviceId));

    [Fact]
    public void From_drops_blank_devices_and_non_positive_counters()
    {
        var clock = VectorClock.From(new Dictionary<string, long>
        {
            ["a"] = 5, ["b"] = 0, ["c"] = -2, [" "] = 9
        });

        clock.Counters.Count.ShouldBe(1);
        clock["a"].ShouldBe(5);
    }

    [Fact]
    public void Merge_takes_the_pointwise_maximum()
    {
        var merged = Clock(("a", 5), ("b", 1)).Merge(Clock(("a", 2), ("b", 9), ("c", 3)));

        merged["a"].ShouldBe(5);
        merged["b"].ShouldBe(9);
        merged["c"].ShouldBe(3);
    }

    [Fact]
    public void Merge_is_commutative()
    {
        var left = Clock(("a", 5), ("b", 1));
        var right = Clock(("a", 2), ("c", 8));

        left.Merge(right).ShouldBe(right.Merge(left));
    }

    [Fact]
    public void Identical_clocks_compare_equal()
        => Clock(("a", 2), ("b", 3)).CompareWith(Clock(("b", 3), ("a", 2)))
            .ShouldBe(ClockOrdering.Equal);

    [Fact]
    public void A_clock_strictly_ahead_compares_after()
        => Clock(("a", 3), ("b", 3)).CompareWith(Clock(("a", 2), ("b", 3)))
            .ShouldBe(ClockOrdering.After);

    [Fact]
    public void A_clock_strictly_behind_compares_before()
        => Clock(("a", 1)).CompareWith(Clock(("a", 2)))
            .ShouldBe(ClockOrdering.Before);

    [Fact]
    public void Divergent_edits_compare_concurrent()
        => Clock(("a", 2), ("b", 1)).CompareWith(Clock(("a", 1), ("b", 2)))
            .ShouldBe(ClockOrdering.Concurrent);

    [Fact]
    public void A_clock_with_an_unknown_device_is_concurrent_when_it_is_also_behind()
        => Clock(("a", 1), ("z", 1)).CompareWith(Clock(("a", 5)))
            .ShouldBe(ClockOrdering.Concurrent);

    [Fact]
    public void Empty_compares_before_any_populated_clock()
        => VectorClock.Empty.CompareWith(Clock(("a", 1))).ShouldBe(ClockOrdering.Before);

    [Fact]
    public void Dominates_covers_equal_and_after_but_not_concurrent()
    {
        Clock(("a", 2)).Dominates(Clock(("a", 2))).ShouldBeTrue();
        Clock(("a", 3)).Dominates(Clock(("a", 2))).ShouldBeTrue();
        Clock(("a", 1)).Dominates(Clock(("a", 2))).ShouldBeFalse();
        Clock(("a", 2), ("b", 1)).Dominates(Clock(("a", 1), ("b", 2))).ShouldBeFalse();
    }

    [Fact]
    public void HasUnseenEventsFrom_is_true_only_when_the_other_side_is_ahead_somewhere()
    {
        Clock(("a", 2)).HasUnseenEventsFrom(Clock(("a", 3))).ShouldBeTrue();
        Clock(("a", 2)).HasUnseenEventsFrom(Clock(("b", 1))).ShouldBeTrue();
        Clock(("a", 2)).HasUnseenEventsFrom(Clock(("a", 2))).ShouldBeFalse();
        Clock(("a", 5)).HasUnseenEventsFrom(Clock(("a", 2))).ShouldBeFalse();
    }

    [Fact]
    public void Restrict_keeps_only_the_named_devices()
    {
        var restricted = Clock(("a", 1), ("b", 2), ("c", 3)).Restrict(["a", "c"]);

        restricted.Counters.Keys.ShouldBe(new[] { "a", "c" }, ignoreOrder: true);
        restricted["b"].ShouldBe(0);
    }

    [Fact]
    public void Json_round_trip_preserves_every_counter()
    {
        var original = Clock(("device-1", 12), ("device-2", 4));

        VectorClock.FromJson(original.ToJson()).ShouldBe(original);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromJson_treats_missing_input_as_empty(string? json)
        => VectorClock.FromJson(json).IsEmpty.ShouldBeTrue();

    [Fact]
    public void Equal_clocks_share_a_hash_code()
        => Clock(("a", 1)).GetHashCode().ShouldBe(Clock(("a", 1)).GetHashCode());

    [Fact]
    public void Equals_handles_null_and_other_types()
    {
        Clock(("a", 1)).Equals(null).ShouldBeFalse();
        Clock(("a", 1)).Equals("not a clock").ShouldBeFalse();
    }

    [Fact]
    public void Two_devices_editing_offline_then_syncing_converge_on_a_joined_clock()
    {
        // Both start from the same known state, then each writes while offline.
        var shared = Clock(("phone", 4), ("laptop", 2));
        var phone = shared.Tick("phone");
        var laptop = shared.Tick("laptop");

        phone.CompareWith(laptop).ShouldBe(ClockOrdering.Concurrent);

        var reconciled = phone.Merge(laptop);
        reconciled.Dominates(phone).ShouldBeTrue();
        reconciled.Dominates(laptop).ShouldBeTrue();
    }
}
