
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
    /// 获取用户在指定分区下的权限名称集合；没有缓存、或缓存的是另一个分区算出来的集合时返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// ★ 一个用户只有一个键，值上带着它是在哪个分区（租户过滤器）之下算出来的。
    /// 分区放在值里而不是键里，是为了让所有失效路径保持「按用户删一个键」：
    /// 角色成员变化、直授变化、角色授权变化在哪个租户上下文里发生都删得到，不必知道这个人在哪些分区里留过缓存。
    /// 分区不同按未命中处理：那份集合是别的租户视角下的事实，读给这个分区就是越权或者漏权。
    /// </remarks>
    /// <param name="userId">用户ID</param>
    /// <param name="partition">当前请求的分区（见 <c>PermissionCachePartition</c>）</param>
    /// <returns>权限名称集合</returns>
    public async Task<IEnumerable<string>?> GetUserPermissionNamesAsync(Guid userId, string partition)
    {
        Check.NotNullOrWhiteSpace(partition);
        var entry = await _cache.GetAsync<UserPermissionEntry>(CacheKeys.Authorization.UserFunctions(userId));
        return entry != null && string.Equals(entry.Partition, partition, StringComparison.Ordinal)
            ? entry.Codes
            : null;
    }

    /// <summary>
    /// 设置用户在指定分区下的权限名称集合（覆盖这个用户此前缓存的任何分区）。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="partition">算出这份集合时的分区</param>
    /// <param name="permissionNames">权限名称集合</param>
    /// <returns>任务</returns>
    public async Task SetUserPermissionNamesAsync(Guid userId, string partition, IEnumerable<string> permissionNames)
    {
        Check.NotNullOrWhiteSpace(partition);
        Check.NotNull(permissionNames);
        var entry = new UserPermissionEntry { Partition = partition, Codes = permissionNames.ToList() };
        await _cache.SetAsync(CacheKeys.Authorization.UserFunctions(userId), entry, DefaultCacheExpiration);
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

    /// <summary>缓存值：一份权限集，连同它是在哪个分区之下算出来的。</summary>
    private sealed class UserPermissionEntry
    {
        public string Partition { get; init; } = string.Empty;

        public List<string> Codes { get; init; } = [];
    }
}

