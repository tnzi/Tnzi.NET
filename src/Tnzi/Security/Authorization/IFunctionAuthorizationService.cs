
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

    /// <summary>
    /// 用户是否为超级管理员（绕过一切权限检查、可支配全部角色）。
    /// 默认实现返回 false：没有超管概念的实现把所有用户当普通用户。
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<bool> IsSuperAdminAsync(Guid userId) => Task.FromResult(false);

    /// <summary>
    /// <paramref name="grantorUserId"/> 是否可支配（管理）指定角色：为其授予/回收权限、
    /// 变更其用户成员。委托规则（权限集包含模型）：超管支配一切角色；其余用户仅能支配
    /// "显式权限集是自己有效权限集子集"的角色，且永远不能支配超管配置角色。
    /// 默认实现返回 true：没有委托语义的实现保持宽松（护栏由调用方按可空服务跳过）。
    /// </summary>
    /// <param name="grantorUserId">授权者用户ID</param>
    /// <param name="roleId">目标角色ID</param>
    Task<bool> CanManageRoleAsync(Guid grantorUserId, Guid roleId) => Task.FromResult(true);
}