namespace Tnzi.Authorization.Controllers.Admin;

/// <summary>
/// 功能管理控制器基类
/// 提供功能CRUD、启用/禁用等API端点，所有方法支持重写
/// </summary>
[Route("admin/module-functions")]
public abstract class ModuleFunctionAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IModuleManagementService ModuleManagementService;

    /// <summary>
    /// 初始化功能管理控制器基类
    /// </summary>
    /// <param name="moduleManagementService">模块管理服务</param>
    protected ModuleFunctionAdminControllerBase(IModuleManagementService moduleManagementService)
    {
        ModuleManagementService = Check.NotNull(moduleManagementService);
    }

    /// <summary>
    /// 根据ID获取功能
    /// </summary>
    /// <param name="id">功能ID</param>
    /// <returns>功能信息</returns>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<ModuleFunction>> GetById(Guid id)
    {
        var result = await ModuleManagementService.GetModuleFunctionByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取模块的功能列表
    /// </summary>
    /// <param name="moduleId">模块ID</param>
    /// <returns>功能列表</returns>
    [HttpGet("module/{moduleId:guid}")]
    public virtual async Task<ApiResult<IEnumerable<ModuleFunction>>> GetByModuleId(Guid moduleId)
    {
        var result = await ModuleManagementService.GetModuleFunctionsAsync(moduleId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建功能
    /// </summary>
    /// <param name="request">功能信息</param>
    /// <returns>创建的功能</returns>
    [HttpPost]
    public virtual async Task<ApiResult<ModuleFunction>> Create([FromBody] CreateModuleFunctionRequest request)
    {
        var result = await ModuleManagementService.CreateModuleFunctionAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新功能
    /// </summary>
    /// <param name="id">功能ID</param>
    /// <param name="request">功能信息</param>
    /// <returns>更新后的功能</returns>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<ModuleFunction>> Update(Guid id, [FromBody] UpdateModuleFunctionRequest request)
    {
        var result = await ModuleManagementService.UpdateModuleFunctionAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除功能
    /// </summary>
    /// <param name="id">功能ID</param>
    /// <returns>操作结果</returns>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await ModuleManagementService.DeleteModuleFunctionAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 启用功能
    /// </summary>
    /// <param name="id">功能ID</param>
    /// <returns>操作结果</returns>
    [HttpPost("{id:guid}/enable")]
    public virtual async Task<ApiResult> Enable(Guid id)
    {
        var result = await ModuleManagementService.EnableModuleFunctionAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 禁用功能
    /// </summary>
    /// <param name="id">功能ID</param>
    /// <returns>操作结果</returns>
    [HttpPost("{id:guid}/disable")]
    public virtual async Task<ApiResult> Disable(Guid id)
    {
        var result = await ModuleManagementService.DisableModuleFunctionAsync(id);
        return result.ToApiResult();
    }

}
