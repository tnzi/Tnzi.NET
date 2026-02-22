namespace Tnzi.Template.Services;

/// <summary>
/// 布局存储服务接口
/// </summary>
public interface ILayoutStoreService
{
    /// <summary>
    /// 根据名称、模块和分类获取布局（内部使用，返回实体）
    /// </summary>
    Task<Result<Layout>> GetLayoutAsync(string layoutName, string module, string? category = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据ID获取布局
    /// </summary>
    Task<Result<LayoutDto>> GetLayoutByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取默认布局（内部使用，返回实体）
    /// </summary>
    Task<Result<Layout>> GetDefaultLayoutAsync(string module, string category, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建布局
    /// </summary>
    Task<Result<LayoutDto>> CreateLayoutAsync(CreateLayoutRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新布局
    /// </summary>
    Task<Result<LayoutDto>> UpdateLayoutAsync(Guid id, UpdateLayoutRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除布局
    /// </summary>
    Task<Result> DeleteLayoutAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量删除布局
    /// </summary>
    Task<Result> DeleteLayoutsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有布局（不分页）
    /// </summary>
    Task<Result<IEnumerable<LayoutDto>>> GetAllLayoutsAsync(string? module = null, string? category = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询布局列表（支持分页和过滤）
    /// </summary>
    Task<Result<IPagedList<LayoutInfoDto>>> QueryLayoutsAsync(QueryLayoutRequest request, CancellationToken cancellationToken = default);
}
