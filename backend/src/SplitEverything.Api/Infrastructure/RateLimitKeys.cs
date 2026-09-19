using System.Security.Claims;

namespace SplitEverything.Api.Infrastructure;

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
