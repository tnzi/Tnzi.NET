namespace Tnzi.Authorization.Controllers.Admin;

/// <summary>
/// 模块管理控制器基类
/// 提供模块CRUD、树形查询等API端点，所有方法支持重写
/// </summary>
[Route("admin/modules")]
public abstract class ModuleAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IModuleManagementService ModuleManagementService;

    /// <summary>
    /// 初始化模块管理控制器基类
    /// </summary>
    /// <param name="moduleManagementService">模块管理服务</param>
    protected ModuleAdminControllerBase(IModuleManagementService moduleManagementService)
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
    /// <param name="moduleId">模块ID</param>
    /// <returns>功能列表</returns>
    [HttpGet("{moduleId:guid}/functions")]
    public virtual async Task<ApiResult<IEnumerable<ModuleFunction>>> GetModuleFunctions(Guid moduleId)
    {
        var result = await ModuleManagementService.GetModuleFunctionsAsync(moduleId);
        return result.ToApiResult();
    }

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

}
