namespace SplitEverything.Application.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }

    string? DeviceId { get; }

    bool IsAuthenticated { get; }

    Guid RequireUserId();
}
