namespace SplitEverything.Application.Contracts.Categories;

public sealed record CategoryDto(
    string Key,
    string Name,
    string IconName,
    string ColorHex,
    int SortOrder,
    IReadOnlyList<string> Keywords);

public sealed record CategoryInputDto(
    string Name,
    string? Key = null,
    string? IconName = null,
    string? ColorHex = null,
    IReadOnlyList<string>? Keywords = null);

public sealed record SetCategoriesRequest(IReadOnlyList<CategoryInputDto> Categories);
