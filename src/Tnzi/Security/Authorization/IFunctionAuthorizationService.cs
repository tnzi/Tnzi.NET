
namespace Tnzi.Security.Authorization;

/// <summary>
/// 功能授权服务接口
/// </summary>
public interface IFunctionAuthorizationService
{
    /// <summary>
    /// 检查用户是否有权限访问指定功能
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="permissionName">权限名称</param>
    /// <returns>是否有权限</returns>
    Task<bool> CheckPermissionAsync(Guid userId, string permissionName);

    /// <summary>
    /// 获取用户的所有权限名称
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>权限名称集合</returns>
    Task<IEnumerable<string>> GetUserPermissionNamesAsync(Guid userId);
}