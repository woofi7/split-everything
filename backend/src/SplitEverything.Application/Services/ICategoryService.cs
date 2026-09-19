using SplitEverything.Application.Contracts.Categories;

namespace SplitEverything.Application.Services;

/// <summary>
/// What an expense can be filed under.
///
/// Two lists in one: the server's own, which every group starts from and whoever
/// runs the server maintains, and a group's, which exists only once that group has
/// edited it. A group that has never touched the subject sees the server's list and
/// nothing else to think about.
/// </summary>
public interface ICategoryService
{
    /// <summary>This group's categories: its own if it has any, otherwise the server's.</summary>
    Task<IReadOnlyList<CategoryDto>> GetForGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// Replaces this group's list, taking a copy of the server's the first time.
    ///
    /// Any member, like the names left out of the totals: it decides how spending
    /// is filed on a screen everybody reads and changes no amount, no balance and
    /// nothing anybody owes.
    /// </summary>
    Task<IReadOnlyList<CategoryDto>> SetForGroupAsync(
        Guid userId, Guid groupId, SetCategoriesRequest request, CancellationToken ct = default);

    /// <summary>The server's own list. For whoever runs it.</summary>
    Task<IReadOnlyList<CategoryDto>> GetGlobalAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryDto>> SetGlobalAsync(
        Guid userId, SetCategoriesRequest request, CancellationToken ct = default);
}
