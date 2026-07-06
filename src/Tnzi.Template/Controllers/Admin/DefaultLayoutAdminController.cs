namespace Tnzi.Template.Controllers.Admin;

/// <summary>
/// 布局管理控制器
/// 提供布局CRUD等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/layouts")]
[ApiAuthorize(PermissionName = "template.layout.view")]
public class DefaultLayoutAdminController : ApiAdminControllerBase
{
    protected readonly ILayoutStoreService LayoutStoreService;

    /// <summary>
    /// 初始化布局管理控制器
    /// </summary>
    public DefaultLayoutAdminController(ILayoutStoreService layoutStoreService)
    {
        LayoutStoreService = Check.NotNull(layoutStoreService);
    }

    /// <summary>
    /// 根据ID获取布局
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<LayoutDto>> GetById(Guid id)
    {
        var result = await LayoutStoreService.GetLayoutByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建布局
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<LayoutDto>> Create([FromBody] CreateLayoutRequest request)
    {
        var result = await LayoutStoreService.CreateLayoutAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新布局
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<LayoutDto>> Update(Guid id, [FromBody] UpdateLayoutRequest request)
    {
        var result = await LayoutStoreService.UpdateLayoutAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除布局
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await LayoutStoreService.DeleteLayoutAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除布局
    /// </summary>
    [HttpDelete("batch")]
    public virtual async Task<ApiResult> DeleteBatch([FromBody] IEnumerable<Guid> ids)
    {
        var result = await LayoutStoreService.DeleteLayoutsAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据名称、模块和分类获取布局
    /// </summary>
    [HttpGet("name/{layoutName}")]
    public virtual async Task<ApiResult<LayoutDto>> GetLayout(string layoutName, [FromQuery] string module, [FromQuery] string? category = null)
    {
        var result = await LayoutStoreService.GetLayoutAsync(layoutName, module, category);
        return result.Map(e => e.MapTo<LayoutDto>()).ToApiResult();
    }

    /// <summary>
    /// 获取默认布局
    /// </summary>
    [HttpGet("default")]
    public virtual async Task<ApiResult<LayoutDto>> GetDefaultLayout([FromQuery] string module, [FromQuery] string category)
    {
        var result = await LayoutStoreService.GetDefaultLayoutAsync(module, category);
        return result.Map(e => e.MapTo<LayoutDto>()).ToApiResult();
    }

    /// <summary>
    /// 克隆布局
    /// </summary>
    [HttpPost("{id:guid}/clone")]
    public virtual async Task<ApiResult<LayoutDto>> Clone(Guid id, [FromQuery] string newName)
    {
        var result = await LayoutStoreService.CloneLayoutAsync(id, newName);
        return result.ToApiResult();
    }

    /// <summary>
    /// 查询布局列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<LayoutInfoDto>>> GetAllLayouts([FromQuery] QueryLayoutRequest request)
    {
        var result = await LayoutStoreService.QueryLayoutsAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// Export layouts as JSON
    /// </summary>
    [HttpGet("export")]
    public virtual async Task<ApiResult<string>> Export([FromQuery] string? module = null, [FromQuery] string? category = null)
    {
        var result = await LayoutStoreService.ExportLayoutsAsync(module, category);
        return result.ToApiResult();
    }

    /// <summary>
    /// Import layouts from JSON
    /// </summary>
    [HttpPost("import")]
    public virtual async Task<ApiResult<LayoutImportResultDto>> Import([FromBody] LayoutImportRequest request)
    {
        var result = await LayoutStoreService.ImportLayoutsAsync(request.Json, request.OverwriteExisting);
        return result.ToApiResult();
    }

    /// <summary>
    /// Batch activate or deactivate layouts
    /// </summary>
    [HttpPost("batch-activate")]
    public virtual async Task<ApiResult<int>> BatchActivate([FromBody] BatchActivateRequest request)
    {
        var result = await LayoutStoreService.BatchActivateAsync(request.Ids, request.IsActive);
        return result.ToApiResult();
    }
}
