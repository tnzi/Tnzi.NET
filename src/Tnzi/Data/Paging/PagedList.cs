namespace Tnzi.Data;

/// <summary>
/// 分页列表接口
/// </summary>
public interface IPagedList<T>
{
    int PageIndex { get; }
    int PageSize { get; }
    int TotalCount { get; }
    int TotalPages { get; }
    bool HasPreviousPage { get; }
    bool HasNextPage { get; }
    IList<T> Items { get; }
}

/// <summary>
/// 分页查询参数
/// </summary>
public class PagedQuery
{
    private int _pageIndex = 1;
    private int _pageSize = 10;

    public int PageIndex
    {
        get => _pageIndex;
        set => _pageIndex = value > 0 ? value : 1;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value > 0 && value <= 100 ? value : 10; // 最大100条
    }

    /// <summary>
    /// 排序表达式，例如 "Name DESC" 或 "CreationTime DESC, Id ASC"
    /// 为 null 时，仓储层按主键默认排序
    /// </summary>
    public string? OrderBy { get; set; }

    /// <summary>
    /// Dynamic filter group for building query predicates.
    /// When set, the repository will apply FilterExpressionBuilder to build WHERE clauses.
    /// </summary>
    public FilterGroup? Filter { get; set; }

    public int Skip => (PageIndex - 1) * PageSize;
    public int Take => PageSize;
}

/// <summary>
/// 分页列表实现（Core版本，不依赖EntityFrameworkCore）
/// </summary>
public class PagedList<T> : IPagedList<T>
{
    public int PageIndex { get; }
    public int PageSize { get; }
    public int TotalCount { get; }
    public int TotalPages { get; }
    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage => PageIndex < TotalPages;
    public IList<T> Items { get; }

    public PagedList(IList<T> items, int pageIndex, int pageSize, int totalCount)
    {
        PageIndex = pageIndex;
        PageSize = pageSize;
        TotalCount = totalCount;
        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        Items = items;
    }

    public static PagedList<T> Create(IEnumerable<T> source, int pageIndex, int pageSize)
    {
        // 避免不必要的复制：优先复用已有的 IList<T>
        var list = source as IList<T> ?? source.ToList();
        var totalCount = list.Count;

        // 使用索引范围高效分页，避免 Skip/Take 的迭代开销
        var skip = (pageIndex - 1) * pageSize;
        var take = Math.Min(pageSize, Math.Max(0, totalCount - skip));

        List<T> items;
        if (skip >= totalCount)
        {
            items = new List<T>();
        }
        else if (list is List<T> concreteList)
        {
            items = concreteList.GetRange(skip, take);
        }
        else
        {
            items = new List<T>(take);
            for (int i = skip; i < skip + take; i++)
            {
                items.Add(list[i]);
            }
        }

        return new PagedList<T>(items, pageIndex, pageSize, totalCount);
    }


}

