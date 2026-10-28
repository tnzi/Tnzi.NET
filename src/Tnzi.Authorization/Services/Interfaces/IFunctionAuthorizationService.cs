namespace Tnzi.Authorization.Services;

/// <summary>
/// 功能授权服务接口（权限检查）
/// 继承框架层接口，扩展带 Result 返回的权限检查方法
/// </summary>
public interface IFunctionAuthorizationService : Tnzi.Security.Authorization.IFunctionAuthorizationService
{
    /// <summary>
    /// 批量检查用户是否有多个权限
    /// 一次性获取用户权限并检查，比多次调用 CheckPermissionAsync 性能更好
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="permissionNames">权限名称列表</param>
    /// <returns>权限检查结果字典</returns>
    Task<Dictionary<string, bool>> CheckPermissionsAsync(Guid userId, IEnumerable<string> permissionNames);

    /// <summary>
    /// 检查用户是否有权限访问指定功能（返回 Result，用于 Controller）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="permissionName">权限名称</param>
    /// <returns>是否有权限</returns>
    Task<Result<bool>> CheckPermissionWithResultAsync(Guid userId, string permissionName);

    /// <summary>
    /// 获取用户的所有权限名称（返回 Result，用于 Controller）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>权限名称集合</returns>
    Task<Result<IEnumerable<string>>> GetUserPermissionNamesWithResultAsync(Guid userId);

    /// <summary>
    /// 谁持有这个码：授予它的角色（含超管角色、行停用与休眠授权）、最终持有它的账号及其路径
    /// （超管 / 角色 / 直授）、被单独否定的账号。持有者名单与 <c>CheckPermissionAsync</c> 逐人一致，
    /// 收窄到调用者的租户范围。码不存在答 404；Identity 的角色成员关系或角色表不可用答 501。
    /// </summary>
    /// <param name="permissionName">权限码（大小写不敏感，与运行时检查同口径）</param>
    Task<Result<PermissionHoldersDto>> GetPermissionHoldersAsync(string permissionName);

    /// <summary>
    /// Get authorization statistics overview
    /// </summary>
    /// <returns>Authorization statistics</returns>
    Task<Result<AuthorizationStatisticsDto>> GetStatisticsAsync();

    /// <summary>
    /// Resolve the user's access profile: effective permission codes plus the
    /// backend-authoritative super-admin flag (single self-service payload for
    /// the admin front-end).
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<Result<AccessProfileDto>> GetAccessProfileAsync(Guid userId);
}
