

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
    /// 批量获取多个用户的角色 Id 集合（与 <see cref="GetUserRolesAsync"/> 同形，只是返回 Id 而不是名称）。
    /// 供授权模块一次解析 N 个用户的授予，避免逐用户往返。默认实现逐个回落到
    /// <see cref="GetUserRoleIdsAsync(Guid)"/>（不打断既有实现）；真实实现应改写成一条 IN 查询。
    /// 没有任何角色的用户不出现在字典里。
    /// </summary>
    /// <param name="userIds">用户ID集合</param>
    /// <returns>用户ID与角色ID集合的字典</returns>
    async Task<IDictionary<Guid, IEnumerable<Guid>>> GetUserRoleIdsAsync(IEnumerable<Guid> userIds)
    {
        var result = new Dictionary<Guid, IEnumerable<Guid>>();
        foreach (var userId in userIds.Distinct())
        {
            var roleIds = (await GetUserRoleIdsAsync(userId)).ToList();
            if (roleIds.Count > 0) result[userId] = roleIds;
        }

        return result;
    }

    /// <summary>
    /// 获取角色的用户ID集合
    /// 用于权限缓存失效时批量清除用户缓存
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>用户ID集合</returns>
    Task<IEnumerable<Guid>> GetRoleUserIdsAsync(Guid roleId);
}

