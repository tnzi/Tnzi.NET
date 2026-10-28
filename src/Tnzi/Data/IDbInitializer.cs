namespace Tnzi.Data;

/// <summary>
/// 数据库初始化器接口（迁移）
/// </summary>
/// <remarks>
/// 每个 DbContext 注册一个。种子数据不经它：启动流程在全部迁移完成后把每个 <see cref="IDataSeeder"/> 执行一次，
/// 与有几个 DbContext 无关（种子挂在初始化器上就会按 DbContext 的个数重复执行）。
/// </remarks>
public interface IDbInitializer
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
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
