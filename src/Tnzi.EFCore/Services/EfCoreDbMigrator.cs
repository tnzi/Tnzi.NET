
namespace Tnzi.EFCore.Services;

/// <summary>
/// EF Core 数据库迁移器实现
/// 支持多 DbContext 迁移，自动发现所有注册的 DbContext
/// </summary>
public class EfCoreDbMigrator : IDbMigrator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEntityManager _entityManager;
    private readonly ILogger<EfCoreDbMigrator> _logger;

    public EfCoreDbMigrator(
        IServiceProvider serviceProvider,
        IEntityManager entityManager,
        ILogger<EfCoreDbMigrator> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _entityManager = Check.NotNull(entityManager);
        _logger = Check.NotNull(logger);
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var dbContextTypes = _entityManager.GetAllDbContextTypes();
        if (dbContextTypes.Length == 0)
        {
            _logger.LogWarning("No DbContext types found. Skipping migration.");
            return;
        }

        foreach (var dbContextType in dbContextTypes)
        {
            var dbContext = _serviceProvider.GetService(dbContextType) as DbContext;
            if (dbContext == null)
            {
                _logger.LogWarning("DbContext {Type} not resolved from DI. Skipping migration.", dbContextType.Name);
                continue;
            }

            try
            {
                var pending = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
                var pendingList = pending.ToList();

                if (pendingList.Count > 0)
                {
                    _logger.LogInformation("Applying {Count} pending migrations for {DbContext}: {Migrations}",
                        pendingList.Count, dbContextType.Name, string.Join(", ", pendingList));

                    await dbContext.Database.MigrateAsync(cancellationToken);

                    _logger.LogInformation("Successfully applied migrations for {DbContext}", dbContextType.Name);
                }
                else
                {
                    _logger.LogDebug("No pending migrations for {DbContext}", dbContextType.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to migrate database for {DbContext}", dbContextType.Name);
                throw;
            }
        }
    }

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        var dbContextTypes = _entityManager.GetAllDbContextTypes();

        foreach (var dbContextType in dbContextTypes)
        {
            var dbContext = _serviceProvider.GetService(dbContextType) as DbContext;
            if (dbContext == null) continue;

            try
            {
                var pending = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
                result.AddRange(pending.Select(m => $"[{dbContextType.Name}] {m}"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get pending migrations for {DbContext}", dbContextType.Name);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<string>> GetAppliedMigrationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        var dbContextTypes = _entityManager.GetAllDbContextTypes();

        foreach (var dbContextType in dbContextTypes)
        {
            var dbContext = _serviceProvider.GetService(dbContextType) as DbContext;
            if (dbContext == null) continue;

            try
            {
                var applied = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
                result.AddRange(applied.Select(m => $"[{dbContextType.Name}] {m}"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get applied migrations for {DbContext}", dbContextType.Name);
            }
        }

        return result;
    }

    public async Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken = default)
    {
        var dbContextTypes = _entityManager.GetAllDbContextTypes();

        foreach (var dbContextType in dbContextTypes)
        {
            var dbContext = _serviceProvider.GetService(dbContextType) as DbContext;
            if (dbContext == null) continue;

            try
            {
                var pending = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
                if (pending.Any())
                    return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check pending migrations for {DbContext}", dbContextType.Name);
            }
        }

        return false;
    }
}
