

namespace Tnzi.Identity.Services;

/// <summary>
/// 用户角色服务接口（用于从Identity模块获取用户角色）
/// </summary>
public interface IUserRoleService
{
    /// <summary>
    /// 获取用户的角色ID集合
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>角色ID集合</returns>
    Task<IEnumerable<Guid>> GetUserRoleIdsAsync(Guid userId);

    /// <summary>
    /// 获取多个用户的角色名称集合
    /// </summary>
    /// <param name="userIds">用户ID集合</param>
    /// <returns>用户ID与角色名称集合的字典</returns>
    Task<IDictionary<Guid, IEnumerable<string>>> GetUserRolesAsync(IEnumerable<Guid> userIds);

    /// <summary>
    /// 获取角色的用户ID集合
    /// 用于权限缓存失效时批量清除用户缓存
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>用户ID集合</returns>
    Task<IEnumerable<Guid>> GetRoleUserIdsAsync(Guid roleId);
}

