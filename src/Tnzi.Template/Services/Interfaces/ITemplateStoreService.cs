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

    /// <summary>
    /// 克隆模板（深拷贝，生成新名称）
    /// </summary>
    Task<Result<TemplateDto>> CloneTemplateAsync(Guid sourceId, string newTemplateName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Export templates as JSON (batch export for backup/migration)
    /// </summary>
    /// <param name="module">Filter by module (optional)</param>
    /// <param name="category">Filter by category (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON string containing exported templates</returns>
    Task<Result<string>> ExportTemplatesAsync(string? module = null, string? category = null, CancellationToken cancellationToken = default)
    {
        // Default implementation returns not implemented
        return Task.FromResult(Result.Failure<string>("Export not implemented", 501));
    }

    /// <summary>
    /// Import templates from JSON (batch import, skip duplicates by default)
    /// </summary>
    /// <param name="json">JSON string containing templates to import</param>
    /// <param name="overwriteExisting">Whether to overwrite existing templates with same name+module+category</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result with count of created and skipped templates</returns>
    Task<Result<TemplateImportResultDto>> ImportTemplatesAsync(string json, bool overwriteExisting = false, CancellationToken cancellationToken = default)
    {
        // Default implementation returns not implemented
        return Task.FromResult(Result.Failure<TemplateImportResultDto>("Import not implemented", 501));
    }

    /// <summary>
    /// Discover variables used in a template (parses @Model.XXX references)
    /// </summary>
    Task<Result<TemplateVariablesDto>> GetTemplateVariablesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<TemplateVariablesDto>("Variable discovery not implemented", 501));
    }

    /// <summary>
    /// Batch activate or deactivate templates
    /// </summary>
    Task<Result<int>> BatchActivateAsync(List<Guid> ids, bool isActive, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<int>("Batch activate not implemented", 501));
    }
}
