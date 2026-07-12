namespace Tnzi.Authorization.Controllers.Admin;

/// <summary>
/// 功能授权控制器
/// 提供权限检查、获取用户权限列表、获取模块树等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/function-authorization")]
// 该控制器承载前端登录链路的自服务端点。豁免面刻意收窄到仅两个自服务端点：
// GetUserPermissionNames（当前用户权限名列表）与 GetAccessProfile（当前用户访问档案），
// 二者只随基类的认证边界开放（任何已登录用户可自查）——零授权用户也必须能查到
// “自己没有权限”，否则前端权限加载死锁（这正是旧 Admin.Manage 外层门的事故面：
// 角色权限被清空后连自查端点都 403）。
// 其余端点（权限检查 / 模块树 / 模块功能 / 反查角色用户 / 统计）均属管理读操作，
// 逐一叠加方法级 authorization.permission.view，不再随认证边界泛化开放。
public class DefaultFunctionAuthorizationAdminController : ApiAdminControllerBase
{
    protected readonly IFunctionAuthorizationService FunctionAuthorizationService;
    protected readonly IModuleManagementService ModuleManagementService;

    /// <summary>
    /// 初始化功能授权控制器
    /// </summary>
    /// <param name="functionAuthorizationService">功能授权服务</param>
    /// <param name="moduleManagementService">模块管理服务</param>
    public DefaultFunctionAuthorizationAdminController(
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
    [ApiAuthorize(PermissionName = "authorization.permission.view")]
    public virtual async Task<ApiResult<bool>> CheckPermission([FromQuery] Guid userId, [FromQuery] string permissionName)
    {
        var result = await FunctionAuthorizationService.CheckPermissionWithResultAsync(userId, permissionName);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户的所有权限名称。自读(userId=当前用户)只需已认证(登录链路);
    /// **跨用户读**会暴露他人的完整有效权限集,故须额外持有
    /// authorization.permission.view(目录读码),否则任意已登录用户都能
    /// 探测超管/他人的权限拓扑(IDOR)。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>权限名称集合</returns>
    [HttpGet("user/{userId:guid}/permissions")]
    public virtual async Task<ApiResult<IEnumerable<string>>> GetUserPermissionNames(Guid userId)
    {
        var currentUserId = CurrentUser?.Id;
        if (userId != currentUserId
            && !(currentUserId is Guid readerId
                 && await FunctionAuthorizationService.CheckPermissionAsync(readerId, "authorization.permission.view")))
        {
            return Result.Failure<IEnumerable<string>>(
                "You may only read your own permissions.", 403, ErrorCodes.FORBIDDEN).ToApiResult();
        }

        var result = await FunctionAuthorizationService.GetUserPermissionNamesWithResultAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 当前用户的访问档案：有效权限码列表 + 后端权威的超管标志。
    /// 前端登录链路的自服务端点——isSuperAdmin 由后端下发,前端无需再镜像
    /// SuperAdminRoles 配置自行推断。
    /// </summary>
    [HttpGet("access-profile")]
    public virtual async Task<ApiResult<AccessProfileDto>> GetAccessProfile()
    {
        var userId = CurrentUser?.Id;
        if (userId == null || userId == Guid.Empty)
        {
            return Result.Failure<AccessProfileDto>("User is not authenticated", 401, ErrorCodes.UNAUTHORIZED).ToApiResult();
        }

        var result = await FunctionAuthorizationService.GetAccessProfileAsync(userId.Value);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取模块树
    /// </summary>
    /// <returns>模块树</returns>
    [HttpGet("modules/tree")]
    [ApiAuthorize(PermissionName = "authorization.permission.view")]
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
    [ApiAuthorize(PermissionName = "authorization.permission.view")]
    public virtual async Task<ApiResult<IEnumerable<ModuleFunction>>> GetModuleFunctions(Guid moduleId)
    {
        var result = await ModuleManagementService.GetModuleFunctionsAsync(moduleId);
        return result.ToApiResult();
    }

    /// <summary>
    /// Reverse permission query: get all roles that have a specific permission
    /// </summary>
    /// <param name="permissionName">Permission name (function code)</param>
    /// <returns>List of roles with the permission</returns>
    [HttpGet("permission/roles")]
    [ApiAuthorize(PermissionName = "authorization.permission.view")]
    public virtual async Task<ApiResult<IEnumerable<PermissionRoleDto>>> GetPermissionRoles([FromQuery] string permissionName)
    {
        var result = await FunctionAuthorizationService.GetPermissionRolesAsync(permissionName);
        return result.ToApiResult();
    }

    /// <summary>
    /// Reverse permission query: get all users that have a specific permission
    /// </summary>
    /// <param name="permissionName">Permission name (function code)</param>
    /// <returns>List of users with the permission</returns>
    [HttpGet("permission/users")]
    [ApiAuthorize(PermissionName = "authorization.permission.view")]
    public virtual async Task<ApiResult<IEnumerable<PermissionUserDto>>> GetPermissionUsers([FromQuery] string permissionName)
    {
        var result = await FunctionAuthorizationService.GetPermissionUsersAsync(permissionName);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get authorization statistics overview
    /// </summary>
    /// <returns>Authorization statistics</returns>
    [HttpGet("statistics")]
    [ApiAuthorize(PermissionName = "authorization.permission.view")]
    public virtual async Task<ApiResult<AuthorizationStatisticsDto>> GetStatistics()
    {
        var result = await FunctionAuthorizationService.GetStatisticsAsync();
        return result.ToApiResult();
    }
}
