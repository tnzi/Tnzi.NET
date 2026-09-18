namespace Tnzi.EFCore.Internal;

/// <summary>
/// 软删 / 多租户查询过滤器的统一配置（<c>TnziDbContext</c> 与 <c>IdentityDbContext</c> 共用）。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <b>EF Core 的无名 <c>HasQueryFilter</c> 是单槽覆盖语义</b>：后一次调用顶掉前一次。
/// 同时是 <c>ISoftDelete + IMultiTenant</c> 的实体（<c>MultiTenantAuditedEntity</c> 一族，全框架近七十个）
/// 必须用一条<b>组合</b>过滤器表达两个条件。此前 <c>TnziDbContext</c> 有组合过滤器而
/// <c>IdentityDbContext</c> 各自维护一套并两次裸调 <c>HasQueryFilter</c>：多租户一开，
/// 租户过滤器顶掉软删过滤器，已软删的行对所有查询重新可见 —— 已收回的授权继续生效。
/// 两个基类的分支逻辑收口到这里，再也没有第二份可以漂开。
/// </para>
/// <para>
/// 表达式手工构造而不是写成泛型 lambda，理由见 <see cref="IQueryFilterContext"/>：
/// 过滤器里必须<b>直接</b>出现 DbContext 实例常量，EF 才会按执行查询的实例改写；
/// 泛型 lambda 只能把上下文捕获进闭包，改写认不出闭包字段。
/// </para>
/// </remarks>
public static class QueryFilterHelper
{
    private static readonly PropertyInfo SoftDeleteSwitch =
        typeof(IQueryFilterContext).GetProperty(nameof(IQueryFilterContext.IsSoftDeleteFilterEnabled))!;

    private static readonly PropertyInfo TenantSwitch =
        typeof(IQueryFilterContext).GetProperty(nameof(IQueryFilterContext.IsMultiTenantFilterEnabled))!;

    private static readonly PropertyInfo CurrentTenant =
        typeof(IQueryFilterContext).GetProperty(nameof(IQueryFilterContext.CurrentTenantId))!;

    /// <summary>
    /// 为模型里每个实体按 <c>ISoftDelete</c> / <c>IMultiTenant</c> 配置过滤器；
    /// 多租户关闭时把 <c>TenantId</c> 从模型里摘掉（数据库不出现该列）。
    /// </summary>
    public static void ConfigureQueryFilters(DbContext dbContext, ModelBuilder modelBuilder, bool multiTenancyEnabled)
    {
        Check.NotNull(dbContext);
        Check.NotNull(modelBuilder);
        if (dbContext is not IQueryFilterContext filterContext)
        {
            throw new InvalidOperationException(
                $"{dbContext.GetType().Name} must implement {nameof(IQueryFilterContext)} for the framework query filters to be configured.");
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            var isSoftDelete = typeof(ISoftDelete).IsAssignableFrom(clrType);
            var isMultiTenant = typeof(IMultiTenant).IsAssignableFrom(clrType);

            if (!isSoftDelete && !isMultiTenant)
            {
                continue;
            }

            var applyTenant = isMultiTenant && multiTenancyEnabled;
            if (isMultiTenant && !multiTenancyEnabled)
            {
                modelBuilder.Entity(clrType).Ignore(nameof(IMultiTenant.TenantId));
            }

            if (isSoftDelete || applyTenant)
            {
                modelBuilder.Entity(clrType).HasQueryFilter(BuildFilter(filterContext, clrType, isSoftDelete, applyTenant));
            }
        }
    }

    /// <summary>
    /// 只配软删过滤器（供基类保留的 <c>ConfigureSoftDeleteFilter&lt;T&gt;</c> 兼容入口使用）。
    /// </summary>
    public static void ApplySoftDeleteFilter(IQueryFilterContext filterContext, ModelBuilder modelBuilder, Type entityType)
        => modelBuilder.Entity(entityType).HasQueryFilter(BuildFilter(filterContext, entityType, softDelete: true, tenant: false));

    /// <summary>
    /// 只配租户过滤器（供基类保留的 <c>ConfigureMultiTenantFilter&lt;T&gt;</c> 兼容入口使用）。
    /// </summary>
    public static void ApplyMultiTenantFilter(IQueryFilterContext filterContext, ModelBuilder modelBuilder, Type entityType)
        => modelBuilder.Entity(entityType).HasQueryFilter(BuildFilter(filterContext, entityType, softDelete: false, tenant: true));

    /// <summary>
    /// 组合过滤器（供基类保留的 <c>ConfigureCombinedFilter&lt;T&gt;</c> 兼容入口使用）。
    /// </summary>
    public static void ApplyCombinedFilter(IQueryFilterContext filterContext, ModelBuilder modelBuilder, Type entityType)
        => modelBuilder.Entity(entityType).HasQueryFilter(BuildFilter(filterContext, entityType, softDelete: true, tenant: true));

    /// <summary>
    /// <c>e => (!ctx.IsSoftDeleteFilterEnabled || !e.IsDeleted) &amp;&amp; (!ctx.IsMultiTenantFilterEnabled || e.TenantId == ctx.CurrentTenantId)</c>，
    /// 按需只保留其中一半。<c>ctx</c> 是 DbContext 实例本身的常量。
    /// </summary>
    private static LambdaExpression BuildFilter(IQueryFilterContext filterContext, Type entityType, bool softDelete, bool tenant)
    {
        var entity = Expression.Parameter(entityType, "e");
        // 常量的静态类型必须是 DbContext 实例的运行期类型：EF 按「常量类型可赋给当前上下文类型」
        // 认出它并改写成执行查询的那个实例。再转成契约接口去访问成员。
        var context = Expression.Convert(Expression.Constant(filterContext, filterContext.GetType()), typeof(IQueryFilterContext));

        Expression? body = null;

        if (softDelete)
        {
            var softDeleteCondition = Expression.OrElse(
                Expression.Not(Expression.Property(context, SoftDeleteSwitch)),
                Expression.Not(Expression.Property(entity, nameof(ISoftDelete.IsDeleted))));
            body = softDeleteCondition;
        }

        if (tenant)
        {
            var tenantCondition = Expression.OrElse(
                Expression.Not(Expression.Property(context, TenantSwitch)),
                Expression.Equal(
                    Expression.Property(entity, nameof(IMultiTenant.TenantId)),
                    Expression.Property(context, CurrentTenant)));
            body = body == null ? tenantCondition : Expression.AndAlso(body, tenantCondition);
        }

        return Expression.Lambda(body!, entity);
    }
}
