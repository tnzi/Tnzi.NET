
namespace Tnzi.EFCore;

public class EFCoreDbInitializer<TDbContext> : IDbInitializer
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<IDataSeeder> _seeders;
    private readonly ILogger<EFCoreDbInitializer<TDbContext>> _logger;

    public EFCoreDbInitializer(
        TDbContext dbContext,
        IServiceProvider serviceProvider,
        IEnumerable<IDataSeeder> seeders,
        ILogger<EFCoreDbInitializer<TDbContext>> logger)
    {
        _dbContext = Check.NotNull(dbContext);
        _serviceProvider = Check.NotNull(serviceProvider);
        _seeders = Check.NotNull(seeders);
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

        // 【性能优化】慢速路径：只有在没有迁移文件时才执行详细检查
        // 检查数据库是否已存在（这个操作需要连接数据库，较慢）
        _logger.LogDebug("No migrations found, checking database connection...");
        var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);

        if (!canConnect)
        {
            // 数据库不存在，使用 EnsureCreated 创建
            _logger.LogWarning("Database does not exist, creating using EnsureCreated. For production, use 'dotnet ef migrations add' first.");
            await _dbContext.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        // 数据库存在但没有【待应用】迁移
        // 检查是否有迁移历史记录（这个操作需要查询数据库，较慢）
        var appliedMigrations = await _dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
        var hasMigrationHistory = appliedMigrations.Any();

        if (hasMigrationHistory)
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

        // 数据库存在但没有迁移历史，可能是用 EnsureCreated 创建的
        // 为了支持根据最新实体更新表结构，我们需要使用 Migrations
        _logger.LogWarning("Database exists but no migration history found. This database may have been created with EnsureCreated.");
        _logger.LogInformation("Attempting to use Migrations to update database schema...");

        try
        {
            // 尝试使用 MigrateAsync，即使没有迁移文件
            // 如果数据库中没有 __EFMigrationsHistory 表，MigrateAsync 会抛出异常
            await _dbContext.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Database schema updated successfully using Migrations.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("migration", StringComparison.OrdinalIgnoreCase))
        {
            // 没有迁移历史表，需要先创建初始迁移
            _logger.LogWarning("No migration history found. To update database schema based on latest entities:");
            _logger.LogWarning("1. Run: dotnet ef migrations add InitialCreate --context {DbContextName}", typeof(TDbContext).Name);
            _logger.LogWarning("2. Run: dotnet ef database update --context {DbContextName}", typeof(TDbContext).Name);
            _logger.LogWarning("Or run the application again after creating migrations.");

            // 【性能优化】移除硬编码的表名检查，直接尝试 EnsureCreated
            // EnsureCreated 在数据库已存在时不会创建缺失的表，但也不会报错
            try
            {
                await _dbContext.Database.EnsureCreatedAsync(cancellationToken);
                _logger.LogInformation("Schema ensured. If tables are missing, please create migrations to update the database schema.");
            }
            catch (Exception ensureEx)
            {
                _logger.LogError(ensureEx, "Failed to ensure schema. Please create and apply migrations manually.");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate database. Please create and apply migrations manually.");
            throw;
        }
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var seeder in _seeders)
        {
            await seeder.SeedAsync(_serviceProvider, cancellationToken);
        }
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