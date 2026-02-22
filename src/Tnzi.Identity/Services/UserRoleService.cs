
namespace Tnzi.Identity.Services;

/// <summary>
/// 用户角色服务实现（用于授权模块获取用户角色）
/// </summary>
public class UserRoleService : ApplicationService, IUserRoleService
{
    private readonly DbContext _dbContext;

    /// <summary>
    /// 初始化一个<see cref="UserRoleService"/>类型的新实例
    /// </summary>
    public UserRoleService(DbContext dbContext,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _dbContext = Check.NotNull(dbContext);
    }

    /// <summary>
    /// 获取用户的角色ID集合
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>角色ID集合</returns>
    public async Task<IEnumerable<Guid>> GetUserRoleIdsAsync(Guid userId)
    {
        // 从IdentityUserRole表获取用户的角色
        var userRoles = await _dbContext.Set<UserRole>()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        return userRoles;
    }

    /// <summary>
    /// 获取多个用户的角色名称集合
    /// </summary>
    /// <param name="userIds">用户ID集合</param>
    /// <returns>用户ID与角色名称集合的字典</returns>
    public async Task<IDictionary<Guid, IEnumerable<string>>> GetUserRolesAsync(IEnumerable<Guid> userIds)
    {
        var idList = userIds.ToList();
        if (!idList.Any())
        {
            return new Dictionary<Guid, IEnumerable<string>>();
        }

        var userRoles = await _dbContext.Set<UserRole>()
            .Where(ur => idList.Contains(ur.UserId))
            .Join(_dbContext.Set<Role>(), ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync();

        return userRoles.GroupBy(ur => ur.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).AsEnumerable());
    }

    /// <summary>
    /// 获取角色的用户ID集合
    /// 用于权限缓存失效时批量清除用户缓存
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>用户ID集合</returns>
    public async Task<IEnumerable<Guid>> GetRoleUserIdsAsync(Guid roleId)
    {
        // 从IdentityUserRole表获取角色的所有用户
        var userIds = await _dbContext.Set<UserRole>()
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .ToListAsync();

        return userIds;
    }
}
