namespace EduCenter.SharedKernel;

/// <summary>
/// One page of a list endpoint. Lists that used to return every row (users, audit logs) grew past the point
/// where a browser could render them, so the shape is shared rather than re-invented per controller: the
/// client needs <see cref="Total"/> to draw the pager and cannot derive it from <see cref="Items"/>.
/// </summary>
public record Page<T>(IReadOnlyList<T> Items, int Total, int PageNumber, int PageSize)
{
    public int PageCount => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

/// <summary>Page/size query parameters, clamped so a caller cannot ask for a million rows.</summary>
public record PageRequest
{
    public const int DefaultSize = 25;
    public const int MaxSize = 200;

    public int Page { get; init; } = 1;
    public int Size { get; init; } = DefaultSize;

    public int Skip => (Math.Max(Page, 1) - 1) * Take;
    public int Take => Math.Clamp(Size, 1, MaxSize);
    public int Number => Math.Max(Page, 1);
}
