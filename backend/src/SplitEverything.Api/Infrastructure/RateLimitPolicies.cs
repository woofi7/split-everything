namespace SplitEverything.Api.Infrastructure;

/// <summary>
/// The named rate limits, in one place so a controller and the registration cannot
/// drift apart over a string.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Signing in, refreshing, redeeming an invite: tight.</summary>
    public const string Auth = "auth";

    /// <summary>Crash reports from a client, which a crash loop can produce fast.</summary>
    public const string Diagnostics = "diagnostics";
}
