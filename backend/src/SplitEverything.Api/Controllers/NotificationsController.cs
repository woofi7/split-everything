using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Notifications;
using SplitEverything.Application.Services;

namespace SplitEverything.Api.Controllers;

public sealed class NotificationsController(
    ICurrentUser currentUser,
    INotificationService notifications) : ApiControllerBase(currentUser)
{
    /// <summary>Public: the browser needs this before it can subscribe.</summary>
    [AllowAnonymous]
    [HttpGet("vapid-key")]
    public ActionResult<VapidPublicKeyDto> VapidKey() => Ok(notifications.GetVapidPublicKey());

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PushSubscriptionDto>>> List(CancellationToken ct)
        => Ok(await notifications.ListAsync(UserId, ct));

    [HttpPost]
    public async Task<ActionResult<PushSubscriptionDto>> Register(
        RegisterPushRequest request, CancellationToken ct)
        => Ok(await notifications.RegisterAsync(UserId,
            request.DeviceId is null ? request with { DeviceId = CurrentUser.DeviceId } : request, ct));

    [HttpDelete]
    public async Task<IActionResult> Unregister([FromQuery] string endpoint, CancellationToken ct)
    {
        await notifications.UnregisterAsync(UserId, endpoint, ct);
        return NoContent();
    }
}
