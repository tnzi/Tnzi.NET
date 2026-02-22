
namespace Tnzi.EFCore;

/// <summary>
/// Tnzi 数据库上下文基类
/// </summary>
/// <typeparam name="TDbContext">派生的 DbContext 类型</typeparam>
[StableApi(Since = "0.1.0")]
public abstract class TnziDbContext<TDbContext> : DbContext
    where TDbContext : DbContext
{
    // 缓存泛型方法实例，避免每次调用 MakeGenericMethod
    private static readonly ConcurrentDictionary<Type, MethodInfo> SoftDeleteFilterMethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> MultiTenantFilterMethodCache = new();

    // 基础方法缓存（非泛型）
    private static readonly MethodInfo? BaseSoftDeleteFilterMethod = typeof(TnziDbContext<TDbContext>)
        .GetMethod(nameof(ConfigureSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly MethodInfo? BaseMultiTenantFilterMethod = typeof(TnziDbContext<TDbContext>)
        .GetMethod(nameof(ConfigureMultiTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance);

    protected ICurrentUser CurrentUser { get; }
    protected ICurrentTenant? CurrentTenant { get; }
    protected IDataFilterManager? DataFilterManager { get; }

    public TnziDbContext(
        DbContextOptions<TDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null)
        : base(options)
    {
        CurrentUser = Check.NotNull(currentUser);
        CurrentTenant = currentTenant;
        DataFilterManager = dataFilterManager;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 使用辅助类统一处理模型初始化（实体注册、批量配置等）
        TnziDbContextHelper.OnModelCreating(this, modelBuilder);

        // 配置查询过滤器
        ConfigureQueryFilters(modelBuilder);
    }

    protected virtual void ConfigureQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (typeof(ISoftDelete).IsAssignableFrom(clrType))
            {
                var method = GetOrCreateSoftDeleteFilterMethod(clrType);
                method?.Invoke(this, new object[] { modelBuilder });
            }

            if (typeof(IMultiTenant).IsAssignableFrom(clrType))
            {
                var method = GetOrCreateMultiTenantFilterMethod(clrType);
                method?.Invoke(this, new object[] { modelBuilder });
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