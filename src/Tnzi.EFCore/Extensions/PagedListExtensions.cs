
namespace Tnzi.EFCore.Extensions;

/// <summary>
/// PagedList 扩展方法（EF Core 异步支持）
/// </summary>
public static class PagedListExtensions
{
    /// <summary>
    /// 从 IQueryable 创建分页列表（异步）
    /// </summary>
    public static async Task<IPagedList<T>> CreateAsync<T>(this IQueryable<T> source, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        var totalCount = await source.CountAsync(cancellationToken);
        var items = await source.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedList<T>(items, pageIndex, pageSize, totalCount);
    }

    /// <summary>
    /// 从 IQueryable 创建分页列表（异步，使用 PagedQueryDto）
    /// </summary>
    public static async Task<IPagedList<T>> CreateAsync<T>(this IQueryable<T> source, PagedQueryDto query, CancellationToken cancellationToken = default)
    {
        var totalCount = await source.CountAsync(cancellationToken);
        var items = await source.Skip(query.Skip).Take(query.Take).ToListAsync(cancellationToken);
        return new PagedList<T>(items, query.PageIndex, query.PageSize, totalCount);
    }
}