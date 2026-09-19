using System.Text.Json;
using System.Text.Json.Nodes;
using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Sync;

public enum SyncDecision
{
    Apply = 0,
    AlreadyApplied = 1,
    Conflict = 2
}

public static class SyncArbiter
{
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
            ClockOrdering.Before => SyncDecision.AlreadyApplied,
            ClockOrdering.Concurrent => SyncDecision.Conflict,
            _ => SyncDecision.Conflict
        };

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

        if (left is JsonValue leftValue && right is JsonValue rightValue
            && leftValue.TryGetValue<decimal>(out var leftNumber)
            && rightValue.TryGetValue<decimal>(out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return JsonNode.DeepEquals(left, right);
    }
}
