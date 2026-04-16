namespace Tnzi.Authorization.Controllers.Admin;

/// <summary>
/// 角色功能管理控制器
/// 提供角色功能分配、查询等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/role-functions")]
public class DefaultRoleFunctionAdminController : ApiAdminControllerBase
{
    protected readonly IRoleFunctionService RoleFunctionService;

    /// <summary>
    /// 初始化角色功能管理控制器
    /// </summary>
    /// <param name="roleFunctionService">角色功能服务</param>
    public DefaultRoleFunctionAdminController(IRoleFunctionService roleFunctionService)
    {
        RoleFunctionService = Check.NotNull(roleFunctionService);
    }

    /// <summary>
    /// Canonical paged list of role-function assignments across all roles.
    /// Used by the RoleFunction admin page (TCrudPage). Filters by role /
    /// function / enabled state via query string.
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<RoleFunctionDto>>> GetList([FromQuery] RoleFunctionQueryDto query)
    {
        var result = await RoleFunctionService.GetRoleFunctionsPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取角色的功能列表
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>功能列表</returns>
    [HttpGet("role/{roleId:guid}")]
    public virtual async Task<ApiResult<IEnumerable<ModuleFunction>>> GetRoleFunctions(Guid roleId)
    {
        var result = await RoleFunctionService.GetRoleFunctionsAsync(roleId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取角色的功能ID列表
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>功能ID列表</returns>
    [HttpGet("role/{roleId:guid}/function-ids")]
    public virtual async Task<ApiResult<IEnumerable<Guid>>> GetRoleFunctionIds(Guid roleId)
    {
        var result = await RoleFunctionService.GetRoleFunctionIdsAsync(roleId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 分配功能到角色
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="request">分配请求</param>
    /// <returns>操作结果</returns>
    [HttpPost("role/{roleId:guid}/assign")]
    public virtual async Task<ApiResult> AssignFunctionsToRole(Guid roleId, [FromBody] AssignFunctionsRequest request)
    {
        var result = await RoleFunctionService.AssignFunctionsToRoleAsync(roleId, request.FunctionIds);
        return result.ToApiResult();
    }

    /// <summary>
    /// 从角色移除功能
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="request">移除请求</param>
    /// <returns>操作结果</returns>
    [HttpPost("role/{roleId:guid}/remove")]
    public virtual async Task<ApiResult> RemoveFunctionsFromRole(Guid roleId, [FromBody] RemoveFunctionsRequest request)
    {
        var result = await RoleFunctionService.RemoveFunctionsFromRoleAsync(roleId, request.FunctionIds);
        return result.ToApiResult();
    }

    /// <summary>
    /// 设置角色的功能（覆盖原有功能）
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="request">设置请求</param>
    /// <returns>操作结果</returns>
    [HttpPut("role/{roleId:guid}/set")]
    public virtual async Task<ApiResult> SetRoleFunctions(Guid roleId, [FromBody] SetRoleFunctionsRequest request)
    {
        var result = await RoleFunctionService.SetRoleFunctionsAsync(roleId, request.FunctionIds);
        return result.ToApiResult();
    }

    /// <summary>
    /// 检查角色是否有指定功能
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="functionId">功能ID</param>
    /// <returns>是否有权限</returns>
    [HttpGet("role/{roleId:guid}/has-function/{functionId:guid}")]
    public virtual async Task<ApiResult<bool>> HasFunction(Guid roleId, Guid functionId)
    {
        var result = await RoleFunctionService.RoleHasFunctionAsync(roleId, functionId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取功能的角色列表
    /// </summary>
    /// <param name="functionId">功能ID</param>
    /// <returns>角色列表</returns>
    [HttpGet("function/{functionId:guid}/roles")]
    public virtual async Task<ApiResult<IEnumerable<RoleFunction>>> GetFunctionRoles(Guid functionId)
    {
        var result = await RoleFunctionService.GetFunctionRolesAsync(functionId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量分配功能到多个角色
    /// </summary>
    /// <param name="request">批量分配请求</param>
    /// <returns>操作结果</returns>
    [HttpPost("batch/assign")]
    public virtual async Task<ApiResult> BatchAssignFunctions([FromBody] BatchAssignFunctionsRequest request)
    {
        var result = await RoleFunctionService.BatchAssignFunctionsAsync(request.RoleIds, request.FunctionIds);
        return result.ToApiResult();
    }

    /// <summary>
    /// 清空角色的所有功能
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>操作结果</returns>
    [HttpDelete("role/{roleId:guid}/clear")]
    public virtual async Task<ApiResult> ClearRoleFunctions(Guid roleId)
    {
        var result = await RoleFunctionService.ClearRoleFunctionsAsync(roleId);
        return result.ToApiResult();
    }

    /// <summary>
    /// Compare permissions between two roles
    /// </summary>
    [HttpGet("compare")]
    public virtual async Task<ApiResult<PermissionComparisonDto>> CompareRolePermissions([FromQuery] Guid roleId1, [FromQuery] Guid roleId2)
    {
        var result = await RoleFunctionService.CompareRolePermissionsAsync(roleId1, roleId2);
        return result.ToApiResult();
    }

    /// <summary>
    /// Clone permissions from source role to target role
    /// </summary>
    [HttpPost("role/{roleId:guid}/clone")]
    public virtual async Task<ApiResult<int>> CloneRolePermissions(Guid roleId, [FromBody] CloneRolePermissionsRequest request)
    {
        var result = await RoleFunctionService.CloneRoleFunctionsAsync(request.SourceRoleId, roleId);
        return result.ToApiResult();
    }

    /// <summary>
    /// Export role's permissions as JSON
    /// </summary>
    [HttpGet("role/{roleId:guid}/export")]
    public virtual async Task<ApiResult<RolePermissionExportDto>> ExportRolePermissions(Guid roleId)
    {
        var result = await RoleFunctionService.ExportRolePermissionsAsync(roleId);
        return result.ToApiResult();
    }

    /// <summary>
    /// Import permissions to a role from exported data
    /// </summary>
    [HttpPost("role/{roleId:guid}/import")]
    public virtual async Task<ApiResult<PermissionImportResultDto>> ImportRolePermissions(Guid roleId, [FromBody] RolePermissionExportDto importData)
    {
        var result = await RoleFunctionService.ImportRolePermissionsAsync(roleId, importData);
        return result.ToApiResult();
    }

}
