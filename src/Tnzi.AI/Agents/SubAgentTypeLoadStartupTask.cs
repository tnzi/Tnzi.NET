namespace Tnzi.AI.Agents;

/// <summary>
/// 启动时把数据库里启用的子 Agent 类型（<see cref="SubAgentType"/>）读进 <see cref="ISubAgentRegistry"/> 的租户桶。
/// </summary>
/// <remarks>
/// <para>
/// 走 <see cref="IPostMigrationStartupTask"/>：迁移之后才跑，空库首启时表已经在了；启动那一刻数据库不可达时
/// 抛出，由运行器在后台重试直到读到，健康检查 <c>startup-tasks</c> 在此之前报 Degraded。
/// 以前它在模块初始化里（迁移之前）读，失败只记一条 Warning：每个租户自定义的类型都缺席，
/// 直到那个租户改一次类型（只重载它自己的桶）或应用重启。
/// </para>
/// <para>
/// 缺席期间内置类型照常可用，所以不阻止接流量（<see cref="IPostMigrationStartupTask.RequiredForReadiness"/> 为 false）。
/// </para>
/// </remarks>
internal sealed class SubAgentTypeLoadStartupTask : IPostMigrationStartupTask
{
    public async Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        Check.NotNull(serviceProvider);

        var registry = serviceProvider.GetRequiredService<ISubAgentRegistry>();
        await using var scope = serviceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<SubAgentType, Guid>>();

        // 根作用域没有租户，过滤器只放行 TenantId 为 null 的行；关掉它一次读全部租户，按 TenantId 分桶
        using var tenantFilterScope = scope.ServiceProvider.GetService<IDataFilterManager>()?.Disable<IMultiTenantFilter>();
        await registry.LoadAllTenantsFromStoreAsync(repository, cancellationToken);
        scope.ServiceProvider.GetRequiredService<ILogger<SubAgentTypeLoadStartupTask>>()
            .LogDebug("Sub-agent type definitions loaded from database.");
    }
}
