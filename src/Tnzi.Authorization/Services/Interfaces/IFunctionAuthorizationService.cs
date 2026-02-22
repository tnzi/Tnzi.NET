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
}
