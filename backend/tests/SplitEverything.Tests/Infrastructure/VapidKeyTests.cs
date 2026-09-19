using Shouldly;
using SplitEverything.Infrastructure.Notifications;

namespace SplitEverything.Tests.Infrastructure;

public class VapidKeyTests
{
    private const string PublicKey =
        "BDLIpARp5poJEsnhCHwluND9bDbYwZX2nMc3rKpQbPAjRDnLFQUFKyr3av2mffIbsNoWZc0D7UL6kQjxBwcIwTw";

    private const string PrivateKey = "93k0NC3nAZW-tPWQlgUFPUqTMxIvKKGoFPRPBRFbNBg";

    [Fact]
    public void AcceptsAGeneratedPair()
    {
        VapidKey.IsValidPublicKey(PublicKey).ShouldBeTrue();
        VapidKey.IsValidPrivateKey(PrivateKey).ShouldBeTrue();
    }

    [Fact]
    public void RefusesAContactAddressAsAKey()
    {
        VapidKey.IsValidPublicKey("mailto:someone@example.com").ShouldBeFalse();
    }

    [Fact]
    public void RefusesTheTwoKeysInEachOthersSlots()
    {
        VapidKey.IsValidPublicKey(PrivateKey).ShouldBeFalse();
        VapidKey.IsValidPrivateKey(PublicKey).ShouldBeFalse();
    }

    [Fact]
    public void RefusesEmptyAndWhitespace()
    {
        VapidKey.IsValidPublicKey(null).ShouldBeFalse();
        VapidKey.IsValidPublicKey("").ShouldBeFalse();
        VapidKey.IsValidPublicKey("   ").ShouldBeFalse();
    }

    [Fact]
    public void AcceptsAKeyPastedWithPadding()
    {
        VapidKey.IsValidPublicKey(PublicKey + "=").ShouldBeTrue();
    }

    [Fact]
    public void RefusesAPointThatDoesNotStartWithFour()
    {
        var bytes = Convert.FromBase64String(PublicKey.Replace('-', '+').Replace('_', '/') + "=");
        bytes[0] = 0x02;

        VapidKey.IsValidPublicKey(Convert.ToBase64String(bytes)).ShouldBeFalse();
    }

    [Fact]
    public void WantsAContactItCanReachAsTheSubject()
    {
        VapidKey.IsValidSubject("mailto:someone@example.com").ShouldBeTrue();
        VapidKey.IsValidSubject("https://example.com/contact").ShouldBeTrue();
        VapidKey.IsValidSubject("someone@example.com").ShouldBeFalse();
        VapidKey.IsValidSubject("").ShouldBeFalse();
    }
}
