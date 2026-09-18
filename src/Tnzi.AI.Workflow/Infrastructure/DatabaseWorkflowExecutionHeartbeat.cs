namespace Tnzi.AI.Workflow.Infrastructure;

/// <summary>
/// 默认心跳实现：按 <see cref="WorkflowWatchdogOptions.HeartbeatInterval"/> 把
/// <c>WorkflowExecution.UpdatedTime</c> 推到当前时刻。
/// </summary>
/// <remarks>
/// <para>
/// 每次心跳在**自己的作用域**里执行：引擎的心跳循环与请求线程并发运行，而 DbContext 不允许并发使用。
/// </para>
/// <para>
/// 用集合更新（<c>ExecuteUpdate</c>）只写这一列，**不经变更跟踪、不碰乐观并发标记**：请求作用域里的
/// 检查点保存靠那个标记做 CAS，心跳若走 UpdateAsync 会让每一次检查点保存都撞上冲突路径。
/// </para>
/// <para>
/// 更新时禁用多租户过滤器：引擎作用域派生出的作用域里未必有当前租户，过滤器会把租户的行藏起来，
/// 于是心跳 UPDATE 0 行、零症状 —— 与看门狗 2026-09-12 修掉的是同一种失效。ExecutionId 是 128 位随机数，
/// 按它更新一列时间戳没有跨租户面。
/// </para>
/// </remarks>
public class DatabaseWorkflowExecutionHeartbeat : IWorkflowExecutionHeartbeat
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<WorkflowWatchdogOptions> _options;
    private readonly ILogger<DatabaseWorkflowExecutionHeartbeat> _logger;

    public DatabaseWorkflowExecutionHeartbeat(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<WorkflowWatchdogOptions> options,
        ILogger<DatabaseWorkflowExecutionHeartbeat> logger)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public TimeSpan Interval => _options.CurrentValue.HeartbeatInterval;

    public async Task BeatAsync(string executionId, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowExecution, Guid>>();
        var filterManager = scope.ServiceProvider.GetService<IDataFilterManager>();
        var now = DateTime.UtcNow;

        using var tenantFilterScope = filterManager?.Disable<IMultiTenantFilter>();
        var affected = await repository.AsQueryable()
            .Where(e => e.ExecutionId == executionId)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.UpdatedTime, now), cancellationToken);

        if (affected == 0)
        {
            _logger.LogWarning("Heartbeat for workflow execution '{ExecutionId}' matched no row.", executionId);
        }
    }
}
