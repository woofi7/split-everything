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

    /// <summary>The signed-in caller, or a 403 from the exception handler.</summary>
    protected Guid UserId => CurrentUser.RequireUserId();
}
