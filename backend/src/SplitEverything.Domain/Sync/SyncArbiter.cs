using System.Text.Json;
using System.Text.Json.Nodes;
using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Sync;

public enum SyncDecision
{
    /// <summary>Incoming revision causally follows what is stored: write it.</summary>
    Apply = 0,
    /// <summary>Stored state already contains this revision: nothing to do.</summary>
    AlreadyApplied = 1,
    /// <summary>Concurrent edits: hold both and ask a human.</summary>
    Conflict = 2
}

/// <summary>
/// Decides what to do with an incoming revision, and works out which fields a
/// human actually has to choose between.
///
/// Pure and side-effect free on purpose: this is the rule the entire offline story
/// rests on, so it is testable without a database, a clock or a network.
/// </summary>
public static class SyncArbiter
{
    /// <summary>
    /// Fields that change on every write and carry no user intent. Reporting them
    /// as conflicts would bury the one field the user actually needs to resolve.
    /// </summary>
    private static readonly HashSet<string> BookkeepingFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "updatedAt", "createdAt", "serverSeq", "vectorClockJson", "vectorClock",
        "lastWriterDeviceId", "revision", "clock"
    };

    public static SyncDecision Decide(VectorClock stored, VectorClock incoming)
        => incoming.CompareWith(stored) switch
        {
            ClockOrdering.After => SyncDecision.Apply,
            ClockOrdering.Equal => SyncDecision.AlreadyApplied,
            // An older revision arriving late is not an error: some other device
            // already carried the group past it, so dropping it is correct.
            ClockOrdering.Before => SyncDecision.AlreadyApplied,
            ClockOrdering.Concurrent => SyncDecision.Conflict,
            _ => SyncDecision.Conflict
        };

    /// <summary>
    /// Names of the top-level fields that differ between two payload snapshots.
    /// Returns ["*"] when either side cannot be read as a JSON object, since then
    /// the only honest answer is "the whole thing differs".
    /// </summary>
    public static IReadOnlyList<string> ConflictingFields(string storedJson, string incomingJson)
    {
        var stored = TryParseObject(storedJson);
        var incoming = TryParseObject(incomingJson);

        if (stored is null || incoming is null) return ["*"];

        var fields = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var name in stored.Select(p => p.Key).Union(incoming.Select(p => p.Key), StringComparer.Ordinal))
        {
            if (BookkeepingFields.Contains(name)) continue;

            var inStored = stored.TryGetPropertyValue(name, out var left);
            var inIncoming = incoming.TryGetPropertyValue(name, out var right);

            if (inStored != inIncoming || !NodesMatch(left, right))
                fields.Add(name);
        }

        return [.. fields];
    }

    private static JsonObject? TryParseObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool NodesMatch(JsonNode? left, JsonNode? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;

        // 50.00 and 50 are the same amount; comparing the raw text would report a
        // conflict every time two clients serialise a decimal differently.
        if (left is JsonValue leftValue && right is JsonValue rightValue
            && leftValue.TryGetValue<decimal>(out var leftNumber)
            && rightValue.TryGetValue<decimal>(out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return JsonNode.DeepEquals(left, right);
    }
}
