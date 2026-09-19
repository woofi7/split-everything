using System.Text.Json;
using SplitEverything.Application.Abstractions;

namespace SplitEverything.Infrastructure.Notifications;

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
