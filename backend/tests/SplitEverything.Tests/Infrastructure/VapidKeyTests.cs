using Shouldly;
using SplitEverything.Infrastructure.Notifications;

namespace SplitEverything.Tests.Infrastructure;

/// <summary>
/// Whether a configured VAPID value is one at all.
///
/// Written from a real deployment: the contact address had been set in the slot for
/// the public key. The server served it happily, and every phone that tried to turn
/// notifications on got "Failed to execute 'atob'" - a browser error for a server
/// mistake, three layers from the setting that caused it.
/// </summary>
public class VapidKeyTests
{
    /// <summary>A real uncompressed P-256 point: 0x04, then x, then y.</summary>
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
        // Both are base64url and both decode; only their lengths tell them apart.
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
        // A tool that writes standard base64 is not wrong, only differently spelled.
        VapidKey.IsValidPublicKey(PublicKey + "=").ShouldBeTrue();
    }

    [Fact]
    public void RefusesAPointThatDoesNotStartWithFour()
    {
        // Sixty-five bytes of the wrong thing is still not a public key.
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
