namespace Tnzi.Authorization.Services;

/// <summary>
/// 角色-功能分配服务接口
/// 提供角色与功能的关联管理操作
/// </summary>
public interface IRoleFunctionService
{
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
}
