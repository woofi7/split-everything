using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Notifications;
using SplitEverything.Application.Services;

namespace SplitEverything.Api.Controllers;

public sealed class ActivityController(
    ICurrentUser currentUser,
    IActivityService activity) : ApiControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<Paged<ActivityEntryDto>>> List(
        [FromQuery] Guid? groupId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await activity.ListAsync(UserId, groupId, new PageRequest(page, pageSize), ct));
}
