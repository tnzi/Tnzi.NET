
namespace Tnzi.EFCore;

/// <summary>
/// 实体配置上下文辅助类，提供对当前配置上下文的访问
/// </summary>
/// <remarks>
/// 设计决策：使用 ThreadLocal 而非 AsyncLocal。
/// EF Core 的 OnModelCreating 始终在同步上下文中执行（即使调用方是 async），
/// 因此 ThreadLocal 在此场景下是安全且高效的。
/// RegisterTo 方法使用 try/finally 确保值在配置完成后立即清理，
/// 避免线程池复用导致的残留值问题。
/// 如果未来 EF Core 改为异步执行 OnModelCreating，需迁移至 AsyncLocal。
/// </remarks>
public static class EntityConfigurationContext
{
    private static readonly ThreadLocal<DbContext?> _currentDbContext = new();
    private static readonly ThreadLocal<ILogger?> _currentLogger = new();
    private static readonly ThreadLocal<bool> _fallbackWarningLogged = new();

    /// <summary>
    /// 设置当前配置上下文的 DbContext（由 EntityTypeConfigurationBase.RegisterTo 调用）
    /// </summary>
    internal static void SetCurrentDbContext(DbContext? dbContext)
    {
        _currentDbContext.Value = dbContext;
    }

    /// <summary>
    /// 设置当前配置上下文的 Logger（由 EntityTypeConfigurationBase.RegisterTo 调用）
    /// </summary>
    internal static void SetCurrentLogger(ILogger? logger)
    {
        _currentLogger.Value = logger;
    }

    /// <summary>
    /// 重置回退警告标志（每次 RegisterTo 调用前重置）
    /// </summary>
    internal static void ResetFallbackWarning()
    {
        _fallbackWarningLogged.Value = false;
    }

    /// <summary>
    /// 从线程本地存储获取当前配置上下文的数据库提供者
    /// 如果无法确定，则返回默认值（PostgreSQL）并记录警告日志
    /// </summary>
    /// <returns>数据库提供者</returns>
    /// <remarks>
    /// 此方法供 IndexFilterFactory 和 PropertyBuilderExtensions 等工厂类使用，
    /// 用于在实体配置中自动获取数据库提供者，无需显式传递参数。
    /// 只能在 EntityTypeConfigurationBase.Configure 方法执行期间调用。
    /// </remarks>
    public static DatabaseProvider GetCurrentDatabaseProviderOrDefault()
    {
        var dbContext = GetCurrentDbContext();
        var provider = DatabaseProviderDetector.DetectFromDbContext(dbContext);
        if (provider != null)
            return provider.Value;

        // 回退到 PostgreSQL 并记录警告（每次 RegisterTo 周期内仅记录一次）
        if (!_fallbackWarningLogged.Value)
        {
            _currentLogger.Value?.LogWarning(
                "Could not detect database provider from DbContext during entity configuration. " +
                "Falling back to PostgreSQL. If you are using a different database provider, " +
                "ensure the DbContext is properly configured or pass the provider explicitly.");
            _fallbackWarningLogged.Value = true;
        }

        return DatabaseProvider.PostgreSQL;
    }

    /// <summary>
    /// 从 DbContext 检测数据库提供者
    /// 委托给 DatabaseProviderDetector 的统一实现，避免检测逻辑重复
    /// </summary>
    public static DatabaseProvider? GetDatabaseProviderFromDbContext(DbContext? dbContext)
    {
        return DatabaseProviderDetector.DetectFromDbContext(dbContext);
    }

    /// <summary>
    /// 获取当前配置上下文的 DbContext 值（供 EntityTypeConfigurationBase.GetDbContext 调用）
    /// </summary>
    internal static DbContext? GetCurrentDbContextValue()
    {
        return _currentDbContext.Value;
    }

    private static DbContext? GetCurrentDbContext()
    {
        return _currentDbContext.Value;
    }
}

/// <summary>
/// 实体类型配置基类，实现实体配置和自动注册
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
/// <typeparam name="TKey">主键类型</typeparam>
[StableApi(Since = "0.1.0")]
public abstract class EntityTypeConfigurationBase<TEntity, TKey> : IEntityTypeConfiguration<TEntity>, IEntityRegister
    where TEntity : class, IEntity<TKey>
{
    /// <summary>
    /// 线程本地存储，用于在 Configure 方法中访问当前的 ModelBuilder
    /// </summary>
    private static readonly ThreadLocal<ModelBuilder?> _currentModelBuilder = new();

    /// <summary>
    /// 获取所属的 DbContext 类型，null 表示注册到默认/主 DbContext
    /// </summary>
    public virtual Type? DbContextType => null;

    /// <summary>
    /// 获取实体类型
    /// </summary>
    public Type EntityType => typeof(TEntity);

    /// <summary>
    /// 将实体注册到模型构建器
    /// </summary>
    public void RegisterTo(ModelBuilder modelBuilder)
    {
        RegisterTo(modelBuilder, null);
    }

    /// <summary>
    /// 将实体注册到模型构建器（带 DbContext 参数，用于获取数据库提供者信息）
    /// </summary>
    public void RegisterTo(ModelBuilder modelBuilder, DbContext? dbContext)
    {
        _currentModelBuilder.Value = modelBuilder;
        EntityConfigurationContext.SetCurrentDbContext(dbContext);

        // 设置 Logger 以便回退时记录警告
        ILogger? logger = null;
        if (dbContext != null)
        {
            try
            {
                var factory = dbContext.GetService<ILoggerFactory>();
                logger = factory?.CreateLogger(typeof(EntityConfigurationContext));
            }
            catch { /* Logger 是可选的 */ }
        }
        EntityConfigurationContext.SetCurrentLogger(logger);
        EntityConfigurationContext.ResetFallbackWarning();

        try
        {
            modelBuilder.ApplyConfiguration(this);
        }
        finally
        {
            _currentModelBuilder.Value = null;
            EntityConfigurationContext.SetCurrentDbContext(null);
            EntityConfigurationContext.SetCurrentLogger(null);
        }
    }

    /// <summary>
    /// 实现实体配置（由子类重写）
    /// </summary>
    public abstract void Configure(EntityTypeBuilder<TEntity> builder);

    /// <summary>
    /// 获取当前配置上下文的 ModelBuilder
    /// </summary>
    protected ModelBuilder? GetModelBuilder()
    {
        return _currentModelBuilder.Value;
    }

    /// <summary>
    /// 获取当前配置上下文的 DbContext（从 EntityConfigurationContext 统一获取）
    /// </summary>
    protected DbContext? GetDbContext()
    {
        return EntityConfigurationContext.GetCurrentDbContextValue();
    }

    /// <summary>
    /// 从 DbContext 获取数据库提供者
    /// </summary>
    /// <param name="dbContext">数据库上下文</param>
    /// <returns>数据库提供者，如果无法确定则返回 null</returns>
    /// <remarks>
    /// 委托给 EntityConfigurationContext 的统一实现，避免重复代码。
    /// </remarks>
    protected static DatabaseProvider? GetDatabaseProvider(DbContext? dbContext)
    {
        return EntityConfigurationContext.GetDatabaseProviderFromDbContext(dbContext);
    }

    /// <summary>
    /// 获取当前配置上下文的数据库提供者
    /// 如果无法确定，则返回默认值（PostgreSQL）并记录警告日志
    /// </summary>
    protected DatabaseProvider GetDatabaseProviderOrDefault()
    {
        // 委托给 EntityConfigurationContext，统一回退逻辑和日志记录
        return EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
    }

}