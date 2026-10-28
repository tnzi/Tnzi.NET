namespace Tnzi.Authorization.Controllers.Admin;

/// <summary>
/// 功能授权控制器
/// 提供权限检查、获取用户权限列表、权限持有者反查等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/function-authorization")]
// 该控制器承载前端登录链路的自服务端点。豁免面刻意收窄到仅两个自服务端点：
// GetUserPermissionNames（当前用户权限名列表）与 GetAccessProfile（当前用户访问档案），
// 二者只随基类的认证边界开放（任何已登录用户可自查）——零授权用户也必须能查到
// “自己没有权限”，否则前端权限加载死锁（这正是旧 Admin.Manage 外层门的事故面：
// 角色权限被清空后连自查端点都 403）。
// 其余端点（权限检查 / 持有者反查 / 统计）均属管理读操作，
// 逐一叠加方法级 authorization.permission.view，不再随认证边界泛化开放。
// 模块树与模块功能列表不在这里：它们属于 admin/modules（DefaultModuleAdminController），
// 由 authorization.functionModule.view 把守。
public class DefaultFunctionAuthorizationAdminController : ApiAdminControllerBase
{
    protected readonly IFunctionAuthorizationService FunctionAuthorizationService;

    /// <summary>
    /// 初始化功能授权控制器
    /// </summary>
    /// <param name="functionAuthorizationService">功能授权服务</param>
    public DefaultFunctionAuthorizationAdminController(IFunctionAuthorizationService functionAuthorizationService)
    {
        FunctionAuthorizationService = Check.NotNull(functionAuthorizationService);
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
            return Forbidden<IEnumerable<string>>(
                "You may only read your own permissions.", ErrorCodes.FORBIDDEN);
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
            return Unauthorized<AccessProfileDto>("User is not authenticated", ErrorCodes.UNAUTHORIZED);
        }

        var result = await FunctionAuthorizationService.GetAccessProfileAsync(userId.Value);
        return result.ToApiResult();
    }

    /// <summary>
    /// 谁持有这个权限码：授予它的角色（含超管角色、行停用与休眠授权）、最终持有它的账号及其路径
    /// （超管 / 角色 / 直授）、被单独否定的账号。名单与运行时检查逐人一致，收窄到调用者的租户范围。
    /// </summary>
    /// <param name="permissionName">权限码（大小写不敏感）</param>
    /// <returns>持有者全景</returns>
    [HttpGet("permission/holders")]
    [ApiAuthorize(PermissionName = "authorization.permission.view")]
    public virtual async Task<ApiResult<PermissionHoldersDto>> GetPermissionHolders([FromQuery] string permissionName)
    {
        var result = await FunctionAuthorizationService.GetPermissionHoldersAsync(permissionName);
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
