using System.Text.Json;
using System.Text.Json.Serialization;
using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Sync;

/// <summary>
/// A per-device logical clock: device id -> monotonic counter.
///
/// Every syncable entity carries one. Comparing two clocks tells us whether one
/// revision causally follows the other, or whether the two were written
/// concurrently (a true conflict that a human must resolve).
///
/// The clock is immutable; every mutation returns a new instance so that a
/// revision's clock can never be edited after the fact.
/// </summary>
public sealed class VectorClock : IEquatable<VectorClock>
{
    private readonly SortedDictionary<string, long> _counters;

    public static VectorClock Empty { get; } = new(new SortedDictionary<string, long>(StringComparer.Ordinal));

    private VectorClock(SortedDictionary<string, long> counters) => _counters = counters;

    [JsonIgnore]
    public IReadOnlyDictionary<string, long> Counters => _counters;

    [JsonIgnore]
    public bool IsEmpty => _counters.Count == 0;

    public static VectorClock From(IEnumerable<KeyValuePair<string, long>> counters)
    {
        var dict = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var (device, value) in counters)
        {
            if (string.IsNullOrWhiteSpace(device) || value <= 0) continue;
            dict[device] = value;
        }
        return new VectorClock(dict);
    }

    public long this[string deviceId] => _counters.TryGetValue(deviceId, out var v) ? v : 0;

    /// <summary>Bumps this device's counter by one, leaving every other entry untouched.</summary>
    public VectorClock Tick(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device id is required to tick a vector clock.", nameof(deviceId));

        var next = new SortedDictionary<string, long>(_counters, StringComparer.Ordinal);
        next[deviceId] = this[deviceId] + 1;
        return new VectorClock(next);
    }

    /// <summary>
    /// Pointwise maximum of two clocks. This is the join used after a successful
    /// sync, and the basis of merge reconciliation: joining two group logs keeps
    /// the furthest-known point for every device on either side.
    /// </summary>
    public VectorClock Merge(VectorClock other)
    {
        var next = new SortedDictionary<string, long>(_counters, StringComparer.Ordinal);
        foreach (var (device, value) in other._counters)
        {
            if (!next.TryGetValue(device, out var mine) || value > mine)
                next[device] = value;
        }
        return new VectorClock(next);
    }

    public ClockOrdering CompareWith(VectorClock other)
    {
        var leftAhead = false;
        var rightAhead = false;

        foreach (var device in _counters.Keys.Union(other._counters.Keys, StringComparer.Ordinal))
        {
            var mine = this[device];
            var theirs = other[device];
            if (mine > theirs) leftAhead = true;
            else if (mine < theirs) rightAhead = true;
            if (leftAhead && rightAhead) return ClockOrdering.Concurrent;
        }

        if (leftAhead) return ClockOrdering.After;
        if (rightAhead) return ClockOrdering.Before;
        return ClockOrdering.Equal;
    }

    /// <summary>True when this clock has seen everything <paramref name="other"/> has seen.</summary>
    public bool Dominates(VectorClock other)
    {
        var ordering = CompareWith(other);
        return ordering is ClockOrdering.After or ClockOrdering.Equal;
    }

    /// <summary>
    /// True when <paramref name="other"/> contains at least one event this clock
    /// has not observed - i.e. the remote revision is worth pulling.
    /// </summary>
    public bool HasUnseenEventsFrom(VectorClock other)
        => other._counters.Any(entry => entry.Value > this[entry.Key]);

    /// <summary>
    /// Restricts the clock to a set of devices. Used when splitting a group so the
    /// partitioned log keeps only the causal history relevant to its own devices.
    /// </summary>
    public VectorClock Restrict(IEnumerable<string> deviceIds)
    {
        var keep = new HashSet<string>(deviceIds, StringComparer.Ordinal);
        return From(_counters.Where(entry => keep.Contains(entry.Key)));
    }

    public string ToJson() => JsonSerializer.Serialize(_counters);

    public static VectorClock FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        var parsed = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
        return parsed is null ? Empty : From(parsed);
    }

    public bool Equals(VectorClock? other)
        => other is not null && CompareWith(other) == ClockOrdering.Equal;

    public override bool Equals(object? obj) => Equals(obj as VectorClock);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var (device, value) in _counters)
        {
            hash.Add(device, StringComparer.Ordinal);
            hash.Add(value);
        }
        return hash.ToHashCode();
    }

    public override string ToString() => ToJson();
}
