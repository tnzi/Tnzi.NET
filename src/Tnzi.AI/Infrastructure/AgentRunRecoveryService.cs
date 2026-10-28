namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 回收孤儿运行：执行它的进程已经不在了（重新部署、崩溃），行却还停在 Pending / Running。
/// </summary>
/// <remarks>
/// <para>
/// 运行的终态只由执行它的那个进程写入。进程一没，行就永远停在「在跑」：spawn 的并发上限按「仍在跑」计数
/// （<see cref="IRunStore.CountActiveRootRunsByOwnerAsync"/> / <see cref="IRunStore.CountActiveDescendantsAsync"/>），
/// 于是这个用户此后的每一次 spawn 都被 429 拒绝，管理端统计里也一直挂着这些运行，直到有人逐条手工取消。
/// </para>
/// <para>
/// 判据是心跳而不是「启动时所有在跑的行」：多实例部署下，另一个节点上的运行正常在跑。
/// 在跑的运行由 <see cref="IAgentRunHeartbeat"/> 每分钟刷新 <see cref="AgentRun.LastHeartbeatAt"/>；
/// 超过 <see cref="StaleAfter"/> 没有心跳（没有心跳列的旧行按建行时间算）就判定执行者已经不在，
/// 标为 Failed（可续跑），Error 写明原因。
/// </para>
/// <para>
/// 工作流运行（带 <see cref="AgentRun.WorkflowExecutionId"/>）不在这里回收：它们的进度由工作流引擎
/// 记在执行实例上，由工作流自己的看门狗判定。
/// </para>
/// <para>
/// 跨全部租户：调用方是后台作用域，没有当前租户；摘掉全局过滤器、手工补回软删条件。
/// 回收是按行的条件更新（只动仍在跑且心跳仍旧的行），与运行自己的收尾或心跳赛跑时以它们为准，多实例同时扫描也安全。
/// </para>
/// </remarks>
public class AgentRunRecoveryService
{
    /// <summary>多久没有心跳就判定执行者已经不在（心跳间隔的 5 倍）。</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    /// <summary>写进回收行 <see cref="AgentRun.Error"/> 的说明。</summary>
    public const string OrphanedRunError =
        "The run stopped reporting progress (the process running it most likely restarted) and was marked as failed. It can be resumed.";

    private const int MaxBatchSize = 100;

    private readonly IRepository<AgentRun, Guid> _repository;
    private readonly ILogger<AgentRunRecoveryService> _logger;

    public AgentRunRecoveryService(IRepository<AgentRun, Guid> repository, ILogger<AgentRunRecoveryService> logger)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 扫描一次，把心跳停了超过 <see cref="StaleAfter"/> 的在跑运行标为 Failed。
    /// </summary>
    /// <returns>本次回收的运行数</returns>
    public async Task<int> RecoverOrphanedRunsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - StaleAfter;

        // 先取一批 Id（最旧的先处理），再按同一判据做条件更新：两步之间运行收尾或心跳刷新了，就不会被误标
        var staleIds = await StaleRuns(cutoff)
            .OrderBy(r => r.CreationTime)
            .Select(r => r.Id)
            .Take(MaxBatchSize)
            .ToListAsync(cancellationToken);

        if (staleIds.Count == 0)
        {
            return 0;
        }

        var recovered = await StaleRuns(cutoff)
            .Where(r => staleIds.Contains(r.Id))
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Status, AgentRunStatus.Failed)
                .SetProperty(r => r.Error, OrphanedRunError)
                .SetProperty(r => r.LastHeartbeatAt, now)
                .SetProperty(r => r.LastModificationTime, now), cancellationToken);

        if (recovered > 0)
        {
            _logger.LogWarning(
                "Marked {Count} agent run(s) as failed: no heartbeat for more than {StaleAfter} (the process running them most likely restarted). Run ids: {RunIds}",
                recovered, StaleAfter, string.Join(", ", staleIds));
        }

        return recovered;
    }

    private IQueryable<AgentRun> StaleRuns(DateTime cutoff)
        => _repository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(r => !r.IsDeleted
                        && r.WorkflowExecutionId == null
                        && (r.Status == AgentRunStatus.Pending || r.Status == AgentRunStatus.Running)
                        && (r.LastHeartbeatAt ?? r.CreationTime) < cutoff);
}
