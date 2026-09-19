using System.Security.Claims;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;

namespace SplitEverything.Api.Infrastructure;

public sealed class CurrentUserAccessor(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string DeviceHeader = "X-Device-Id";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId
        => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? Principal?.FindFirstValue("sub"), out var id)
            ? id
            : null;

    public string? Email
        => Principal?.FindFirstValue(ClaimTypes.Email) ?? Principal?.FindFirstValue("email");

    public string? DeviceId
    {
        get
        {
            var header = accessor.HttpContext?.Request.Headers[DeviceHeader].ToString();
            return string.IsNullOrWhiteSpace(header) ? Principal?.FindFirstValue("device") : header.Trim();
        }
    }

    public bool IsAuthenticated => UserId is not null;

    public Guid RequireUserId()
        => UserId ?? throw new ForbiddenException("You must be signed in.");
}
