
namespace Tnzi.Authorization.Services;

/// <summary>
/// 功能权限缓存服务
/// </summary>
public class FunctionAuthCache
{
    private readonly ICache _cache;
    private static readonly TimeSpan DefaultCacheExpiration = TimeSpan.FromMinutes(30);
    /// <summary>
    /// SuperAdmin 全功能目录是 "all enabled ModuleFunction.Code" - 它只随
    /// `ModuleFunction.IsEnabled` / `FunctionModule.IsEnabled` 变化，跟用户
    /// 无关。给它一个独立的、更长的 TTL，避免每次 super-admin 操作（如菜单
    /// 渲染、UI 列出全权限）都做一次全表扫描。
    /// </summary>
    private static readonly TimeSpan SuperAdminCatalogueExpiration = TimeSpan.FromHours(1);

    /// <summary>
    /// 缓存键前缀常量
    /// </summary>
    public const string UserFunctionsCachePrefix = "UserFunctions:";

    /// <summary>
    /// SuperAdmin 全功能目录缓存键（单例 key，跨所有 super-admin 共享）。
    /// </summary>
    private const string SuperAdminCatalogueKey = "Permissions:SuperAdminCatalogue";

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

    // NOTE: this cache intentionally exposes NO permission-check helpers.
    // The per-user key stores EXPLICIT grants only - super-admin-unaware.
    // Any check built directly on it would return false negatives for super
    // admins (their bypass lives in FunctionAuthorizationService). All
    // permission checks must go through FunctionAuthorizationService.
    // (Former CheckPermissionAsync/CheckPermissionsAsync helpers were removed
    // for exactly this reason - zero consumers, misleading semantics.)

    /// <summary>
    /// 清除所有功能权限缓存
    /// 使用 ICache 的前缀删除功能
    /// </summary>
    /// <returns>任务</returns>
    public async Task ClearAllAsync()
    {
        await _cache.RemoveByPrefixAsync(UserFunctionsCachePrefix);
        // The super-admin catalogue lives outside the UserFunctions prefix so
        // the bulk-prefix remove can't catch it - clear explicitly.
        await _cache.RemoveAsync(SuperAdminCatalogueKey);
    }

    /// <summary>
    /// Get the cached super-admin catalogue (full list of enabled
    /// ModuleFunction codes). Returns <c>null</c> on cache miss so the
    /// caller can re-query the DB and call <see cref="SetSuperAdminCatalogueAsync"/>
    /// to repopulate.
    /// </summary>
    public Task<IReadOnlyList<string>?> GetSuperAdminCatalogueAsync()
        => _cache.GetAsync<IReadOnlyList<string>>(SuperAdminCatalogueKey);

    /// <summary>
    /// Store the super-admin catalogue with a 1-hour TTL. Should be called
    /// after a cache-miss DB query; subsequent super-admin permission
    /// enumerations hit cache until either TTL expires or
    /// <see cref="InvalidateSuperAdminCatalogueAsync"/> fires.
    /// </summary>
    public Task SetSuperAdminCatalogueAsync(IReadOnlyList<string> codes)
        => _cache.SetAsync(SuperAdminCatalogueKey, codes, SuperAdminCatalogueExpiration);

    /// <summary>
    /// Invalidate the super-admin catalogue. Triggered when any
    /// ModuleFunction or FunctionModule changes its <c>IsEnabled</c> state
    /// (because those events change which codes belong in the catalogue).
    /// Regular user-permission caches are unaffected.
    /// </summary>
    public Task InvalidateSuperAdminCatalogueAsync()
        => _cache.RemoveAsync(SuperAdminCatalogueKey);

}

