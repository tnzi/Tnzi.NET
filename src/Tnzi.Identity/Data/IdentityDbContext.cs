
namespace Tnzi.Identity.Data;

/// <summary>
/// Tnzi Identity 数据库上下文基类
/// 继承自 ASP.NET Core Identity 的 IdentityDbContext,并集成 Tnzi.NET 框架的自动配置功能
/// </summary>
/// <typeparam name="TDbContext">派生的 DbContext 类型</typeparam>
public abstract class IdentityDbContext<TDbContext> : IdentityDbContext<User, Role, Guid, UserClaim, UserRole, UserLogin, RoleClaim, UserToken>
    where TDbContext : DbContext
{
    protected ICurrentUser CurrentUser { get; }
    protected ICurrentTenant? CurrentTenant { get; }
    protected IDataFilterManager? DataFilterManager { get; }

    /// <summary>
    /// 初始化 IdentityDbContext 实例
    /// </summary>
    /// <param name="options">DbContext 选项</param>
    /// <param name="currentUser">当前用户服务</param>
    /// <param name="currentTenant">当前租户服务（可选）</param>
    /// <param name="dataFilterManager">数据过滤器管理器（可选）</param>
    public IdentityDbContext(
        DbContextOptions<TDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null)
        : base(options)
    {
        CurrentUser = currentUser;
        CurrentTenant = currentTenant;
        DataFilterManager = dataFilterManager;
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // 使用辅助类统一处理模型初始化
        // 传入额外的 Identity 表名配置 Action，确保 Identity 实体被正确配置
        TnziDbContextHelper.OnModelCreating(this, builder, (b) =>
        {
            // 1. 移除 Identity 默认的 AspNet 前缀
            ConfigureIdentityTableNames(b);
        });

        // 2. 修复 IdentityPasskeyData 表名 (如果适用)
        FixIdentityPasskeyDataTableName(builder);

        // 3. 配置查询过滤器
        ConfigureQueryFilters(builder);
    }

    /// <summary>
    /// 配置 Identity 相关实体的表名，移除默认的 "AspNet" 前缀
    /// 表名会在后续的 TableNamePrefixConfiguration 中自动添加 "Identity_" 前缀
    /// </summary>
    protected virtual void ConfigureIdentityTableNames(ModelBuilder builder)
    {
        builder.Entity<User>().ToTable("User");
        builder.Entity<Role>().ToTable("Role");
        builder.Entity<UserRole>().ToTable("UserRole");
        builder.Entity<UserClaim>().ToTable("UserClaim");
        builder.Entity<UserLogin>().ToTable("UserLogin");
        builder.Entity<RoleClaim>().ToTable("RoleClaim");
        builder.Entity<UserToken>().ToTable("UserToken");

#if NET10_0_OR_GREATER
        try
        {
            builder.Entity<Microsoft.AspNetCore.Identity.IdentityPasskeyData>().ToTable("PasskeyData").HasNoKey();
        }
        catch (InvalidOperationException)
        {
            // 预期的异常：当 IdentityPasskeyData 实体不存在或已配置时会发生
            // 这是正常情况，不需要处理
        }
#endif
    }

    /// <summary>
    /// 修复 IdentityPasskeyData 表名
    /// </summary>
    protected virtual void FixIdentityPasskeyDataTableName(ModelBuilder builder)
    {
#if NET10_0_OR_GREATER
        try
        {
            var passkeyType = typeof(Microsoft.AspNetCore.Identity.IdentityPasskeyData);
            if (builder.Model.FindEntityType(passkeyType) != null)
            {
                builder.Entity(passkeyType).ToTable("Identity_PasskeyData");
            }
        }
        catch (InvalidOperationException)
        {
            // 预期的异常：当 IdentityPasskeyData 实体不存在或已配置时会发生
            // 这是正常情况，不需要处理
        }
#endif
    }

    // 缓存泛型方法实例，避免每次调用 MakeGenericMethod
    private static readonly ConcurrentDictionary<Type, MethodInfo> SoftDeleteFilterMethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> MultiTenantFilterMethodCache = new();

    // 基础方法缓存（非泛型）
    private static readonly MethodInfo? BaseSoftDeleteFilterMethod = typeof(IdentityDbContext<TDbContext>)
        .GetMethod(nameof(ConfigureSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly MethodInfo? BaseMultiTenantFilterMethod = typeof(IdentityDbContext<TDbContext>)
        .GetMethod(nameof(ConfigureMultiTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// 配置查询过滤器 (软删除和多租户)
    /// </summary>
    protected virtual void ConfigureQueryFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (typeof(ISoftDelete).IsAssignableFrom(clrType))
            {
                var method = GetOrCreateSoftDeleteFilterMethod(clrType);
                method?.Invoke(this, new object[] { builder });
            }

            if (typeof(IMultiTenant).IsAssignableFrom(clrType))
            {
                var method = GetOrCreateMultiTenantFilterMethod(clrType);
                method?.Invoke(this, new object[] { builder });
            }
        }
    }

    /// <summary>
    /// 获取或创建软删除过滤器的泛型方法实例
    /// </summary>
    private static MethodInfo? GetOrCreateSoftDeleteFilterMethod(Type entityType)
    {
        if (BaseSoftDeleteFilterMethod == null)
            return null;

        return SoftDeleteFilterMethodCache.GetOrAdd(entityType,
            type => BaseSoftDeleteFilterMethod.MakeGenericMethod(type));
    }

    /// <summary>
    /// 获取或创建多租户过滤器的泛型方法实例
    /// </summary>
    private static MethodInfo? GetOrCreateMultiTenantFilterMethod(Type entityType)
    {
        if (BaseMultiTenantFilterMethod == null)
            return null;

        return MultiTenantFilterMethodCache.GetOrAdd(entityType,
            type => BaseMultiTenantFilterMethod.MakeGenericMethod(type));
    }

    protected virtual bool IsSoftDeleteFilterEnabled => DataFilterManager?.IsEnabled<ISoftDeleteFilter>() ?? true;
    protected virtual bool IsMultiTenantFilterEnabled => DataFilterManager?.IsEnabled<IMultiTenantFilter>() ?? true;

    /// <summary>
    /// 获取当前租户ID（在查询时调用，表达式树延迟求值）
    /// </summary>
    protected virtual Guid? GetCurrentTenantId() => CurrentTenant?.Id ?? CurrentUser?.TenantId;

    protected void ConfigureSoftDeleteFilter<T>(ModelBuilder modelBuilder) where T : class, ISoftDelete
        => modelBuilder.Entity<T>().HasQueryFilter(e => !IsSoftDeleteFilterEnabled || !e.IsDeleted);

    /// <summary>
    /// 配置多租户查询过滤器
    /// </summary>
    protected void ConfigureMultiTenantFilter<T>(ModelBuilder modelBuilder) where T : class, IMultiTenant
    {
        modelBuilder.Entity<T>().HasQueryFilter(e =>
            !IsMultiTenantFilterEnabled || e.TenantId == GetCurrentTenantId());
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 使用辅助类处理通用的保存逻辑（审计、文件追踪、事务等）
        return await TnziDbContextHelper.SaveChangesAsync(
            this,
            base.SaveChangesAsync,
            CurrentUser,
            CurrentTenant,
            cancellationToken);
    }
}
