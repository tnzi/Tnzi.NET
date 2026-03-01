
namespace Tnzi.Authorization.Services;

/// <summary>
/// 功能权限缓存服务
/// </summary>
public class FunctionAuthCache
{
    private readonly ICache _cache;
    private static readonly TimeSpan DefaultCacheExpiration = TimeSpan.FromMinutes(30);
    
    /// <summary>
    /// 缓存键前缀常量
    /// </summary>
    public const string UserFunctionsCachePrefix = "UserFunctions:";

    /// <summary>
    /// 初始化一个<see cref="FunctionAuthCache"/>类型的新实例
    /// </summary>
    /// <param name="cache">缓存服务</param>
    public FunctionAuthCache(ICache cache)
    {
        _cache = Check.NotNull(cache);
    }

    /// <summary>
    /// 获取用户的权限名称集合
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>权限名称集合</returns>
    public async Task<IEnumerable<string>?> GetUserPermissionNamesAsync(Guid userId)
    {
        string cacheKey = CacheKeys.Authorization.UserFunctions(userId);
        return await _cache.GetAsync<IEnumerable<string>>(cacheKey);
    }

    /// <summary>
    /// 设置用户的权限名称集合
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="permissionNames">权限名称集合</param>
    /// <returns>任务</returns>
    public async Task SetUserPermissionNamesAsync(Guid userId, IEnumerable<string> permissionNames)
    {
        string cacheKey = CacheKeys.Authorization.UserFunctions(userId);
        await _cache.SetAsync(cacheKey, permissionNames, DefaultCacheExpiration);
    }

    /// <summary>
    /// 移除用户的权限缓存
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>任务</returns>
    public async Task RemoveUserPermissionNamesAsync(Guid userId)
    {
        string cacheKey = CacheKeys.Authorization.UserFunctions(userId);
        await _cache.RemoveAsync(cacheKey);
    }

    /// <summary>
    /// 批量移除多个用户的权限缓存
    /// 当角色权限变更时调用此方法
    /// </summary>
    /// <param name="userIds">用户ID集合</param>
    /// <returns>任务</returns>
    public async Task RemoveUserPermissionNamesAsync(IEnumerable<Guid> userIds)
    {
        var tasks = userIds.Select(id => RemoveUserPermissionNamesAsync(id));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 检查用户是否有指定权限（带缓存）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="permissionName">权限名称</param>
    /// <returns>是否有权限</returns>
    public async Task<bool> CheckPermissionAsync(Guid userId, string permissionName)
    {
        // 先从缓存获取用户的权限列表
        var userPermissions = await GetUserPermissionNamesAsync(userId);
        if (userPermissions != null)
        {
            return userPermissions.Contains(permissionName);
        }

        // 缓存未命中，返回false表示需要从数据库查询
        return false;
    }

    /// <summary>
    /// 批量检查用户是否有指定权限（带缓存）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="permissionNames">权限名称集合</param>
    /// <returns>权限检查结果字典</returns>
    public async Task<Dictionary<string, bool>> CheckPermissionsAsync(Guid userId, IEnumerable<string> permissionNames)
    {
        var userPermissions = await GetUserPermissionNamesAsync(userId);
        var userPermissionSet = userPermissions != null 
            ? new HashSet<string>(userPermissions) 
            : new HashSet<string>();
        
        return permissionNames.ToDictionary(
            p => p,
            p => userPermissionSet.Contains(p)
        );
    }

    /// <summary>
    /// 清除所有功能权限缓存
    /// 使用 ICache 的前缀删除功能
    /// </summary>
    /// <returns>任务</returns>
    public async Task ClearAllAsync()
    {
        await _cache.RemoveByPrefixAsync(UserFunctionsCachePrefix);
    }
}

