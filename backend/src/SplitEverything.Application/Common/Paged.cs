namespace SplitEverything.Application.Common;

public sealed record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public bool HasMore => Page * PageSize < Total;
}

public sealed record PageRequest(int Page = 1, int PageSize = 50)
{
    public int Skip => (Math.Max(1, Page) - 1) * Clamped;
    public int Clamped => Math.Clamp(PageSize, 1, 200);
}
