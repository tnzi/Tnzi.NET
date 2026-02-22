using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Template.Services;

/// <summary>
/// 模板存储服务接口
/// </summary>
public interface ITemplateStoreService
{
    /// <summary>
    /// 根据名称、模块和分类获取模板（内部使用，返回实体）
    /// </summary>
    Task<Result<TemplateEntity>> GetTemplateAsync(string templateName, string module, string? category = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据ID获取模板
    /// </summary>
    Task<Result<TemplateDto>> GetTemplateByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建模板
    /// </summary>
    Task<Result<TemplateDto>> CreateTemplateAsync(CreateTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新模板
    /// </summary>
    Task<Result<TemplateDto>> UpdateTemplateAsync(Guid id, UpdateTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除模板
    /// </summary>
    Task<Result> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量删除模板
    /// </summary>
    Task<Result> DeleteTemplatesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有模板（不分页）
    /// </summary>
    Task<Result<IEnumerable<TemplateDto>>> GetAllTemplatesAsync(string? module = null, string? category = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询模板列表（支持分页和过滤）
    /// </summary>
    Task<Result<IPagedList<TemplateInfoDto>>> QueryTemplatesAsync(QueryTemplateRequest request, CancellationToken cancellationToken = default);
}
