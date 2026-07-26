namespace Tnzi.Data;

/// <summary>
/// 分页查询 DTO 基类 / fallback concrete query for endpoints that only
/// need `pageIndex` + `pageSize`.
/// </summary>
/// <remarks>
/// Originally declared <c>abstract</c> so every concrete subclass had to
/// supply its own defaults. That broke ASP.NET model binding for any
/// controller that took <c>[FromQuery] PagedQueryDto query</c> directly -
/// the binder throws <c>InvalidOperationException: Model bound complex
/// types must not be abstract or value types</c> and the request 500s.
///
/// Concrete now so endpoints (e.g. <c>GET /admin/roles/{id}/users</c>,
/// <c>GET /admin/organizations/{id}/users</c>) that don't need any custom
/// filter shape can bind this base type and still get the page-default
/// behaviour. Subclasses keep overriding <see cref="DefaultPageIndex"/> /
/// <see cref="DefaultPageSize"/> / <see cref="MaxPageSize"/> exactly as
/// before.
/// </remarks>
public class PagedQueryDto : PagedQuery
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

