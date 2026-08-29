namespace SplitEverything.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>SHA-256 of the token. The plaintext only ever exists in the client cookie.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Set when this token was rotated, pointing at its replacement (reuse detection).</summary>
    public Guid? ReplacedByTokenId { get; set; }

    public string? DeviceId { get; set; }
    public string? UserAgent { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
