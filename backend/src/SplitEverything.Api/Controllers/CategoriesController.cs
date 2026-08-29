using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Api.Controllers;

public sealed record CategoryDto(Guid Id, string Key, string Name, string Emoji, string ColorHex, int SortOrder);

public sealed class CategoriesController(
    ICurrentUser currentUser,
    AppDbContext db) : ApiControllerBase(currentUser)
{
    /// <summary>System categories plus any the caller added themselves.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List(CancellationToken ct)
        => Ok(await db.Categories
            .Where(c => c.OwnerUserId == null || c.OwnerUserId == UserId)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Key, c.Name, c.Emoji, c.ColorHex, c.SortOrder))
            .ToListAsync(ct));
}
