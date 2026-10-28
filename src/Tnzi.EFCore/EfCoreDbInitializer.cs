
namespace Tnzi.EFCore;

public class EFCoreDbInitializer<TDbContext> : IDbInitializer
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EFCoreDbInitializer<TDbContext>> _logger;

    public EFCoreDbInitializer(
        TDbContext dbContext,
        IServiceProvider serviceProvider,
        ILogger<EFCoreDbInitializer<TDbContext>> logger)
    {
        _dbContext = Check.NotNull(dbContext);
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        // 在调用数据库操作前，设置模块容器以确保 OnModelCreating 时能获取到表名前缀配置
        EnsureModuleContainerAvailable();

        // 如果是内存数据库，EnsureCreated
        if (_dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            await _dbContext.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        // 【性能优化】快速路径：优先检查是否有待应用的迁移（最常见情况）
        // 这个操作很快，因为只是检查迁移文件，不涉及数据库查询
        var pendingMigrations = (await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pendingMigrations.Count > 0)
        {
            // 有迁移文件，直接执行迁移（最快路径）
            _logger.LogInformation("Found {Count} pending migration(s), applying...", pendingMigrations.Count);
            await _dbContext.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Successfully applied {Count} migration(s).", pendingMigrations.Count);
            return;
        }

        // 慢速路径：没有待应用的迁移。先看历史表（库不存在时只答「没有」，不会去建库）
        var appliedMigrations = await _dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);

        if (appliedMigrations.Any())
        {
            // 区分两种 "history 非空" 场景，避免多 DbContext 共享历史表时的静默误判：
            var definedMigrations = _dbContext.Database.GetMigrations();
            if (definedMigrations.Any())
            {
                // 本 context 有自己的迁移文件且无 pending —— schema 确实最新，无需操作
                _logger.LogDebug("Database has migration history and no pending migrations. Schema is up to date.");
            }
            else
            {
                // 可疑：本 context 没有任何自己的迁移文件，历史表却非空。
                // 这通常意味着它与其他 DbContext 共享了同一张 __EFMigrationsHistory 表
                // （未配置独立 MigrationsHistoryTable），别的 context 的迁移记录被误判为本 context 已最新，
                // 从而跳过本 context 的建表 —— 本 context 的表可能根本没创建（参见 RagDbContext 既往问题）。
                _logger.LogWarning(
                    "DbContext '{DbContextName}' has no migration files of its own, yet the migration history table is non-empty. " +
                    "It likely shares '__EFMigrationsHistory' with another DbContext, so its tables may NOT have been created. " +
                    "Configure an isolated MigrationsHistoryTable (separate schema/table) for this context, or add migrations for it.",
                    typeof(TDbContext).Name);
            }
            return;
        }

        // 没有待应用、也没有已应用的迁移 = 本 context 没有任何迁移文件：只能按当前模型直接建表。
        // EnsureCreated 本身幂等：库不存在则建库，库里一张表都没有则建表，已经有表则不动。
        // ★ 不先问 CanConnect，也不先试 MigrateAsync。SQLite 打开连接即创建空文件，「能连上」恒为真，
        //   于是此前从不走建表分支；而对一个没有任何迁移的 context 调 MigrateAsync 一张业务表都不建，
        //   最多留下一张空的历史表，之后的 EnsureCreated 又因为「库里已经有表」什么都不做：
        //   新库启动成功，第一条查询才报 no such table。
        _logger.LogWarning(
            "DbContext '{DbContextName}' has no migrations; creating its schema with EnsureCreated. " +
            "For production, add migrations ('dotnet ef migrations add InitialCreate --context {DbContextName}') and apply them instead.",
            typeof(TDbContext).Name, typeof(TDbContext).Name);
        await _dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }

    /// <summary>
    /// 确保模块容器在 OnModelCreating 时可用
    /// 从服务提供者获取 IModuleContainer 并设置到 TableNamePrefixConfiguration.DesignTimeModuleContainer
    /// 这样 TableNamePrefixConfiguration 就能在 OnModelCreating 时获取到模块信息，正确应用表名前缀
    /// </summary>
    private void EnsureModuleContainerAvailable()
    {
        try
        {
            // 尝试从服务提供者获取 IModuleContainer
            var moduleContainer = _serviceProvider.GetService<IModuleContainer>();
            if (moduleContainer != null)
            {
                // 设置到静态属性，以便 TableNamePrefixConfiguration 在 OnModelCreating 时可以使用
                TableNamePrefixConfiguration.DesignTimeModuleContainer = moduleContainer;
                _logger.LogDebug("Module container set for database initialization. Table name prefixes will be applied.");
                return;
            }

            // 如果无法获取 IModuleContainer，尝试从 ITnziApplication 获取
            var application = _serviceProvider.GetService<ITnziApplication>();
            if (application != null)
            {
                var container = new ModuleContainer(application.Modules);
                TableNamePrefixConfiguration.DesignTimeModuleContainer = container;
                _logger.LogDebug("Module container created from ITnziApplication. Table name prefixes will be applied.");
                return;
            }

            _logger.LogWarning("Unable to get IModuleContainer or ITnziApplication. Table name prefixes may not be applied during EnsureCreated.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set module container. Table name prefixes may not be applied during EnsureCreated.");
        }
    }

}