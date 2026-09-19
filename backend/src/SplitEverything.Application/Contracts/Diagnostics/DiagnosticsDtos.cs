namespace SplitEverything.Application.Contracts.Diagnostics;

public sealed record ClientErrorReport(
    string Message,
    string? Route = null,
    string? Kind = null,
    string? Stack = null,
    string? DeviceId = null,
    string? AppVersion = null);
