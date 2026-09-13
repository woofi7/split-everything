using SplitEverything.Application.Contracts.Admin;

namespace SplitEverything.Application.Services;

/// <summary>
/// What the person who runs the server can do that a member cannot.
///
/// Every method takes the caller and checks it again here rather than trusting the
/// controller: administration is the one place where a missing check is not a bug
/// in a screen but a way into everybody's data.
/// </summary>
public interface IAdminService
{
    /// <summary>Whether this user administers the server. False for everyone on an install with no administrator configured.</summary>
    Task<bool> IsAdminAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Every group on the server, archived ones included, newest activity first.</summary>
    Task<IReadOnlyList<AdminGroupDto>> GetGroupsAsync(Guid userId, CancellationToken ct = default);

    Task<AdminGroupDetailDto> GetGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// Removes a group and everything in it, for good. Only an archived group:
    /// archiving is the reversible step, and this is the one that is not.
    /// </summary>
    Task DeleteGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default);
}
