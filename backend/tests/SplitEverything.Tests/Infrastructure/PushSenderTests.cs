using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SplitEverything.Application.Abstractions;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure;
using SplitEverything.Infrastructure.Notifications;
using SplitEverything.Tests.Application;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Infrastructure;

/// <summary>
/// The three delivery channels. All of them must be no-ops when unconfigured: a
/// homelab install with no Firebase or Apple account still has to work over Web
/// Push instead of erroring on every notification.
/// </summary>
public class PushSenderTests
{
    private static readonly PushMessage Message = new("Title", "Body", "/groups/1", "tag-1",
        new Dictionary<string, string> { ["extra"] = "value" });

    private static PushTarget Target(string endpoint = "https://push.example/abc")
        => new(Guid.NewGuid(), PushChannel.WebPush, endpoint, "p256dh", "auth");

    [Fact]
    public void The_payload_carries_every_field_a_client_needs()
    {
        var json = PushPayload.Serialize(Message);

        json.ShouldContain("\"title\":\"Title\"");
        json.ShouldContain("\"body\":\"Body\"");
        json.ShouldContain("\"url\":\"/groups/1\"");
        json.ShouldContain("\"tag\":\"tag-1\"");
        json.ShouldContain("extra");
    }

    [Fact]
    public void The_payload_leaves_out_fields_that_were_not_set()
        => PushPayload.Serialize(new PushMessage("Title", "Body")).ShouldNotContain("url");

    [Fact]
    public async Task Web_push_with_no_vapid_keys_is_a_no_op_rather_than_a_failure()
    {
        var sender = new WebPushSender(new PushOptions(), NullLogger<WebPushSender>.Instance);

        (await sender.SendAsync(Target(), Message)).ShouldBeTrue();
    }

    [Fact]
    public async Task Web_push_prunes_a_subscription_with_no_encryption_keys()
    {
        var sender = new WebPushSender(new PushOptions
        {
            VapidPublicKey = "BJxKEQ5wJf1QqLTgWvXcRLZQ0eEbHZGmVfNvBBaTLZk",
            VapidPrivateKey = "gK9dR3kQpMxWvHhTLbNcJ4YeXfZqA2sD5vB8nC1mE7o",
            VapidSubject = "mailto:test@example.com"
        }, NullLogger<WebPushSender>.Instance);

        var target = new PushTarget(Guid.NewGuid(), PushChannel.WebPush,
            "https://push.example/abc", null, null);

        (await sender.SendAsync(target, Message)).ShouldBeFalse();
    }

    [Fact]
    public void Web_push_reports_its_channel()
        => new WebPushSender(new PushOptions(), NullLogger<WebPushSender>.Instance)
            .Channel.ShouldBe(PushChannel.WebPush);

    [Fact]
    public async Task Fcm_with_no_credentials_is_a_no_op()
    {
        var handler = new StubHttpHandler("{}");
        var sender = new FcmPushSender(new HttpClient(handler), new PushOptions(),
            Substitute.For<IFcmAccessTokenProvider>(), NullLogger<FcmPushSender>.Instance);

        (await sender.SendAsync(Target("fcm-token"), Message)).ShouldBeTrue();
        handler.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Fcm_sends_when_it_is_configured()
    {
        var handler = new StubHttpHandler("{}");
        var tokens = Substitute.For<IFcmAccessTokenProvider>();
        tokens.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("access-token"));

        var sender = new FcmPushSender(new HttpClient(handler), ConfiguredFcm(),
            tokens, NullLogger<FcmPushSender>.Instance);

        (await sender.SendAsync(Target("fcm-token"), Message)).ShouldBeTrue();
        handler.CallCount.ShouldBe(1);
        handler.LastRequestUri!.ShouldContain("projects/test-project/messages:send");
    }

    [Fact]
    public async Task Fcm_prunes_a_token_the_app_no_longer_holds()
    {
        var handler = new StubHttpHandler("{}", HttpStatusCode.NotFound);
        var tokens = Substitute.For<IFcmAccessTokenProvider>();
        tokens.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("access-token"));

        var sender = new FcmPushSender(new HttpClient(handler), ConfiguredFcm(),
            tokens, NullLogger<FcmPushSender>.Instance);

