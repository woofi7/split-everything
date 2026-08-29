using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Common;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Services;

/// <summary>
/// The single access check for everything group-scoped.
///
/// The app is private and gated by Google sign-in, but "signed in" is not
/// "entitled to this group", so every read and write resolves the caller's active
/// membership here rather than trusting an id from the request.
/// </summary>
internal static class GroupAccess
{
    /// <summary>Active membership of the caller, or a 403.</summary>
    public static async Task<GroupMember> RequireMemberAsync(
        AppDbContext db, Guid userId, Guid groupId, CancellationToken ct = default)
    {
        var exists = await db.Groups.AnyAsync(g => g.Id == groupId && !g.IsDeleted, ct);
        if (!exists) throw new NotFoundException($"Group {groupId}");

        var member = await db.GroupMembers
            .FirstOrDefaultAsync(m =>
                m.GroupId == groupId
                && m.UserId == userId
                && m.Status == MembershipStatus.Active
                && !m.IsDeleted, ct);

        return member ?? throw new ForbiddenException("You are not a member of this group.");
    }

    /// <summary>Membership that also carries the right to change group settings.</summary>
    public static async Task<GroupMember> RequireAdminAsync(
        AppDbContext db, Guid userId, Guid groupId, CancellationToken ct = default)
    {
        var member = await RequireMemberAsync(db, userId, groupId, ct);
        if (member.Role is not (GroupRole.Owner or GroupRole.Admin))
            throw new ForbiddenException("Only a group owner or admin can do this.");
        return member;
    }

    public static async Task<Group> RequireGroupAsync(
        AppDbContext db, Guid groupId, CancellationToken ct = default)
        => await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct)
           ?? throw new NotFoundException($"Group {groupId}");

    public static void RequireWritable(Group group)
    {
        if (group.IsArchived) throw new GroupArchivedException();
    }

    public static string NormalizeCurrency(string? currency, string field = "Currency")
    {
        var trimmed = currency?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetterUpper))
            throw new ValidationException($"{field} must be a three-letter currency code.");
        return trimmed;
    }

    public static string RequireText(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ValidationException($"{field} is required.");
        if (trimmed.Length > maxLength)
            throw new ValidationException($"{field} must be at most {maxLength} characters.");
        return trimmed;
    }
}
