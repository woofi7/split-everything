using SplitEverything.Application.Contracts.Notifications;

namespace SplitEverything.Application.Services;

public interface INotificationService
{
    Task<PushSubscriptionDto> RegisterAsync(Guid userId, RegisterPushRequest request, CancellationToken ct = default);
    Task UnregisterAsync(Guid userId, string endpoint, CancellationToken ct = default);
    Task<IReadOnlyList<PushSubscriptionDto>> ListAsync(Guid userId, CancellationToken ct = default);
    VapidPublicKeyDto GetVapidPublicKey();
}
