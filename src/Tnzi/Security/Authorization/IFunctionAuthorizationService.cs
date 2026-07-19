
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
    /// 返回所有超级管理员用户的 Id 集合（<c>SuperAdminRoles</c> 全部角色成员的并集）。
    /// 与 <see cref="IsSuperAdminAsync"/> 对称：后者回答"某一个用户是否超管"（反向单查），
    /// 本方法正向列出全体超管用户，供调用方一次性把超管从<b>面向业务用户</b>的名单
    /// （IM 通讯录、群成员候选、全员通知接收人等）中剔除——超管是系统维护/运维账号，
    /// 按约定不参与业务，不应作为业务联系人对普通用户可见。
    /// 默认实现返回空集：未启用超管概念（如未加载 Authorization 模块）的实现不隐藏任何人。
    /// </summary>
    Task<IReadOnlySet<Guid>> GetSuperAdminUserIdsAsync() =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

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