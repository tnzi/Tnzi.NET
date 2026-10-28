namespace Tnzi.Authorization.Controllers.Admin;

/// <summary>
/// 功能管理控制器
/// 提供功能CRUD、启用/禁用等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/module-functions")]
[ApiAuthorize(PermissionName = "authorization.permission.view")]
public class DefaultModuleFunctionAdminController : ApiAdminControllerBase
{
    protected readonly IModuleManagementService ModuleManagementService;

    /// <summary>
    /// 初始化功能管理控制器
    /// </summary>
    /// <param name="moduleManagementService">模块管理服务</param>
    public DefaultModuleFunctionAdminController(IModuleManagementService moduleManagementService)
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
    /// 权限目录：全部模块（平铺）连同各自的全部功能，含停用与退役行
    /// </summary>
    /// <remarks>
    /// 权限目录页的数据源，挂在本控制器的 <c>authorization.permission.view</c> 门下：
    /// 只管权限点的人不必再持有模块管理的 <c>authorization.functionModule.view</c> 才看得见模块树。
    /// </remarks>
    /// <returns>权限目录</returns>
    [HttpGet("catalogue")]
    public virtual async Task<ApiResult<IReadOnlyList<PermissionCatalogueModuleDto>>> GetCatalogue()
    {
        var result = await ModuleManagementService.GetPermissionCatalogueAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建功能
    /// </summary>
    /// <param name="request">功能信息</param>
    /// <returns>创建的功能</returns>
    [HttpPost]
    [ApiAuthorize(PermissionName = "authorization.permission.create")]
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
    [ApiAuthorize(PermissionName = "authorization.permission.update")]
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
    [ApiAuthorize(PermissionName = "authorization.permission.delete")]
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
    [ApiAuthorize(PermissionName = "authorization.permission.update")]
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
    [ApiAuthorize(PermissionName = "authorization.permission.update")]
    public virtual async Task<ApiResult> Disable(Guid id)
    {
        var result = await ModuleManagementService.DisableModuleFunctionAsync(id);
        return result.ToApiResult();
    }

}
