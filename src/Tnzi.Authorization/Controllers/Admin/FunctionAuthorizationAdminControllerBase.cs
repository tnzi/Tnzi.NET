namespace Tnzi.Authorization.Controllers.Admin;

/// <summary>
/// 功能授权控制器基类
/// 提供权限检查、获取用户权限列表、获取模块树等API端点，所有方法支持重写
/// </summary>
[Route("admin/function-authorization")]
public abstract class FunctionAuthorizationAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IFunctionAuthorizationService FunctionAuthorizationService;
    protected readonly IModuleManagementService ModuleManagementService;

    /// <summary>
    /// 初始化功能授权控制器基类
    /// </summary>
    /// <param name="functionAuthorizationService">功能授权服务</param>
    /// <param name="moduleManagementService">模块管理服务</param>
    protected FunctionAuthorizationAdminControllerBase(
        IFunctionAuthorizationService functionAuthorizationService,
        IModuleManagementService moduleManagementService)
    {
        FunctionAuthorizationService = Check.NotNull(functionAuthorizationService);
        ModuleManagementService = Check.NotNull(moduleManagementService);
    }

    /// <summary>
    /// 检查用户是否有权限访问指定功能
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="permissionName">权限名称</param>
    /// <returns>是否有权限</returns>
    [HttpGet("check")]
    public virtual async Task<ApiResult<bool>> CheckPermission([FromQuery] Guid userId, [FromQuery] string permissionName)
    {
        var result = await FunctionAuthorizationService.CheckPermissionWithResultAsync(userId, permissionName);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户的所有权限名称
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>权限名称集合</returns>
    [HttpGet("user/{userId:guid}/permissions")]
    public virtual async Task<ApiResult<IEnumerable<string>>> GetUserPermissionNames(Guid userId)
    {
        var result = await FunctionAuthorizationService.GetUserPermissionNamesWithResultAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取模块树
    /// </summary>
    /// <returns>模块树</returns>
    [HttpGet("modules/tree")]
    public virtual async Task<ApiResult<IEnumerable<FunctionModule>>> GetModuleTree()
    {
        var result = await ModuleManagementService.GetModuleTreeAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取模块的功能列表
    /// </summary>
    /// <param name="moduleId">模块ID</param>
    /// <returns>功能列表</returns>
    [HttpGet("modules/{moduleId:guid}/functions")]
    public virtual async Task<ApiResult<IEnumerable<ModuleFunction>>> GetModuleFunctions(Guid moduleId)
    {
        var result = await ModuleManagementService.GetModuleFunctionsAsync(moduleId);
        return result.ToApiResult();
    }
}
