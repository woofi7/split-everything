using System.Security.Claims;

namespace SplitEverything.Api.Infrastructure;

/// <summary>
/// Who a request counts against.
///
/// The signed-in account first, because that is what a limit is meant to be about:
/// one person's phone and laptop are one caller, and a household behind one address
/// is several. The address is the fallback, for the endpoints somebody reaches
/// before they are anybody.
///
/// The address is only meaningful once forwarded headers have been applied, and
/// those are only honoured from a proxy on a private network. Without that this
/// counted every request through a reverse proxy against one key, which behind
/// Traefik is every user of the app at once - and it read a header any caller can
/// set, which made the limit optional for anyone who knew.
/// </summary>
public static class RateLimitKeys
{
    public static string For(HttpContext context)
    {
        var user = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? context.User.FindFirstValue("sub");

        if (!string.IsNullOrWhiteSpace(user)) return $"user:{user}";

        var address = context.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(address) ? "unknown" : $"ip:{address}";
    }
}
