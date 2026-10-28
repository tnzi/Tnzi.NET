namespace Tnzi.Authorization.Services;

/// <summary>
/// 每用户权限缓存的分区：这份权限集是在哪一个租户过滤器之下算出来的。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 同一个用户在不同租户上下文里算出来的是<b>两份不同的集合</b>：用户-角色关联不按租户过滤，
/// 而角色授权与用户直授（<see cref="RoleFunction"/> / <see cref="UserFunction"/>）按调用者的租户严格等值过滤。
/// 缓存键此前只有用户 id，于是租户 A 的管理员替租户 B 的用户算一次（查他的权限、给他直授），
/// 算出来的「A 视角下的集合」就落进 B 本人请求读的那个键，最长 30 分钟：
/// 能把 A 里才有的码带进 B、把 B 本来的授权清空，键是热的时候还能把 B 的完整权限集读给 A。
/// </para>
/// <para>
/// 分区跟着数据库过滤器走，判据逐项与 <c>TnziDbContext</c> 相同：多租户开关、租户过滤器是否开着、
/// 当前租户（<see cref="ICurrentTenant"/> 优先，其次 <see cref="ICurrentUser.TenantId"/>）。
/// 多租户未开启只有一个分区，与此前逐字相同。租户过滤器被显式关掉时（跨租户的维护代码）
/// 算出来的是所有租户的并集，那样的结果<b>不进也不读</b>缓存，返回 <c>null</c>。
/// </para>
/// </remarks>
internal static class PermissionCachePartition
{
    /// <summary>多租户未开启：全部请求共用一个分区。</summary>
    public const string Single = "*";

    /// <summary>多租户开启、但当前没有租户上下文（宿主 / 全局管理员）。</summary>
    public const string Host = "host";

    /// <summary>
    /// 当前请求的分区；<c>null</c> 表示这次计算不该经过缓存。
    /// </summary>
    public static string? Resolve(bool multiTenancyEnabled, IDataFilterManager? dataFilters, ICurrentTenant? currentTenant, ICurrentUser? currentUser)
    {
        if (!multiTenancyEnabled)
        {
            return Single;
        }

        if (dataFilters != null && !dataFilters.IsEnabled<IMultiTenantFilter>())
        {
            return null;
        }

        var tenantId = currentTenant?.Id ?? currentUser?.TenantId;
        return tenantId.HasValue ? $"t:{tenantId.Value:N}" : Host;
    }
}