        (await sender.SendAsync(Target("stale-token"), Message)).ShouldBeFalse();
    }

    [Fact]
    public async Task Fcm_keeps_a_token_after_a_transient_failure()
    {
        var handler = new StubHttpHandler("{}", HttpStatusCode.InternalServerError);
        var tokens = Substitute.For<IFcmAccessTokenProvider>();
        tokens.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("access-token"));

        var sender = new FcmPushSender(new HttpClient(handler), ConfiguredFcm(),
            tokens, NullLogger<FcmPushSender>.Instance);

        // A 500 is Google's problem, not a dead device.
        (await sender.SendAsync(Target("token"), Message)).ShouldBeTrue();
    }

    [Fact]
    public async Task Fcm_skips_the_send_when_no_access_token_can_be_obtained()
    {
        var handler = new StubHttpHandler("{}");
        var tokens = Substitute.For<IFcmAccessTokenProvider>();
        tokens.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));

        var sender = new FcmPushSender(new HttpClient(handler), ConfiguredFcm(),
            tokens, NullLogger<FcmPushSender>.Instance);

        (await sender.SendAsync(Target("token"), Message)).ShouldBeTrue();
        handler.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_fcm_token_provider_returns_nothing_when_unconfigured()
        => (await new FcmAccessTokenProvider(new PushOptions(), Clock()).GetAsync()).ShouldBeNull();

    [Fact]
    public async Task Apns_with_no_credentials_is_a_no_op()
    {
        var handler = new StubHttpHandler("{}");
        var sender = new ApnsPushSender(new HttpClient(handler), new PushOptions(),
            Substitute.For<IApnsJwtProvider>(), NullLogger<ApnsPushSender>.Instance);

        (await sender.SendAsync(Target("device-token"), Message)).ShouldBeTrue();
        handler.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Apns_sends_when_it_is_configured()
    {
        var handler = new StubHttpHandler("{}");
        var jwt = Substitute.For<IApnsJwtProvider>();
        jwt.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult("signed-jwt"));

        var sender = new ApnsPushSender(new HttpClient(handler), ConfiguredApns(),
            jwt, NullLogger<ApnsPushSender>.Instance);

        (await sender.SendAsync(Target("device-token"), Message)).ShouldBeTrue();
        handler.LastRequestUri!.ShouldContain("/3/device/device-token");
    }

    [Fact]
    public async Task Apns_uses_the_sandbox_host_when_asked()
    {
        var handler = new StubHttpHandler("{}");
        var jwt = Substitute.For<IApnsJwtProvider>();
        jwt.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult("signed-jwt"));
        var options = ConfiguredApns();
        options.ApnsUseSandbox = true;

        var sender = new ApnsPushSender(new HttpClient(handler), options, jwt,
            NullLogger<ApnsPushSender>.Instance);
        await sender.SendAsync(Target("device-token"), Message);

        handler.LastRequestUri!.ShouldContain("api.sandbox.push.apple.com");
    }

    [Fact]
    public async Task Apns_prunes_a_device_token_apple_reports_as_gone()
    {
        var handler = new StubHttpHandler("{}", HttpStatusCode.Gone);
        var jwt = Substitute.For<IApnsJwtProvider>();
        jwt.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult("signed-jwt"));

        var sender = new ApnsPushSender(new HttpClient(handler), ConfiguredApns(),
            jwt, NullLogger<ApnsPushSender>.Instance);

        (await sender.SendAsync(Target("dead-token"), Message)).ShouldBeFalse();
    }

    [Fact]
    public async Task Apns_keeps_a_token_after_a_transient_failure()
    {
        var handler = new StubHttpHandler("{}", HttpStatusCode.ServiceUnavailable);
        var jwt = Substitute.For<IApnsJwtProvider>();
        jwt.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult("signed-jwt"));

        var sender = new ApnsPushSender(new HttpClient(handler), ConfiguredApns(),
            jwt, NullLogger<ApnsPushSender>.Instance);

        (await sender.SendAsync(Target("token"), Message)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_apns_jwt_provider_signs_a_three_part_token()
    {
        var provider = new ApnsJwtProvider(ConfiguredApns(), Clock());

        var token = await provider.GetAsync();

        token.Split('.').Length.ShouldBe(3);
    }

    [Fact]
    public async Task The_apns_jwt_is_reused_inside_its_window()
    {
        var clock = Clock();
        var provider = new ApnsJwtProvider(ConfiguredApns(), clock);

        var first = await provider.GetAsync();
        var second = await provider.GetAsync();

        second.ShouldBe(first);
    }

    [Fact]
    public async Task The_apns_jwt_is_regenerated_once_it_ages_out()
    {
        var clock = Clock();
        var provider = new ApnsJwtProvider(ConfiguredApns(), clock);
        var first = await provider.GetAsync();

        // Apple rejects tokens older than an hour, so it must not be cached forever.
        clock.Advance(TimeSpan.FromMinutes(50));

        (await provider.GetAsync()).ShouldNotBe(first);
    }

    [Fact]
    public async Task The_logging_email_sender_swallows_the_send()
    {
        var sender = new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance);

        // Invites still work by link and QR when SMTP is not configured.
        await sender.SendAsync("someone@example.com", "Subject", "<p>html</p>", "text");
    }

    private static PushOptions ConfiguredFcm() => new()
    {
        FcmProjectId = "test-project",
        FcmServiceAccountJson = """{"type":"service_account","project_id":"test-project"}"""
    };

    private static PushOptions ConfiguredApns() => new()
    {
        ApnsBundleId = "com.example.split",
        ApnsKeyId = "KEYID12345",
        ApnsTeamId = "TEAMID1234",
        ApnsPrivateKey = GenerateEcPrivateKeyPem()
    };

    private static FixedClock Clock()
        => new(new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero));

    /// <summary>A throwaway P-256 key, so the signing path runs for real.</summary>
    private static string GenerateEcPrivateKeyPem()
    {
        using var key = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }
}
