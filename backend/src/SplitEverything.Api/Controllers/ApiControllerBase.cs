using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;

namespace SplitEverything.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class ApiControllerBase(ICurrentUser currentUser) : ControllerBase
{
    protected ICurrentUser CurrentUser { get; } = currentUser;

    protected Guid UserId => CurrentUser.RequireUserId();
}
