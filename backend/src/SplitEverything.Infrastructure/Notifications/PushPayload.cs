using System.Text.Json;
using SplitEverything.Application.Abstractions;

namespace SplitEverything.Infrastructure.Notifications;

/// <summary>
/// One payload shape for every channel, so the service worker and the native
/// handlers read the same fields.
/// </summary>
public static class PushPayload
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(PushMessage message)
        => JsonSerializer.Serialize(new
        {
            title = message.Title,
            body = message.Body,
            url = message.Url,
            tag = message.Tag,
            data = message.Data
        }, Options);
}
