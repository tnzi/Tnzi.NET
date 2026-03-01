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

    /// <summary>
    /// 克隆布局（深拷贝，生成新名称）
    /// </summary>
    Task<Result<LayoutDto>> CloneLayoutAsync(Guid sourceId, string newLayoutName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Export layouts as JSON (batch export for backup/migration)
    /// </summary>
    Task<Result<string>> ExportLayoutsAsync(string? module = null, string? category = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<string>("Export not implemented", 501));
    }

    /// <summary>
    /// Import layouts from JSON (batch import, skip duplicates by default)
    /// </summary>
    Task<Result<LayoutImportResultDto>> ImportLayoutsAsync(string json, bool overwriteExisting = false, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<LayoutImportResultDto>("Import not implemented", 501));
    }

    /// <summary>
    /// Batch activate or deactivate layouts
    /// </summary>
    Task<Result<int>> BatchActivateAsync(List<Guid> ids, bool isActive, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<int>("Batch activate not implemented", 501));
    }
}
