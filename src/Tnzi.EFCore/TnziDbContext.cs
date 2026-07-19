
namespace Tnzi.EFCore;

/// <summary>
/// Tnzi 数据库上下文基类
/// </summary>
/// <typeparam name="TDbContext">派生的 DbContext 类型</typeparam>
[StableApi(Since = "0.1.0")]
public abstract class TnziDbContext<TDbContext> : DbContext
    , Internal.IMultiTenancySwitchProvider
    where TDbContext : DbContext
{
    // 缓存泛型方法实例，避免每次调用 MakeGenericMethod
    private static readonly ConcurrentDictionary<Type, MethodInfo> SoftDeleteFilterMethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> MultiTenantFilterMethodCache = new();

    // 组合过滤器缓存（同时实现 ISoftDelete + IMultiTenant 的实体）
    private static readonly ConcurrentDictionary<Type, MethodInfo> CombinedFilterMethodCache = new();

    // 基础方法缓存（非泛型）
    private static readonly MethodInfo? BaseSoftDeleteFilterMethod = typeof(TnziDbContext<TDbContext>)
        .GetMethod(nameof(ConfigureSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly MethodInfo? BaseMultiTenantFilterMethod = typeof(TnziDbContext<TDbContext>)
        .GetMethod(nameof(ConfigureMultiTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly MethodInfo? BaseCombinedFilterMethod = typeof(TnziDbContext<TDbContext>)
        .GetMethod(nameof(ConfigureCombinedFilter), BindingFlags.NonPublic | BindingFlags.Instance);

    protected ICurrentUser CurrentUser { get; }
    protected ICurrentTenant? CurrentTenant { get; }
    protected IDataFilterManager? DataFilterManager { get; }
    protected TimeProvider? TimeProvider { get; }
    private readonly bool _multiTenancyEnabled;

    public TnziDbContext(
        DbContextOptions<TDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null,
        TimeProvider? timeProvider = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(options)
    {
        CurrentUser = Check.NotNull(currentUser);
        CurrentTenant = currentTenant;
        DataFilterManager = dataFilterManager;
        TimeProvider = timeProvider;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    public bool IsMultiTenancyEnabled => _multiTenancyEnabled;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 使用辅助类统一处理模型初始化（实体注册、批量配置等）
        TnziDbContextHelper.OnModelCreating(this, modelBuilder);

        // 配置查询过滤器
        ConfigureQueryFilters(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // 全局模型约定（含未显式配置精度的 decimal 列的默认精度）
        TnziDbContextHelper.ConfigureConventions(configurationBuilder);
    }

    protected virtual void ConfigureQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            var isSoftDelete = typeof(ISoftDelete).IsAssignableFrom(clrType);
            var isMultiTenant = typeof(IMultiTenant).IsAssignableFrom(clrType);

            if (isSoftDelete && isMultiTenant)
            {
                // EF Core 限制每个实体只允许一个 HasQueryFilter，必须使用组合过滤器
                if (_multiTenancyEnabled)
                {
                    var method = GetOrCreateCombinedFilterMethod(clrType);
                    method?.Invoke(this, [modelBuilder]);
                }
                else
                {
                    var method = GetOrCreateSoftDeleteFilterMethod(clrType);
                    method?.Invoke(this, [modelBuilder]);
                    modelBuilder.Entity(clrType).Ignore(nameof(IMultiTenant.TenantId));
                }
            }
            else if (isSoftDelete)
            {
                var method = GetOrCreateSoftDeleteFilterMethod(clrType);
                method?.Invoke(this, [modelBuilder]);
            }
            else if (isMultiTenant)
            {
                if (_multiTenancyEnabled)
                {
                    var method = GetOrCreateMultiTenantFilterMethod(clrType);
                    method?.Invoke(this, [modelBuilder]);
                }
                else
                {
                    modelBuilder.Entity(clrType).Ignore(nameof(IMultiTenant.TenantId));
                }
            }
        }
    }

    private static MethodInfo? GetOrCreateSoftDeleteFilterMethod(Type entityType)
        => GetOrCreateFilterMethod(SoftDeleteFilterMethodCache, BaseSoftDeleteFilterMethod, entityType);

    private static MethodInfo? GetOrCreateMultiTenantFilterMethod(Type entityType)
        => GetOrCreateFilterMethod(MultiTenantFilterMethodCache, BaseMultiTenantFilterMethod, entityType);

    private static MethodInfo? GetOrCreateCombinedFilterMethod(Type entityType)
        => GetOrCreateFilterMethod(CombinedFilterMethodCache, BaseCombinedFilterMethod, entityType);

    private static MethodInfo? GetOrCreateFilterMethod(ConcurrentDictionary<Type, MethodInfo> cache, MethodInfo? baseMethod, Type entityType)
    {
        if (baseMethod == null)
            return null;

        return cache.GetOrAdd(entityType, type => baseMethod.MakeGenericMethod(type));
    }

    protected virtual bool IsSoftDeleteFilterEnabled => DataFilterManager?.IsEnabled<ISoftDeleteFilter>() ?? true;
    protected virtual bool IsMultiTenantFilterEnabled => _multiTenancyEnabled && (DataFilterManager?.IsEnabled<IMultiTenantFilter>() ?? true);

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

    /// <summary>
    /// 配置组合查询过滤器（软删除 + 多租户）
    /// EF Core 限制每个实体只允许一个 HasQueryFilter，因此同时实现两个接口的实体必须使用组合过滤器
    /// </summary>
    protected void ConfigureCombinedFilter<T>(ModelBuilder modelBuilder) where T : class, ISoftDelete, IMultiTenant
    {
        modelBuilder.Entity<T>().HasQueryFilter(e =>
            (!IsSoftDeleteFilterEnabled || !e.IsDeleted) &&
            (!IsMultiTenantFilterEnabled || e.TenantId == GetCurrentTenantId()));
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 使用辅助类处理通用的保存逻辑（审计、文件追踪、领域事件、事务等）
        return await TnziDbContextHelper.SaveChangesAsync(
            this,
            base.SaveChangesAsync,
            CurrentUser,
            CurrentTenant,
            cancellationToken,
            TimeProvider,
            _multiTenancyEnabled);
    }

    /// <summary>
    /// 同步保存已被禁用。框架的审计字段填充、ID 生成、软删除转换管线是异步实现的
    /// （仅拦截 <see cref="SaveChangesAsync(CancellationToken)"/>）；同步 <see cref="SaveChanges()"/>
    /// 会绕过全部横切逻辑（软删除实体被物理 DELETE、审计字段与 ID 不被填充），因此显式禁用。
    /// </summary>
    /// <exception cref="NotSupportedException">始终抛出，提示改用 <see cref="SaveChangesAsync(CancellationToken)"/>。</exception>
    public override int SaveChanges()
        => throw new NotSupportedException(SyncSaveNotSupportedMessage);

    /// <inheritdoc cref="SaveChanges()"/>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => throw new NotSupportedException(SyncSaveNotSupportedMessage);

    private const string SyncSaveNotSupportedMessage =
        "Synchronous SaveChanges() is not supported on TnziDbContext. The audit-field population, " +
        "ID generation, and soft-delete conversion pipeline is async-only (only SaveChangesAsync is intercepted). " +
        "Calling SaveChanges() would bypass these cross-cutting steps: soft-deletable entities would be physically " +
        "deleted, and audit fields and IDs would be left unset. Use SaveChangesAsync(CancellationToken) instead.";
}
