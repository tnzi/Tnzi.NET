namespace Tnzi.EFCore.Internal;

/// <summary>
/// 「谁是主上下文」的唯一判定。模型里装哪些实体（<see cref="EntityRegistrationHelper"/>）、
/// 实体到上下文的反查（<see cref="EntityManager.GetDbContextTypeForEntity"/>）与模型缓存键都读这里。
/// </summary>
/// <remarks>
/// <para>
/// 实体配置的 <c>DbContextType</c> 为 null 表示「属于主上下文」，所以判定主上下文是建模的前提：
/// 主上下文 = 显式绑定它的实体 ∪ 未指定上下文的实体；次上下文只有显式绑定它的实体。
/// </para>
/// <para>
/// 主上下文：<c>AddTnziDbContext</c> 登记里唯一一个 <c>isPrimary</c> 的；一个都没标而只登记了一种上下文时就是它
/// （单上下文且未标 <c>isPrimary</c> 是既有用法）。没有登记、标了多个、或登记了多个却都没标时判定不了，返回 null，
/// 调用方按旧规则处理（有显式绑定就只用显式绑定，没有就用未指定的那一组）。
/// 设计期没有应用容器，由 <see cref="DesignTimeDbContextFactoryBase{TDbContext}"/> 在 options 上写
/// <see cref="DbContextRoleOptionsExtension"/>，它优先于登记。
/// </para>
/// </remarks>
internal static class PrimaryDbContextResolver
{
    /// <summary>从登记里判定主上下文类型；判定不了返回 null。</summary>
    public static Type? ResolvePrimary(IEnumerable<RegisteredDbContext> registrations)
    {
        Check.NotNull(registrations);

        var list = registrations.ToList();
        var primaries = list.Where(r => r.IsPrimary).Select(r => r.DbContextType).Distinct().ToList();
        if (primaries.Count > 0)
        {
            return primaries.Count == 1 ? primaries[0] : null;
        }

        var all = list.Select(r => r.DbContextType).Distinct().ToList();
        return all.Count == 1 ? all[0] : null;
    }

    /// <summary>
    /// 指定上下文是不是主上下文。null = 判定不了：它不在登记里（裸 <c>AddDbContext</c>、手工 new），或主上下文判定不了。
    /// </summary>
    public static bool? IsPrimary(Type dbContextType, IEnumerable<RegisteredDbContext> registrations)
    {
        Check.NotNull(dbContextType);
        Check.NotNull(registrations);

        var list = registrations.ToList();
        if (!list.Any(r => r.DbContextType == dbContextType))
        {
            return null;
        }

        var primary = ResolvePrimary(list);
        return primary == null ? null : primary == dbContextType;
    }

    /// <summary>
    /// 从上下文实例判定它是不是主上下文：设计期工厂写在 options 上的标记优先，其次应用容器里的登记。
    /// </summary>
    public static bool? IsPrimary(DbContext dbContext)
    {
        Check.NotNull(dbContext);

        try
        {
            var marker = dbContext.GetService<IDbContextOptions>().FindExtension<DbContextRoleOptionsExtension>();
            if (marker != null)
            {
                return marker.IsPrimary;
            }
        }
        catch (InvalidOperationException)
        {
            // 手工构造、没有 options 服务的上下文：没有标记，走下面的登记
        }

        var serviceProvider = DbContextServiceResolver.GetServiceProvider(dbContext);
        return serviceProvider == null
            ? null
            : IsPrimary(dbContext.GetType(), serviceProvider.GetServices<RegisteredDbContext>());
    }
}
