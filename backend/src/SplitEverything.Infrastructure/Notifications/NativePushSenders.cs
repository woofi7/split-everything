using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SplitEverything.Application.Abstractions;
using SplitEverything.Domain.Common;

namespace SplitEverything.Infrastructure.Notifications;

/// <summary>
/// FCM HTTP v1 for the Android shell.
///
/// Left as a thin, configuration-gated sender: with no credentials it reports
/// success and does nothing, so a homelab install without Firebase set up still
/// works over Web Push instead of erroring on every notification.
/// </summary>
public sealed class FcmPushSender(
    HttpClient http,
    PushOptions options,
    IFcmAccessTokenProvider tokens,
    ILogger<FcmPushSender> logger) : IPushSender
{
    public PushChannel Channel => PushChannel.Fcm;

    public async Task<bool> SendAsync(PushTarget target, PushMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.FcmProjectId) || string.IsNullOrWhiteSpace(options.FcmServiceAccountJson))
        {
            logger.LogDebug("FCM is not configured; skipping");
            return true;
        }

        var accessToken = await tokens.GetAsync(ct);
        if (accessToken is null) return true;

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://fcm.googleapis.com/v1/projects/{options.FcmProjectId}/messages:send")
        {
            Content = JsonContent.Create(new
            {
                message = new
                {
                    token = target.Endpoint,
                    notification = new { title = message.Title, body = message.Body },
                    data = BuildData(message),
                    android = new { priority = "high" }
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await http.SendAsync(request, ct);

        if (response.IsSuccessStatusCode) return true;

        // FCM answers 404 for a token the app no longer holds.
        var gone = response.StatusCode is System.Net.HttpStatusCode.NotFound;
        logger.Log(gone ? LogLevel.Information : LogLevel.Warning,
            "FCM send failed with {Status}", response.StatusCode);
        return !gone;
    }

    private static Dictionary<string, string> BuildData(PushMessage message)
    {
        var data = new Dictionary<string, string>();
        if (message.Url is not null) data["url"] = message.Url;
        if (message.Tag is not null) data["tag"] = message.Tag;
        foreach (var (key, value) in message.Data ?? new Dictionary<string, string>())
            data[key] = value;
        return data;
    }
}

public interface IFcmAccessTokenProvider
{
    Task<string?> GetAsync(CancellationToken ct = default);
}

/// <summary>
/// Exchanges the service-account JSON for an OAuth access token, cached until it
/// nears expiry.
/// </summary>
public sealed class FcmAccessTokenProvider(PushOptions options, IClock clock) : IFcmAccessTokenProvider
{
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<string?> GetAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.FcmServiceAccountJson)) return null;

        if (_token is not null && _expiresAt > clock.UtcNow.AddMinutes(2)) return _token;

        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && _expiresAt > clock.UtcNow.AddMinutes(2)) return _token;

            // CredentialFactory rather than GoogleCredential.FromJson, which is
            // deprecated: it accepted any credential type from a json blob, and the
            // factory makes the caller name what it is expecting.
            var credential = Google.Apis.Auth.OAuth2.CredentialFactory
                .FromJson<Google.Apis.Auth.OAuth2.ServiceAccountCredential>(options.FcmServiceAccountJson)
                .ToGoogleCredential()
                .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");

            _token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);
            _expiresAt = clock.UtcNow.AddMinutes(50);
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>
/// APNs for the iOS shell. Uses token-based auth over HTTP/2, and like FCM is a
/// no-op until credentials are configured.
/// </summary>
public sealed class ApnsPushSender(
    HttpClient http,
    PushOptions options,
    IApnsJwtProvider jwt,
    ILogger<ApnsPushSender> logger) : IPushSender
{
    public PushChannel Channel => PushChannel.Apns;

    public async Task<bool> SendAsync(PushTarget target, PushMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApnsBundleId) || string.IsNullOrWhiteSpace(options.ApnsPrivateKey))
        {
            logger.LogDebug("APNs is not configured; skipping");
            return true;
        }

        var host = options.ApnsUseSandbox
            ? "https://api.sandbox.push.apple.com"
            : "https://api.push.apple.com";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/3/device/{target.Endpoint}")
        {
            Version = new Version(2, 0),
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = JsonContent.Create(new
            {
                aps = new
                {
                    alert = new { title = message.Title, body = message.Body },
                    sound = "default",
                    // Wakes the app so the sync engine can pull the change itself
                    // rather than trusting the notification payload.
                    contentAvailable = 1
                },
                url = message.Url
            })
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", await jwt.GetAsync(ct));
        request.Headers.Add("apns-topic", options.ApnsBundleId);
        request.Headers.Add("apns-push-type", "alert");
        if (message.Tag is not null) request.Headers.Add("apns-collapse-id", Truncate(message.Tag, 64));

        using var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return true;

        // 410 Gone means the device token is dead for good.
        var gone = response.StatusCode is System.Net.HttpStatusCode.Gone;
        logger.Log(gone ? LogLevel.Information : LogLevel.Warning,
            "APNs send failed with {Status}", response.StatusCode);
        return !gone;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

public interface IApnsJwtProvider
{
    Task<string> GetAsync(CancellationToken ct = default);
}

public sealed class ApnsJwtProvider(PushOptions options, IClock clock) : IApnsJwtProvider
{
    private string? _token;
    private DateTimeOffset _issuedAt = DateTimeOffset.MinValue;

    public Task<string> GetAsync(CancellationToken ct = default)
    {
        // Apple rejects tokens older than an hour and rate-limits regeneration, so
        // one token is reused for most of that window.
        if (_token is not null && clock.UtcNow - _issuedAt < TimeSpan.FromMinutes(45))
            return Task.FromResult(_token);

        using var key = System.Security.Cryptography.ECDsa.Create();
        key.ImportFromPem(options.ApnsPrivateKey);

        var header = Base64Url(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { alg = "ES256", kid = options.ApnsKeyId }));
        var payload = Base64Url(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { iss = options.ApnsTeamId, iat = clock.UtcNow.ToUnixTimeSeconds() }));

        var signature = key.SignData(
            System.Text.Encoding.UTF8.GetBytes($"{header}.{payload}"),
            System.Security.Cryptography.HashAlgorithmName.SHA256);

        _token = $"{header}.{payload}.{Base64Url(signature)}";
        _issuedAt = clock.UtcNow;
        return Task.FromResult(_token);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
