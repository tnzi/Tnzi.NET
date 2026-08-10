namespace Tnzi.Template.Controllers.Admin;

/// <summary>
/// 模板管理控制器
/// 提供模板CRUD等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/templates")]
[ApiAuthorize(PermissionName = "template.template.view")]
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
    [ApiAuthorize(PermissionName = "template.template.create")]
    public virtual async Task<ApiResult<TemplateDto>> Create([FromBody] CreateTemplateRequest request)
    {
        var result = await TemplateStoreService.CreateTemplateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新模板
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "template.template.update")]
    public virtual async Task<ApiResult<TemplateDto>> Update(Guid id, [FromBody] UpdateTemplateRequest request)
    {
        var result = await TemplateStoreService.UpdateTemplateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除模板
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "template.template.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await TemplateStoreService.DeleteTemplateAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除模板
    /// </summary>
    [HttpDelete("batch")]
    [ApiAuthorize(PermissionName = "template.template.delete")]
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
    /// <remarks>
    /// <b>为什么是写码</b>：入参 <c>Content</c> 是调用方随请求送来的 Razor 源码，验证要把它
    /// <b>编译</b>出来 —— 编译期就能跑代码（<c>@{ }</c> 块、构造函数、类型初始化器）。
    /// 这不是"看一眼模板长什么样"，而是在服务器上执行调用方给的代码，与 <c>.view</c> 不是
    /// 一个风险等级。放在 <c>.update</c> 上不给作者任何新能力（能改模板的人本来就能让自己的
    /// Razor 被执行 —— 存下来等着被渲染即可），但把这件事从只读用户手里拿走了。
    /// 见 <see cref="Preview"/> 的同款说明。
    /// </remarks>
    [HttpPost("validate")]
    [ApiAuthorize(PermissionName = "template.template.update")]
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
    [ApiAuthorize(PermissionName = "template.template.create")]
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
    [ApiAuthorize(PermissionName = "template.template.create")]
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
    [ApiAuthorize(PermissionName = "template.template.update")]
    public virtual async Task<ApiResult<int>> BatchActivate([FromBody] BatchActivateRequest request)
    {
        var result = await TemplateStoreService.BatchActivateAsync(request.Ids, request.IsActive);
        return result.ToApiResult();
    }

    /// <summary>
    /// 预览模板渲染结果
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>这个端点会在服务器上执行调用方送来的代码</b>。<c>Content</c> 是随请求送来的 Razor
    /// 源码，<c>RenderAsync</c> 把它编译成 C# 并运行；Razor 模板里能写任意 C#，
    /// 也就能读文件、发起网络请求、拿连接串。它此前只跟类级 <c>template.template.view</c> 这道
    /// 只读门 —— 于是"能看模板列表"与"能在服务器上跑任意代码"是同一个权限。
    /// </para>
    /// <para>
    /// 改为叠加 <c>.update</c>：能改模板的人本来就能让自己的 Razor 被执行（存下来等着被渲染
    /// 即可），所以这对作者不是新能力；对只读用户则是把一条提权路径关上了。
    /// </para>
    /// <para>
    /// <b>这不是沙箱</b>。真正限制 Razor 能做什么（限定可引用程序集 / 独立进程 / 换成非图灵完备
    /// 的模板语言）仍是 Roadmap 上的 <c>Sandbox security</c>；本处只是把权限门校准到与实际
    /// 能力相称。持有 <c>template.template.update</c> 就等于持有本进程的代码执行能力，
    /// 授予时按这个量级来衡量。
    /// </para>
    /// </remarks>
    [HttpPost("preview")]
    [ApiAuthorize(PermissionName = "template.template.update")]
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
