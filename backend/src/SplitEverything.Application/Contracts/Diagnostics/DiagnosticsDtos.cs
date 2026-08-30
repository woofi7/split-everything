namespace SplitEverything.Application.Contracts.Diagnostics;

/// <summary>
/// Something that broke in a browser.
///
/// Every field but the message is optional: a report that arrives half empty is
/// still worth more than the blank screen it came from.
/// </summary>
public sealed record ClientErrorReport(
    string Message,
    /// <summary>Where it happened: a route name or a path, not a full URL.</summary>
    string? Route = null,
    /// <summary>What kind of failure: render, unhandled, rejection, startup.</summary>
    string? Kind = null,
    string? Stack = null,
    /// <summary>The device the replica belongs to, to line up with sync rows.</summary>
    string? DeviceId = null,
    string? AppVersion = null);
