namespace Tnzi.Authorization.Controllers.Admin;

/// <summary>
/// 模块管理控制器
/// 提供模块CRUD、树形查询等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/modules")]
[ApiAuthorize(PermissionName = "authorization.functionModule.view")]
public class DefaultModuleAdminController : ApiAdminControllerBase
{
    protected readonly IModuleManagementService ModuleManagementService;

    /// <summary>
    /// 初始化模块管理控制器
    /// </summary>
    /// <param name="moduleManagementService">模块管理服务</param>
    public DefaultModuleAdminController(IModuleManagementService moduleManagementService)
    {
        ModuleManagementService = Check.NotNull(moduleManagementService);
    }

    /// <summary>
    /// 获取模块树
    /// </summary>
    /// <returns>模块树</returns>
    [HttpGet("tree")]
    public virtual async Task<ApiResult<IEnumerable<FunctionModule>>> GetModuleTree()
    {
        var result = await ModuleManagementService.GetModuleTreeAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据ID获取模块
    /// </summary>
    /// <param name="id">模块ID</param>
    /// <returns>模块信息</returns>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<FunctionModule>> GetById(Guid id)
    {
        var result = await ModuleManagementService.GetModuleByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取所有模块
    /// </summary>
    /// <returns>模块列表</returns>
    [HttpGet]
    public virtual async Task<ApiResult<IEnumerable<FunctionModule>>> GetModules()
    {
        var result = await ModuleManagementService.GetModulesAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建模块
    /// </summary>
    /// <param name="request">模块信息</param>
    /// <returns>创建的模块</returns>
    [HttpPost]
    public virtual async Task<ApiResult<FunctionModule>> Create([FromBody] CreateFunctionModuleRequest request)
    {
        var result = await ModuleManagementService.CreateModuleAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新模块
    /// </summary>
    /// <param name="id">模块ID</param>
    /// <param name="request">模块信息</param>
    /// <returns>更新后的模块</returns>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<FunctionModule>> Update(Guid id, [FromBody] UpdateFunctionModuleRequest request)
    {
        var result = await ModuleManagementService.UpdateModuleAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除模块
    /// </summary>
    /// <param name="id">模块ID</param>
    /// <returns>操作结果</returns>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await ModuleManagementService.DeleteModuleAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取模块的功能列表
    /// </summary>
    /// <remarks>
    /// Returns <see cref="ModuleFunctionDto"/> rather than the raw entity so that
    /// audit/nav fields do not leak through the admin API surface.
    /// </remarks>
    /// <param name="moduleId">模块ID</param>
    /// <returns>功能列表</returns>
    [HttpGet("{moduleId:guid}/functions")]
    public virtual async Task<ApiResult<IEnumerable<ModuleFunctionDto>>> GetModuleFunctions(Guid moduleId)
    {
        var result = await ModuleManagementService.GetModuleFunctionsAsync(moduleId);
        return result.Map(items => (IEnumerable<ModuleFunctionDto>)items.Select(MapToDto).ToList()).ToApiResult();
    }

    private static ModuleFunctionDto MapToDto(ModuleFunction entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Code = entity.Code,
        Description = entity.Description,
        ModuleId = entity.ModuleId,
        IsEnabled = entity.IsEnabled,
        Order = entity.Order,
        IsSystemManaged = entity.IsSystemManaged,
        Category = entity.Category,
    };

    /// <summary>
    /// 获取模块的子模块
    /// </summary>
    /// <param name="parentId">父模块ID</param>
    /// <returns>子模块列表</returns>
    [HttpGet("{parentId:guid}/children")]
    public virtual async Task<ApiResult<IEnumerable<FunctionModule>>> GetChildren(Guid parentId)
    {
        var result = await ModuleManagementService.GetChildModulesAsync(parentId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 启用模块（级联启用所有子模块及其功能）
    /// </summary>
    /// <param name="id">模块ID</param>
    /// <returns>操作结果</returns>
    [HttpPut("{id:guid}/enable")]
    public virtual async Task<ApiResult> Enable(Guid id)
    {
        var result = await ModuleManagementService.EnableModuleAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 禁用模块（级联禁用所有子模块及其功能）
    /// </summary>
    /// <param name="id">模块ID</param>
    /// <returns>操作结果</returns>
    [HttpPut("{id:guid}/disable")]
    public virtual async Task<ApiResult> Disable(Guid id)
    {
        var result = await ModuleManagementService.DisableModuleAsync(id);
        return result.ToApiResult();
    }

}
