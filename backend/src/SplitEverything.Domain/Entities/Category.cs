namespace SplitEverything.Domain.Entities;

public class Category
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string IconName { get; set; } = "tag";

    public string ColorHex { get; set; } = "#64748b";

    public int SortOrder { get; set; }

    public Guid? GroupId { get; set; }
    public Group? Group { get; set; }

    public string? KeywordsJson { get; set; }
}
