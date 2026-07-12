namespace Tnzi.Authorization.Controllers.Admin;

/// <summary>
/// 用户功能直授管理控制器
/// 提供不经角色、直接把功能授予单个用户的API端点，所有方法支持重写
/// </summary>
/// <remarks>
/// 与角色授权（admin/role-functions）互补：这里管理的是"给单个用户单独
/// 多开权限"的直授行与"唯独该用户不行"的否定行（deny，用户级优先，
/// 从角色授权中扣除）；服务层的委托护栏保证非超管授权者只能授出/拒绝
/// 自己持有的权限码。
/// </remarks>
[DefaultController]
[Route("admin/user-functions")]
[ApiAuthorize(PermissionName = "authorization.userFunction.view")]
public class DefaultUserFunctionAdminController : ApiAdminControllerBase
{
    protected readonly IUserFunctionService UserFunctionService;

    /// <summary>
    /// 初始化用户功能直授管理控制器
    /// </summary>
    /// <param name="userFunctionService">用户功能直授服务</param>
    public DefaultUserFunctionAdminController(IUserFunctionService userFunctionService)
    {
        UserFunctionService = Check.NotNull(userFunctionService);
    }

    /// <summary>
    /// 获取用户直接授权的功能列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>功能列表</returns>
    [HttpGet("user/{userId:guid}")]
    public virtual async Task<ApiResult<IEnumerable<ModuleFunction>>> GetUserFunctions(Guid userId)
    {
        var result = await UserFunctionService.GetUserFunctionsAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户直接授权的功能ID列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>功能ID列表</returns>
    [HttpGet("user/{userId:guid}/function-ids")]
    public virtual async Task<ApiResult<IEnumerable<Guid>>> GetUserFunctionIds(Guid userId)
    {
        var result = await UserFunctionService.GetUserFunctionIdsAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 直接授予功能给用户（增量）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="request">分配请求</param>
    /// <returns>操作结果</returns>
    [HttpPost("user/{userId:guid}/assign")]
    [ApiAuthorize(PermissionName = "authorization.userFunction.assign")]
    public virtual async Task<ApiResult> AssignFunctionsToUser(Guid userId, [FromBody] AssignFunctionsRequest request)
    {
        var result = await UserFunctionService.AssignFunctionsToUserAsync(userId, request.FunctionIds);
        return result.ToApiResult();
    }

    /// <summary>
    /// 移除用户的直接授权
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="request">移除请求</param>
    /// <returns>操作结果</returns>
    [HttpPost("user/{userId:guid}/remove")]
    [ApiAuthorize(PermissionName = "authorization.userFunction.assign")]
    public virtual async Task<ApiResult> RemoveFunctionsFromUser(Guid userId, [FromBody] RemoveFunctionsRequest request)
    {
        var result = await UserFunctionService.RemoveFunctionsFromUserAsync(userId, request.FunctionIds);
        return result.ToApiResult();
    }

    /// <summary>
    /// 设置用户的直接授权（覆盖原有直授集）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="request">设置请求</param>
    /// <returns>操作结果</returns>
    [HttpPut("user/{userId:guid}/set")]
    [ApiAuthorize(PermissionName = "authorization.userFunction.assign")]
    public virtual async Task<ApiResult> SetUserFunctions(Guid userId, [FromBody] SetUserFunctionsRequest request)
    {
        var result = await UserFunctionService.SetUserFunctionsAsync(userId, request.FunctionIds);
        return result.ToApiResult();
    }

    /// <summary>
    /// 清空用户的所有直接授权
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>操作结果</returns>
    [HttpDelete("user/{userId:guid}/clear")]
    [ApiAuthorize(PermissionName = "authorization.userFunction.assign")]
    public virtual async Task<ApiResult> ClearUserFunctions(Guid userId)
    {
        var result = await UserFunctionService.ClearUserFunctionsAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户被否定（deny）的功能ID列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>功能ID列表</returns>
    [HttpGet("user/{userId:guid}/denied-function-ids")]
    public virtual async Task<ApiResult<IEnumerable<Guid>>> GetUserDeniedFunctionIds(Guid userId)
    {
        var result = await UserFunctionService.GetUserDeniedFunctionIdsAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 设置用户的否定权限集（覆盖；传空列表即清空）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="request">设置请求</param>
    /// <returns>操作结果</returns>
    [HttpPut("user/{userId:guid}/set-denied")]
    [ApiAuthorize(PermissionName = "authorization.userFunction.assign")]
    public virtual async Task<ApiResult> SetUserDeniedFunctions(Guid userId, [FromBody] SetUserFunctionsRequest request)
    {
        var result = await UserFunctionService.SetUserDeniedFunctionsAsync(userId, request.FunctionIds);
        return result.ToApiResult();
    }
}
