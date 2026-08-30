using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Shouldly;
using SplitEverything.Api.Infrastructure;

namespace SplitEverything.Tests.Api;

/// <summary>
/// Who a rate limit counts against.
///
/// This got it wrong twice at once: it read a forwarded header any caller can write,
/// which made the limit optional for anybody who knew, and when there was no header
/// it used the connection's address - which behind a reverse proxy is the proxy, so
/// every user of the app shared one allowance and got refused together. A refused
/// request reads to the app as being offline, which is exactly how it showed up.
/// </summary>
public class RateLimitKeyTests
{
    private static HttpContext Request(string? address = null, Guid? userId = null, string? forwarded = null)
    {
        var context = new DefaultHttpContext();

        if (address is not null)
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(address);

        if (forwarded is not null)
            context.Request.Headers["X-Forwarded-For"] = forwarded;

        if (userId is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "test"));
        }

        return context;
    }

    [Fact]
    public void The_signed_in_account_is_the_caller()
    {
        var userId = Guid.CreateVersion7();

        // One person's phone and laptop are one caller; a household behind one
        // address is several.
        RateLimitKeys.For(Request("203.0.113.7", userId)).ShouldBe($"user:{userId}");
    }

    [Fact]
    public void An_anonymous_caller_is_their_address()
        => RateLimitKeys.For(Request("203.0.113.7")).ShouldBe("ip:203.0.113.7");

    [Fact]
    public void Two_addresses_are_two_callers()
    {
        RateLimitKeys.For(Request("203.0.113.7"))
            .ShouldNotBe(RateLimitKeys.For(Request("203.0.113.8")));
    }

    [Fact]
    public void A_forwarded_header_is_not_read_here()
    {
        // The middleware applies it, and only from a proxy on a private network.
        // Reading it here would let any caller pick their own allowance.
        var key = RateLimitKeys.For(Request("203.0.113.7", forwarded: "10.0.0.1, 1.2.3.4"));

        key.ShouldBe("ip:203.0.113.7");
    }

    [Fact]
    public void A_caller_with_no_address_still_has_a_key()
        // Nothing sensible to say, but a null key would take down the limiter.
        => RateLimitKeys.For(Request()).ShouldBe("unknown");
}
