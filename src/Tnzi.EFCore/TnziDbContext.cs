
namespace Tnzi.EFCore;

/// <summary>
/// Tnzi 数据库上下文基类
/// </summary>
/// <typeparam name="TDbContext">派生的 DbContext 类型</typeparam>
[StableApi(Since = "0.1.0")]
public abstract class TnziDbContext<TDbContext> : DbContext
    , IMultiTenancySwitchProvider
    , IQueryFilterContext
    , IAuditPropertyContext
    where TDbContext : DbContext
{
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
        // 三个可选协作者与下面的开关同一个根因：消费方只声明 (options, currentUser)，这里的实参恒为 null。
        // 注入值缺席时从 options 携带的应用容器解析（AddDbContext 记下的请求作用域容器）；
        // 手工构造（设计期）没有容器，仍是 null。见 DbContextCollaborators。
        CurrentTenant = DbContextCollaborators.Resolve(currentTenant, options);
        DataFilterManager = DbContextCollaborators.Resolve(dataFilterManager, options);
        TimeProvider = DbContextCollaborators.Resolve(timeProvider, options);
        // 显式转发的选项优先；否则读 options 里的 MultiTenancyOptionsExtension ——
        // 那是 AddTnziDbContext（运行期）与 DesignTimeDbContextFactoryBase（设计期）共同写入的载体。
        // ★ 绝大多数消费方 DbContext 只声明 (options, currentUser)，第一项恒为 null；
        //   此前没有第二项时开关在运行期恒 false，MultiTenancy:Enabled=true 整条不生效。见 MultiTenancySwitch。
        _multiTenancyEnabled = MultiTenancySwitch.Resolve(multiTenancyOptions?.Value.Enabled, options);
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

    /// <summary>
    /// 配置软删 / 多租户查询过滤器。实现与 <c>IdentityDbContext</c> 共用（<see cref="TnziDbContextHelper.ConfigureQueryFilters"/>），
    /// 同时是 <c>ISoftDelete + IMultiTenant</c> 的实体得到一条组合过滤器。
    /// </summary>
    protected virtual void ConfigureQueryFilters(ModelBuilder modelBuilder)
    {
        TnziDbContextHelper.ConfigureQueryFilters(this, modelBuilder, _multiTenancyEnabled);
    }

    protected virtual bool IsSoftDeleteFilterEnabled => DataFilterManager?.IsEnabled<ISoftDeleteFilter>() ?? true;
    protected virtual bool IsMultiTenantFilterEnabled => _multiTenancyEnabled && (DataFilterManager?.IsEnabled<IMultiTenantFilter>() ?? true);

    /// <summary>
    /// 获取当前租户ID（在查询时调用，表达式树延迟求值）
    /// </summary>
    protected virtual Guid? GetCurrentTenantId() => CurrentTenant?.Id ?? CurrentUser?.TenantId;

    // 过滤器表达式经这个契约访问上面三个成员；显式实现让子类对 protected virtual 的覆写照常生效。
    bool IQueryFilterContext.IsSoftDeleteFilterEnabled => IsSoftDeleteFilterEnabled;
    bool IQueryFilterContext.IsMultiTenantFilterEnabled => IsMultiTenantFilterEnabled;
    Guid? IQueryFilterContext.CurrentTenantId => GetCurrentTenantId();

    // 绕过变更跟踪器的写入路径（Dapper 批量插入 / 更新）经这个契约取到与 SaveChanges 同一组协作者。
    ICurrentUser IAuditPropertyContext.CurrentUser => CurrentUser;
    ICurrentTenant? IAuditPropertyContext.CurrentTenant => CurrentTenant;
    TimeProvider? IAuditPropertyContext.TimeProvider => TimeProvider;

    /// <summary>
    /// 单独为一个实体配置软删过滤器（<see cref="ConfigureQueryFilters"/> 已覆盖全部实体；保留给需要逐个实体覆写的子类）。
    /// </summary>
    protected void ConfigureSoftDeleteFilter<T>(ModelBuilder modelBuilder) where T : class, ISoftDelete
        => QueryFilterHelper.ApplySoftDeleteFilter(this, modelBuilder, typeof(T));

    /// <summary>
    /// 单独为一个实体配置多租户过滤器。
    /// </summary>
    protected void ConfigureMultiTenantFilter<T>(ModelBuilder modelBuilder) where T : class, IMultiTenant
        => QueryFilterHelper.ApplyMultiTenantFilter(this, modelBuilder, typeof(T));

    /// <summary>
    /// 单独为一个实体配置组合过滤器（软删 + 多租户）。
    /// EF Core 的无名 HasQueryFilter 是覆盖式的，同时实现两个接口的实体必须用一条组合过滤器。
    /// </summary>
    protected void ConfigureCombinedFilter<T>(ModelBuilder modelBuilder) where T : class, ISoftDelete, IMultiTenant
        => QueryFilterHelper.ApplyCombinedFilter(this, modelBuilder, typeof(T));

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
