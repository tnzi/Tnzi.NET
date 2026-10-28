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
/// <remarks>
/// <para>
/// 页码与页长<b>只有一个事实源</b>：<see cref="Skip"/> / <see cref="Take"/> 与回报给调用方的
/// <see cref="PageIndex"/> / <see cref="PageSize"/> 读的是同一对属性，默认值与上限由子类重写
/// <see cref="DefaultPageIndex"/> / <see cref="DefaultPageSize"/> / <see cref="MaxPageSize"/> 决定。
/// </para>
/// <para>
/// ★ 此前默认值与上限只在 <see cref="PagedQueryDto"/> 上，它用 <c>new</c> 隐藏这两个属性、只在 setter 里同步基类：
/// 请求不带 <c>pageSize</c> 时 setter 不执行，<c>Take</c> 停在 10 而响应宣称 20，第 N 页跳过 (N-1)*10 行，
/// 后一半记录永远翻不到；<c>MaxPageSize = 0</c>（不限制）的子类给 500 时基类又把它改回 10。
/// </para>
/// </remarks>
public class PagedQuery
{
    private int? _pageIndex;
    private int? _pageSize;

    /// <summary>
    /// 默认页码（子类可重写）
    /// </summary>
    protected virtual int DefaultPageIndex => 1;

    /// <summary>
    /// 默认每页数量（子类可重写）
    /// </summary>
    protected virtual int DefaultPageSize => 10;

    /// <summary>
    /// 最大每页数量（子类可重写，0 表示不限制）。超过上限的请求被夹到上限，不报错。
    /// </summary>
    protected virtual int MaxPageSize => 100;

    /// <summary>
    /// 页码（从 1 开始；未提供或不大于 0 时取 <see cref="DefaultPageIndex"/>）
    /// </summary>
    public int PageIndex
    {
        get => _pageIndex ?? DefaultPageIndex;
        set => _pageIndex = value > 0 ? value : null;
    }

    /// <summary>
    /// 每页数量（未提供或不大于 0 时取 <see cref="DefaultPageSize"/>；超过 <see cref="MaxPageSize"/> 时夹到上限）
    /// </summary>
    public int PageSize
    {
        get
        {
            var size = _pageSize ?? DefaultPageSize;
            return MaxPageSize > 0 && size > MaxPageSize ? MaxPageSize : size;
        }
        set => _pageSize = value > 0 ? value : null;
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

