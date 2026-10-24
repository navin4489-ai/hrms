using Microsoft.EntityFrameworkCore;

namespace Wintech.Hrms.Api.Common.Paging;

/// <summary>Paging query parameters accepted by every list endpoint.</summary>
public sealed class PageRequest
{
    private int _page = 1;
    private int _pageSize = 20;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, 100);
    }

    public static PageRequest Of(int page = 1, int pageSize = 20) => new() { Page = page, PageSize = pageSize };
}

/// <summary>Standard paged envelope: { items, page, pageSize, total, hasNextPage }.</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
    public bool HasNextPage => Page * PageSize < Total;

    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int total) =>
        new() { Items = items, Page = page, PageSize = pageSize, Total = total };
}

public static class PagingExtensions
{
    /// <summary>Applies paging to a query and maps the page to DTOs in a single round-trip-friendly call.</summary>
    public static async Task<PagedResult<TDto>> ToPagedAsync<T, TDto>(
        this IQueryable<T> query,
        PageRequest request,
        Func<T, TDto> map,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var page = request.Page;
        var pageSize = request.PageSize;
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return PagedResult<TDto>.Create(items.Select(map).ToList(), page, pageSize, total);
    }

    /// <summary>Pages an in-memory list (small collections only).</summary>
    public static PagedResult<T> ToPaged<T>(this IEnumerable<T> source, PageRequest request)
    {
        var page = request.Page;
        var pageSize = request.PageSize;
        var items = source as IList<T> ?? source.ToList();
        return PagedResult<T>.Create(items.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, items.Count);
    }
}
