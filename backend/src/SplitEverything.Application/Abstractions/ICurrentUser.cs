namespace SplitEverything.Application.Abstractions;

/// <summary>The authenticated caller, resolved from the JWT.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }

    /// <summary>Device id from the X-Device-Id header; the key used in vector clocks.</summary>
    string? DeviceId { get; }

    bool IsAuthenticated { get; }

    Guid RequireUserId();
}
