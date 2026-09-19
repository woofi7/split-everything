using SplitEverything.Application.Contracts.Categories;

namespace SplitEverything.Application.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> GetForGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryDto>> SetForGroupAsync(
        Guid userId, Guid groupId, SetCategoriesRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryDto>> GetGlobalAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryDto>> SetGlobalAsync(
        Guid userId, SetCategoriesRequest request, CancellationToken ct = default);
}
