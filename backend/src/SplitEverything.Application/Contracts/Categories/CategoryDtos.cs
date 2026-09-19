namespace SplitEverything.Application.Contracts.Categories;

/// <summary>
/// One category as a screen reads it: what to call it, what to draw, and the words
/// that file an expense here without anybody choosing.
/// </summary>
public sealed record CategoryDto(
    string Key,
    string Name,
    string IconName,
    string ColorHex,
    int SortOrder,
    IReadOnlyList<string> Keywords);

/// <summary>
/// One line of an edited list.
///
/// The key can be left out for a category being invented on the spot: the server
/// makes one from the name, which is one less thing for a person adding "Ski" to
/// think about. Sending a key keeps an existing category's expenses attached to it,
/// which is what renaming means.
/// </summary>
public sealed record CategoryInputDto(
    string Name,
    string? Key = null,
    string? IconName = null,
    string? ColorHex = null,
    IReadOnlyList<string>? Keywords = null);

/// <summary>
/// The whole list, every time. It is edited as a list on one screen, and a patch of
/// one line would be a merge nobody asked for - and the order of the list is the
/// order it is shown in.
/// </summary>
public sealed record SetCategoriesRequest(IReadOnlyList<CategoryInputDto> Categories);
