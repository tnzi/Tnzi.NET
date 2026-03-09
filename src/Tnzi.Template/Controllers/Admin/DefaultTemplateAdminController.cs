namespace Tnzi.Template.Controllers.Admin;

/// <summary>
/// 模板管理控制器
/// 提供模板CRUD等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/templates")]
public class DefaultTemplateAdminController : ApiAdminControllerBase
{
    protected readonly ITemplateStoreService TemplateStoreService;
    protected readonly ITemplateEngine? TemplateEngine;

    /// <summary>
    /// 初始化模板管理控制器
    /// </summary>
    public DefaultTemplateAdminController(ITemplateStoreService templateStoreService, ITemplateEngine? templateEngine = null)
    {
        TemplateStoreService = Check.NotNull(templateStoreService);
        TemplateEngine = templateEngine;
    }

    /// <summary>
    /// 根据ID获取模板
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<TemplateDto>> GetById(Guid id)
    {
        var result = await TemplateStoreService.GetTemplateByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建模板
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<TemplateDto>> Create([FromBody] CreateTemplateRequest request)
    {
        var result = await TemplateStoreService.CreateTemplateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新模板
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<TemplateDto>> Update(Guid id, [FromBody] UpdateTemplateRequest request)
    {
        var result = await TemplateStoreService.UpdateTemplateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除模板
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await TemplateStoreService.DeleteTemplateAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除模板
    /// </summary>
    [HttpDelete("batch")]
    public virtual async Task<ApiResult> DeleteBatch([FromBody] IEnumerable<Guid> ids)
    {
        var result = await TemplateStoreService.DeleteTemplatesAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据名称、模块和分类获取模板
    /// </summary>
    [HttpGet("name/{templateName}")]
    public virtual async Task<ApiResult<TemplateDto>> GetTemplate(string templateName, [FromQuery] string module, [FromQuery] string? category = null)
    {
        var result = await TemplateStoreService.GetTemplateAsync(templateName, module, category);
        return result.Map(e => e.MapTo<TemplateDto>()).ToApiResult();
    }

    /// <summary>
    /// 查询模板列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<TemplateInfoDto>>> GetAllTemplates([FromQuery] QueryTemplateRequest request)
    {
        var result = await TemplateStoreService.QueryTemplatesAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 验证模板语法
    /// </summary>
    [HttpPost("validate")]
    public virtual async Task<ApiResult<TemplateValidationResult>> Validate([FromBody] ValidateTemplateRequest request)
    {
        if (TemplateEngine == null)
        {
            return ApiResult<TemplateValidationResult>.Error("Template engine is not available", 503);
        }

        var result = await TemplateEngine.ValidateAsync(request.Content);
        return ApiResult<TemplateValidationResult>.Ok(result);
    }

    /// <summary>
    /// 克隆模板
    /// </summary>
    [HttpPost("{id:guid}/clone")]
    public virtual async Task<ApiResult<TemplateDto>> Clone(Guid id, [FromQuery] string newName)
    {
        var result = await TemplateStoreService.CloneTemplateAsync(id, newName);
        return result.ToApiResult();
    }

    /// <summary>
    /// Export templates as JSON
    /// </summary>
    [HttpGet("export")]
    public virtual async Task<ApiResult<string>> Export([FromQuery] string? module = null, [FromQuery] string? category = null)
    {
        var result = await TemplateStoreService.ExportTemplatesAsync(module, category);
        return result.ToApiResult();
    }

    /// <summary>
    /// Import templates from JSON
    /// </summary>
    [HttpPost("import")]
    public virtual async Task<ApiResult<TemplateImportResultDto>> Import([FromBody] TemplateImportRequest request)
    {
        var result = await TemplateStoreService.ImportTemplatesAsync(request.Json, request.OverwriteExisting);
        return result.ToApiResult();
    }

    /// <summary>
    /// Discover template variables (@Model.XXX references)
    /// </summary>
    [HttpGet("{id:guid}/variables")]
    public virtual async Task<ApiResult<TemplateVariablesDto>> GetVariables(Guid id)
    {
        var result = await TemplateStoreService.GetTemplateVariablesAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// Batch activate or deactivate templates
    /// </summary>
    [HttpPost("batch-activate")]
    public virtual async Task<ApiResult<int>> BatchActivate([FromBody] BatchActivateRequest request)
    {
        var result = await TemplateStoreService.BatchActivateAsync(request.Ids, request.IsActive);
        return result.ToApiResult();
    }

    /// <summary>
    /// 预览模板渲染结果
    /// </summary>
    [HttpPost("preview")]
    public virtual async Task<ApiResult<string>> Preview([FromBody] PreviewTemplateRequest request)
    {
        if (TemplateEngine == null)
        {
            return ApiResult<string>.Error("Template engine is not available", 503);
        }

        var rendered = await TemplateEngine.RenderAsync(request.Content, request.Model);

        // 如果提供了布局内容，应用布局
        if (!string.IsNullOrWhiteSpace(request.LayoutContent))
        {
            rendered = await TemplateEngine.RenderAsync(request.LayoutContent, new Dictionary<string, object> { ["Content"] = rendered });
        }

        return ApiResult<string>.Ok(rendered);
    }
}
