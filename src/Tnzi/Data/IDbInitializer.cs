namespace Tnzi.Data;

/// <summary>
/// 数据库初始化器接口（迁移 + 种子数据）
/// </summary>
public interface IDbInitializer
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
    Task SeedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 数据种子接口
/// </summary>
public interface IDataSeeder
{
    Task SeedAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);
}

/// <summary>
/// 数据库迁移器接口
/// 提供对 EF Core Migration 的抽象，支持启动时自动迁移和按需迁移
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IDbMigrator
{
    /// <summary>
    /// 执行所有待处理的数据库迁移
    /// </summary>
    Task MigrateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取待处理的迁移列表
    /// </summary>
    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取已应用的迁移列表
    /// </summary>
    Task<IReadOnlyList<string>> GetAppliedMigrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查数据库是否需要迁移
    /// </summary>
    Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}
