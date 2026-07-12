namespace Tnzi.Authorization.Services;

/// <summary>
/// 角色-功能分配服务接口
/// 提供角色与功能的关联管理操作
/// </summary>
public interface IRoleFunctionService
{
    /// <summary>
    /// Paged list of role-function assignments across ALL roles. Supports
    /// filtering by role / function / enabled state via <see cref="RoleFunctionQueryDto"/>.
    /// Unlike <see cref="GetRoleFunctionsAsync"/> which is scoped to one role,
    /// this is the canonical admin-side query used by the RoleFunction page.
    /// </summary>
    Task<Result<IPagedList<RoleFunctionDto>>> GetRoleFunctionsPagedAsync(RoleFunctionQueryDto query);

    /// <summary>
    /// 获取角色的功能列表
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>功能列表</returns>
    Task<Result<IEnumerable<ModuleFunction>>> GetRoleFunctionsAsync(Guid roleId);

    /// <summary>
    /// 获取角色的功能ID列表
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>功能ID列表</returns>
    Task<Result<IEnumerable<Guid>>> GetRoleFunctionIdsAsync(Guid roleId);

    /// <summary>
    /// 分配功能到角色
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="functionIds">功能ID列表</param>
    Task<Result> AssignFunctionsToRoleAsync(Guid roleId, IEnumerable<Guid> functionIds);

    /// <summary>
    /// 从角色移除功能
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="functionIds">功能ID列表</param>
    Task<Result> RemoveFunctionsFromRoleAsync(Guid roleId, IEnumerable<Guid> functionIds);

    /// <summary>
    /// 设置角色的功能（覆盖原有功能）
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="functionIds">功能ID列表</param>
    Task<Result> SetRoleFunctionsAsync(Guid roleId, IEnumerable<Guid> functionIds);

    /// <summary>
    /// 检查角色是否有指定功能
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="functionId">功能ID</param>
    /// <returns>是否有权限</returns>
    Task<Result<bool>> RoleHasFunctionAsync(Guid roleId, Guid functionId);

    /// <summary>
    /// 获取功能的角色列表
    /// </summary>
    /// <param name="functionId">功能ID</param>
    /// <returns>角色功能列表</returns>
    Task<Result<IEnumerable<RoleFunction>>> GetFunctionRolesAsync(Guid functionId);

    /// <summary>
    /// 批量分配功能到多个角色
    /// </summary>
    /// <param name="roleIds">角色ID列表</param>
    /// <param name="functionIds">功能ID列表</param>
    Task<Result> BatchAssignFunctionsAsync(IEnumerable<Guid> roleIds, IEnumerable<Guid> functionIds);

    /// <summary>
    /// 清空角色的所有功能
    /// </summary>
    /// <param name="roleId">角色ID</param>
    Task<Result> ClearRoleFunctionsAsync(Guid roleId);

    /// <summary>
    /// Compare permissions between two roles
    /// Returns functions unique to each role and shared functions
    /// </summary>
    /// <param name="roleId1">First role ID</param>
    /// <param name="roleId2">Second role ID</param>
    /// <returns>Permission comparison result</returns>
    Task<Result<PermissionComparisonDto>> CompareRolePermissionsAsync(Guid roleId1, Guid roleId2);

    /// <summary>
    /// Clone all function assignments from source role to target role
    /// Existing assignments on the target role are preserved (additive clone)
    /// </summary>
    /// <param name="sourceRoleId">Source role ID</param>
    /// <param name="targetRoleId">Target role ID</param>
    /// <returns>Number of new assignments created</returns>
    Task<Result<int>> CloneRoleFunctionsAsync(Guid sourceRoleId, Guid targetRoleId);

    /// <summary>
    /// Export role's function assignments as JSON (using function codes for portability)
    /// </summary>
    /// <param name="roleId">Role ID</param>
    /// <returns>JSON string of exported permission data</returns>
    Task<Result<RolePermissionExportDto>> ExportRolePermissionsAsync(Guid roleId);

    /// <summary>
    /// Import function assignments to a role from exported JSON data
    /// Uses function codes to resolve function IDs (environment-independent)
    /// </summary>
    /// <param name="roleId">Target role ID</param>
    /// <param name="importData">Exported permission data</param>
    /// <returns>Import result with counts</returns>
    Task<Result<PermissionImportResultDto>> ImportRolePermissionsAsync(Guid roleId, RolePermissionExportDto importData);

    /// <summary>
    /// The role names configured as super administrators
    /// (<c>Authorization:SuperAdminRoles</c>). Members of these roles bypass
    /// every permission check, so assignment UIs must render them read-only:
    /// explicit RoleFunction rows have no effect on their members. Default
    /// interface method so existing implementations keep compiling.
    /// </summary>
    IReadOnlyList<string> GetSuperAdminRoleNames() => [];
}
