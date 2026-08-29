using SplitEverything.Api.Infrastructure;
using Shouldly;

namespace SplitEverything.Tests.Api;

/// <summary>
/// Choosing the address a phone can actually reach.
///
/// A development machine has many addresses: loopback, a real LAN interface, and
/// on this project's own dev box eleven Docker bridges. An invite link built on
/// the wrong one is a dead end, and localhost is the worst of them because on a
/// phone it means the phone.
///
/// The policy is pure so it can be tested without asking the machine anything.
/// </summary>
public class LocalNetworkAddressTests
{
    private static NetworkCandidate Candidate(
        string address, bool hasGateway = true, bool isLoopback = false, bool isWireless = false)
        => new(address, hasGateway, isLoopback, isWireless);

    [Fact]
    public void Prefers_an_interface_with_a_gateway()
    {
        // A Docker bridge has an address but no route off the machine.
        var chosen = LocalNetworkAddress.Choose([
            Candidate("172.17.0.1", hasGateway: false),
            Candidate("192.168.2.48")
        ]);

        chosen.ShouldBe("192.168.2.48");
    }

    [Fact]
    public void Skips_loopback()
    {
        LocalNetworkAddress.Choose([
            Candidate("127.0.0.1", isLoopback: true),
            Candidate("192.168.2.48")
        ]).ShouldBe("192.168.2.48");
    }

    [Fact]
    public void Prefers_wireless_over_a_gatewayless_interface()
    {
        LocalNetworkAddress.Choose([
            Candidate("172.20.0.1", hasGateway: false),
            Candidate("192.168.2.48", isWireless: true)
        ]).ShouldBe("192.168.2.48");
    }

    [Fact]
    public void Takes_a_gatewayless_interface_only_when_nothing_better_exists()
    {
        LocalNetworkAddress.Choose([
            Candidate("172.17.0.1", hasGateway: false)
        ]).ShouldBe("172.17.0.1");
    }

    [Fact]
    public void Finds_nothing_when_there_is_only_loopback()
    {
        LocalNetworkAddress.Choose([Candidate("127.0.0.1", isLoopback: true)]).ShouldBeNull();
    }

    [Fact]
    public void Finds_nothing_in_an_empty_list()
    {
        LocalNetworkAddress.Choose([]).ShouldBeNull();
    }
}

/// <summary>
/// Rewriting the invite base URL for development.
///
/// Only a loopback host is replaced. That is the signal that the value is the
/// default nobody chose, and it is the only host that is actively wrong for
/// another device. Anything a person set deliberately is left alone.
/// </summary>
public class DevelopmentAppBaseUrlTests
{
    [Theory]
    [InlineData("http://localhost:5173", "http://192.168.2.48:5173")]
    [InlineData("http://127.0.0.1:5173", "http://192.168.2.48:5173")]
    [InlineData("http://[::1]:5173", "http://192.168.2.48:5173")]
    public void Replaces_a_loopback_host(string configured, string expected)
    {
        DevelopmentAppBaseUrl.Rewrite(configured, "192.168.2.48").ShouldBe(expected);
    }

    [Fact]
    public void Keeps_the_port()
    {
        DevelopmentAppBaseUrl.Rewrite("http://localhost:4000", "192.168.2.48")
            .ShouldBe("http://192.168.2.48:4000");
    }

    [Fact]
    public void Keeps_https()
    {
        DevelopmentAppBaseUrl.Rewrite("https://localhost:5173", "192.168.2.48")
            .ShouldBe("https://192.168.2.48:5173");
    }

    [Fact]
    public void Leaves_a_real_host_alone()
    {
        // Someone set this on purpose; second-guessing it would be worse than
        // leaving a link that does not work on a phone.
        DevelopmentAppBaseUrl.Rewrite("https://split.example.com", "192.168.2.48")
            .ShouldBe("https://split.example.com");
    }

    [Fact]
    public void Leaves_it_alone_when_no_address_was_found()
    {
        DevelopmentAppBaseUrl.Rewrite("http://localhost:5173", null)
            .ShouldBe("http://localhost:5173");
    }

    [Fact]
    public void Leaves_a_value_it_cannot_parse_alone()
    {
        DevelopmentAppBaseUrl.Rewrite("not a url", "192.168.2.48").ShouldBe("not a url");
    }

    [Fact]
    public void Leaves_an_empty_value_alone()
    {
        DevelopmentAppBaseUrl.Rewrite("", "192.168.2.48").ShouldBe("");
    }
}
