namespace Tnzi.Data;

/// <summary>
/// 分页查询 DTO 基类
/// </summary>
public abstract class PagedQueryDto : PagedQuery
{
    /// <summary>
    /// 默认页码（子类可重写）
    /// </summary>
    protected virtual int DefaultPageIndex => 1;

    /// <summary>
    /// 默认每页数量（子类可重写）
    /// </summary>
    protected virtual int DefaultPageSize => 10;

    /// <summary>
    /// 最大每页数量（子类可重写，0 表示不限制）
    /// </summary>
    protected virtual int MaxPageSize => 100;

    private int? _pageIndex;
    private int? _pageSize;

    /// <summary>
    /// 页码（从1开始，支持自定义默认值）
    /// </summary>
    public new int PageIndex
    {
        get => _pageIndex ?? DefaultPageIndex;
        set
        {
            _pageIndex = value > 0 ? value : null;
            // Sync to base class so that Skip/Take (defined on PagedQuery) use the correct value.
            base.PageIndex = _pageIndex ?? DefaultPageIndex;
        }
    }

    /// <summary>
    /// 每页数量（支持自定义默认值和最大值限制）
    /// </summary>
    public new int PageSize
    {
        get
        {
            if (!_pageSize.HasValue)
                return DefaultPageSize;

            if (MaxPageSize > 0 && _pageSize.Value > MaxPageSize)
                return MaxPageSize;

            return _pageSize.Value;
        }
        set
        {
            _pageSize = value > 0 ? value : null;
            // Sync to base class so that Skip/Take (defined on PagedQuery) use the correct value.
            base.PageSize = _pageSize.HasValue
                ? (MaxPageSize > 0 && _pageSize.Value > MaxPageSize ? MaxPageSize : _pageSize.Value)
                : DefaultPageSize;
        }
    }
}

