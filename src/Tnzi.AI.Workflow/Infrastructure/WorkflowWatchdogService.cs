namespace Tnzi.AI.Workflow.Infrastructure;

/// <summary>
/// 工作流 Watchdog 服务 - 扫描长时间停留在 Running/等待状态的执行实例并将其标记为超时失败
/// </summary>
/// <remarks>
/// <para>
/// 默认由 <see cref="WorkflowWatchdogHostedService"/> 的后台循环驱动；关掉
/// <c>UseBuiltInScheduler</c> 后宿主可通过 Hangfire / 定时器等方式按需调用 <see cref="ScanAsync"/>。
/// </para>
/// <para>
/// ★ 扫描**跨全部租户**。调用方是后台作用域，没有当前租户；<see cref="WorkflowExecution"/> 是
/// <c>IMultiTenant</c>，多租户开启时全局过滤器在这种作用域里退化成 <c>TenantId IS NULL</c>，
/// 于是每个租户的超时执行都对扫描不可见、永远停在 Running，而日志报着健康的「0/0」。
/// 这里在读取与更新期间临时禁用 <see cref="IMultiTenantFilter"/>（同 Channels 的
/// <c>DefaultSessionBinder</c> 加载绑定规则的做法）：标记超时是按行的幂等写，行的 TenantId 原样保留，
/// 不存在跨租户泄漏面。没有过滤器管理器（多租户未加载）时行为逐字不变。
/// </para>
/// </remarks>
public class WorkflowWatchdogService
{
    private readonly IRepository<WorkflowExecution, Guid> _repository;
    private readonly ILogger<WorkflowWatchdogService> _logger;
    private readonly WorkflowWatchdogOptions _options;
    private readonly IDataFilterManager? _dataFilterManager;

    public WorkflowWatchdogService(
        IRepository<WorkflowExecution, Guid> repository,
        ILogger<WorkflowWatchdogService> logger,
        IOptionsSnapshot<WorkflowWatchdogOptions> options,
        IDataFilterManager? dataFilterManager = null)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options).Value;
        _dataFilterManager = dataFilterManager;
    }

    /// <summary>
    /// 扫描并处理超时的工作流执行实例。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>本次扫描标记为超时的执行实例数量</returns>
    public async Task<int> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var runningCutoff = now - _options.RunningTimeout;
        var waitingCutoff = now - _options.WaitingTimeout;

        // 跨租户读写：见类注释。using 结束即恢复本作用域的过滤器状态。
        using var tenantFilterScope = _dataFilterManager?.Disable<IMultiTenantFilter>();

        // 查询超时的 Running 和等待状态执行实例。
        // ★ Take 必须在查询侧：此前是先 ToListAsync 拉回全部超时行、再在内存里 Take，
        // 于是 MaxBatchSize 限制的是"处理多少条"而不是"拉回多少条" —— 积压一多，
        // 每次扫描都要把整个积压读进内存，而这正是它本该防住的情形。
        // 最旧的先处理（稳定顺序 + 逐次排空积压）。
        var batch = await _repository.AsQueryable()
            .Where(e =>
                (e.Status == WorkflowExecutionStatus.Running && e.UpdatedTime < runningCutoff) ||
                ((e.Status == WorkflowExecutionStatus.AwaitingApproval || e.Status == WorkflowExecutionStatus.AwaitingInput)
                 && e.UpdatedTime < waitingCutoff))
            .OrderBy(e => e.UpdatedTime)
            .Take(_options.MaxBatchSize)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            return 0;
        }

        var markedCount = 0;
        foreach (var execution in batch)
        {
            try
            {
                var previousStatus = execution.Status;
                var lastActiveTime = execution.UpdatedTime; // capture BEFORE overwriting
                execution.Status = WorkflowExecutionStatus.Failed;
                execution.CurrentWaitReason = "timed_out";
                execution.CompletedTime = now;
                execution.UpdatedTime = now;

                await _repository.UpdateAsync(execution, cancellationToken);
                markedCount++;

                _logger.LogWarning(
                    "Workflow execution '{ExecutionId}' timed out in status {PreviousStatus} (last active: {LastActive:O}); marked as Failed.",
                    execution.ExecutionId,
                    previousStatus,
                    lastActiveTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to mark workflow execution '{ExecutionId}' as timed out.",
                    execution.ExecutionId);
            }
        }

        _logger.LogInformation(
            "Workflow watchdog scan complete: {Count}/{Total} execution(s) marked as timed out.",
            markedCount,
            batch.Count);

        return markedCount;
    }
}
