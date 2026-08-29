using Shouldly;
using SplitEverything.Domain.Sync;

namespace SplitEverything.Tests.Domain;

/// <summary>
/// The decision the whole offline story rests on: given what is stored and what
/// just arrived, does the incoming revision win, lose, or need a human?
/// </summary>
public class SyncArbiterTests
{
    private static VectorClock Clock(params (string Device, long Value)[] entries)
        => VectorClock.From(entries.Select(e => new KeyValuePair<string, long>(e.Device, e.Value)));

    [Fact]
    public void A_first_write_against_nothing_stored_is_applied()
        => SyncArbiter.Decide(VectorClock.Empty, Clock(("a", 1)))
            .ShouldBe(SyncDecision.Apply);

    [Fact]
    public void A_newer_revision_is_applied()
        => SyncArbiter.Decide(Clock(("a", 1)), Clock(("a", 2)))
            .ShouldBe(SyncDecision.Apply);

    [Fact]
    public void A_revision_that_saw_everything_stored_and_more_is_applied()
        => SyncArbiter.Decide(Clock(("a", 2)), Clock(("a", 2), ("b", 1)))
            .ShouldBe(SyncDecision.Apply);

    [Fact]
    public void A_replay_of_what_is_already_stored_is_a_no_op()
        => SyncArbiter.Decide(Clock(("a", 2)), Clock(("a", 2)))
            .ShouldBe(SyncDecision.AlreadyApplied);

    [Fact]
    public void An_older_revision_arriving_late_is_ignored_not_applied()
        => SyncArbiter.Decide(Clock(("a", 5)), Clock(("a", 3)))
            .ShouldBe(SyncDecision.AlreadyApplied);

    [Fact]
    public void Two_devices_that_edited_the_same_thing_offline_produce_a_conflict()
        => SyncArbiter.Decide(Clock(("a", 2), ("b", 1)), Clock(("a", 1), ("b", 2)))
            .ShouldBe(SyncDecision.Conflict);

    [Fact]
    public void A_revision_from_an_unknown_device_that_is_also_behind_is_a_conflict()
        => SyncArbiter.Decide(Clock(("a", 5)), Clock(("a", 4), ("c", 1)))
            .ShouldBe(SyncDecision.Conflict);

    [Fact]
    public void Conflicting_fields_lists_only_what_actually_differs()
    {
        var stored = """{"description":"Dinner","amount":50.00,"currency":"CAD"}""";
        var incoming = """{"description":"Dinner out","amount":50.00,"currency":"CAD"}""";

        SyncArbiter.ConflictingFields(stored, incoming).ShouldBe(new[] { "description" });
    }

    [Fact]
    public void Identical_payloads_have_no_conflicting_fields()
    {
        var payload = """{"description":"Dinner","amount":50.00}""";

        SyncArbiter.ConflictingFields(payload, payload).ShouldBeEmpty();
    }

    [Fact]
    public void Property_order_does_not_count_as_a_difference()
    {
        var stored = """{"description":"Dinner","amount":50.00}""";
        var incoming = """{"amount":50.00,"description":"Dinner"}""";

        SyncArbiter.ConflictingFields(stored, incoming).ShouldBeEmpty();
    }

    [Fact]
    public void A_field_added_by_one_side_counts_as_a_difference()
    {
        var stored = """{"description":"Dinner"}""";
        var incoming = """{"description":"Dinner","notes":"split with Bob"}""";

        SyncArbiter.ConflictingFields(stored, incoming).ShouldBe(new[] { "notes" });
    }

    [Fact]
    public void A_field_removed_by_one_side_counts_as_a_difference()
    {
        var stored = """{"description":"Dinner","notes":"old"}""";
        var incoming = """{"description":"Dinner"}""";

        SyncArbiter.ConflictingFields(stored, incoming).ShouldBe(new[] { "notes" });
    }

    [Fact]
    public void Numeric_values_that_differ_only_in_trailing_zeros_are_the_same()
    {
        var stored = """{"amount":50.00}""";
        var incoming = """{"amount":50}""";

        SyncArbiter.ConflictingFields(stored, incoming).ShouldBeEmpty();
    }

    [Fact]
    public void Nested_objects_are_compared_whole_and_reported_by_their_field()
    {
        var stored = """{"splits":[{"member":"a","amount":25}]}""";
        var incoming = """{"splits":[{"member":"a","amount":30}]}""";

        SyncArbiter.ConflictingFields(stored, incoming).ShouldBe(new[] { "splits" });
    }

    [Fact]
    public void Bookkeeping_fields_are_not_reported_as_conflicts()
    {
        // These change on every write; asking the user about them would be noise.
        var stored = """{"description":"Dinner","updatedAt":"2026-01-01T00:00:00Z","serverSeq":4,"vectorClockJson":"{}"}""";
        var incoming = """{"description":"Dinner","updatedAt":"2026-02-02T00:00:00Z","serverSeq":9,"vectorClockJson":"{\"a\":1}"}""";

        SyncArbiter.ConflictingFields(stored, incoming).ShouldBeEmpty();
    }

    [Fact]
    public void The_reported_fields_are_sorted_so_the_prompt_is_stable()
    {
        var stored = """{"a":1,"b":1,"c":1}""";
        var incoming = """{"a":2,"b":2,"c":2}""";

        SyncArbiter.ConflictingFields(stored, incoming).ShouldBe(new[] { "a", "b", "c" });
    }

    [Theory]
    [InlineData("not json", "{}")]
    [InlineData("{}", "not json")]
    [InlineData("", "")]
    public void Unparseable_payloads_are_reported_as_a_whole_payload_conflict(string stored, string incoming)
        => SyncArbiter.ConflictingFields(stored, incoming).ShouldBe(new[] { "*" });

    [Fact]
    public void A_json_array_at_the_root_is_compared_as_a_whole()
        => SyncArbiter.ConflictingFields("[1,2]", "[1,3]").ShouldBe(new[] { "*" });
}
