namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 运行进入终态的唯一写入口：以「这条运行还没结束」为条件写下终态，完成这次转换的一方发布 <see cref="CliRunFinishedEvent"/>。
/// </summary>
/// <remarks>
/// <para>
/// 终态有五个写入者：执行器的正常收尾、失败、用户取消；队列处理器的执行异常兜底，以及取消之后宿主死掉的那条运行的租约回收；
/// 调度器取消一条还在排队的运行。它们分处三个类、两种作用域（后台与请求），可能落在不同宿主上，也会为同一条运行赛跑
/// （租约过期之后别处接手又各自收尾、执行器写完终态之后抛异常落进兜底）。所以「结束了没有」只能由数据库回答：
/// 写入一律带 <c>Status ∈ {Queued, Dispatched, Running}</c> 的条件（外加各写入者自己的前置条件），受影响行数为 1 才算这次转换发生，
/// 只有它发布事件。输掉的一方什么都不写：终态一旦写下就不再改写（先到者为准），收到「结束」通知的消费方回读时看到的永远是同一个结局。
/// </para>
/// <para>
/// 一条语句写完全部终态列：门面与 SSE 看到终态的那一刻，交付物与错误信息已经在同一行上，不会读到「已结束但还没有输出」的中间态。
/// 按列写而不是整实体回写：别的写入者此刻写下的事实（取消标记、续租）不会被调用方加载时的旧值盖掉。
/// </para>
/// <para>
/// 发布在写入之后。后台作用域没有工作单元事务，写入即提交；请求作用域里有活跃事务时写入先加入事务，
/// 事务感知发布把事件推迟到提交之后（回滚则一起丢弃）。两种情况下处理器回读都看到终态。事件只走本地总线：
/// 写入提交之后、发布之前进程死掉，这一次的事件就没有了。发布失败只记日志不抛：终态已经落库，
/// 让收尾路径因为通知没发出去而失败（执行器会因此跳过工作区清理与配额结算）比少一条通知更糟。
/// </para>
/// <para>
/// 全部用 <see cref="CancellationToken.None"/>：运行已经停了，无论为什么停都必须记下来，带着已取消的令牌去写只会当场失败。
/// </para>
/// </remarks>
internal sealed class CliRunFinalizer
{
    private readonly IRepository<CliRun, Guid> _repository;
    private readonly IEventBus? _eventBus;
    private readonly ILogger _logger;
    private readonly IRepository<CliRunPermissionRequest, Guid>? _permissionRepository;

    /// <param name="repository">调用方作用域里的运行仓储。</param>
    /// <param name="eventBus">事件总线；为 null（直接构造的测试）时只写终态不发布。</param>
    /// <param name="logger">调用方的日志器。</param>
    /// <param name="permissionRepository">
    /// 权限请求仓储：终态写下之后，还没写给 CLI 的权限请求一律以 <c>Cancelled</c> 结束（宿主死掉之后的租约回收、兜底失败，
    /// 执行器没机会收它们）。为 null（直接构造的测试）时跳过。
    /// </param>
    public CliRunFinalizer(
        IRepository<CliRun, Guid> repository, IEventBus? eventBus, ILogger logger,
        IRepository<CliRunPermissionRequest, Guid>? permissionRepository = null)
    {
        _repository = Check.NotNull(repository);
        _eventBus = eventBus;
        _logger = Check.NotNull(logger);
        _permissionRepository = permissionRepository;
    }

    /// <summary>
    /// 把一条运行写成终态。完成了这次转换返回 true；运行不存在、已经结束、或前置条件不再成立时什么都不写，返回 false。
    /// </summary>
    /// <param name="runId">运行。</param>
    /// <param name="termination">要写下的终态。</param>
    /// <param name="precondition">写入者自己的前置条件，与「还没结束」一起在同一条语句里判定。</param>
    public async Task<bool> FinishAsync(
        Guid runId, CliRunTermination termination, Expression<Func<CliRun, bool>>? precondition = null)
    {
        Check.NotNull(termination);

        // 整行读出来而不是投影：事件要带运行的租户，而多租户关闭时 TenantId 不在模型里，按它投影的查询翻译不了。
        var run = await _repository.AcrossTenants().FirstOrDefaultAsync(r => r.Id == runId, CancellationToken.None);
        if (run is null || IsTerminal(run.Status))
        {
            return false;
        }

        var query = _repository.AcrossTenants()
            .Where(r => r.Id == runId
                        && (r.Status == CliRunStatus.Queued
                            || r.Status == CliRunStatus.Dispatched
                            || r.Status == CliRunStatus.Running));
        if (precondition is not null)
        {
            query = query.Where(precondition);
        }

        var now = DateTime.UtcNow;
        var error = termination.Error;
        var completion = termination.Completion;

        await _repository.EnsureTransactionStartedAsync(CancellationToken.None);
        var written = await query.ExecuteUpdateAsync(s =>
        {
            s.SetProperty(r => r.Status, termination.Status)
                .SetProperty(r => r.FailureReason, termination.FailureReason)
                .SetProperty(r => r.Output, termination.Output)
                .SetProperty(r => r.CompletedAt, (DateTime?)now)
                .SetProperty(r => r.LastModificationTime, (DateTime?)now)
                .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null)
                // 终态的运行没有进程可以接回来
                .SetProperty(r => r.ReattachHandle, (string?)null)
                .SetProperty(r => r.DetachedAt, (DateTime?)null)
                // 运行到达终态，回写凭据立刻作废：凭据校验已按状态挡掉，清掉哈希是让终态记录里不留任何凭据材料
                .SetProperty(r => r.WriteBackTokenHash, (string?)null)
                .SetProperty(r => r.WriteBackTokenExpiresAt, (DateTime?)null)
                .SetProperty(r => r.UsageLimitResetsAt, termination.UsageLimitResetsAt);

            if (termination.KeepExistingError)
            {
                s.SetProperty(r => r.Error, r => r.Error ?? error);
            }
            else
            {
                s.SetProperty(r => r.Error, error);
            }

            if (termination.DurationMs is { } durationMs)
            {
                s.SetProperty(r => r.DurationMs, durationMs);
            }

            if (completion is not null)
            {
                s.SetProperty(r => r.MessageCount, completion.MessageCount)
                    .SetProperty(r => r.ProviderSessionId, completion.ProviderSessionId)
                    .SetProperty(r => r.ProviderSessionEntryId, completion.ProviderSessionEntryId)
                    .SetProperty(r => r.AssistantMessageId, completion.AssistantMessageId);

                if (completion.UsageJson is not null)
                {
                    s.SetProperty(r => r.UsageJson, completion.UsageJson)
                        .SetProperty(r => r.EstimatedCostUsd, completion.EstimatedCostUsd);
                }

                if (completion.SessionUsageJson is { } sessionUsageJson)
                {
                    s.SetProperty(r => r.SessionUsageJson, sessionUsageJson);
                }
            }
        }, CancellationToken.None);

        if (written == 0)
        {
            return false;
        }

        await CloseOpenPermissionRequestsAsync(runId, now);
        await PublishAsync(run, termination);
        return true;
    }

    /// <summary>
    /// 运行结束了，还没写给 CLI 的权限请求不会再有人写：以 <c>Cancelled</c> 结束，回答接口对它们答 409 而不是假装收下。
    /// </summary>
    /// <remarks>失败只记日志：终态已经落库，回答接口本就按运行是否结束拒绝回答。</remarks>
    private async Task CloseOpenPermissionRequestsAsync(Guid runId, DateTime now)
    {
        if (_permissionRepository is null)
        {
            return;
        }

        try
        {
            await _permissionRepository.EnsureTransactionStartedAsync(CancellationToken.None);
            await _permissionRepository.AsQueryable()
                .IgnoreQueryFilters()
                .Where(r => r.RunId == runId && r.ResolvedSequence == null && r.Status != CliPermissionStatus.Cancelled)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, CliPermissionStatus.Cancelled)
                    .SetProperty(r => r.AnsweredAt, r => r.AnsweredAt ?? now), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CLI run {RunId} finished, but its open permission requests could not be closed", runId);
        }
    }

    private async Task PublishAsync(CliRun run, CliRunTermination termination)
    {
        if (_eventBus is null)
        {
            return;
        }

        var finished = new CliRunFinishedEvent
        {
            // 显式写运行的租户：队列处理器的回收与兜底跑在没有租户的作用域里，靠环境捕获只会得到 null
            TenantId = run.TenantId,
            RunId = run.Id,
            AgentRunId = run.AgentRunId,
            AgentId = run.AgentId,
            ThreadId = run.ThreadId,
            UserId = run.CreatorId,
            Status = termination.Status,
            FailureReason = termination.FailureReason,
            Error = termination.KeepExistingError ? run.Error ?? termination.Error : termination.Error,
            Output = termination.Output,
            DurationMs = termination.DurationMs ?? run.DurationMs,
            UserMessageId = run.UserMessageId,
            AssistantMessageId = termination.Completion?.AssistantMessageId,
            UsageLimitResetsAt = termination.UsageLimitResetsAt
        };

        try
        {
            await _eventBus.PublishAsync(finished, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CLI run {RunId} finished with status {Status}, but the finish event could not be published", run.Id, termination.Status);
        }
    }

    private static bool IsTerminal(CliRunStatus status)
        => status is CliRunStatus.Completed or CliRunStatus.Failed
            or CliRunStatus.Cancelled or CliRunStatus.TimedOut;
}

/// <summary>
/// 一次终态转换要写下的内容。
/// </summary>
/// <param name="Status">终态，必须是 Completed / Failed / Cancelled / TimedOut 之一。</param>
/// <param name="FailureReason">失败分类；Completed 为 null。</param>
/// <param name="Error">错误信息。</param>
internal sealed record CliRunTermination(CliRunStatus Status, CliRunFailureReason? FailureReason, string? Error)
{
    /// <summary>
    /// 只在这条运行还没有错误信息时写 <see cref="CliRunTermination.Error"/>：取消落在一条已经记下原因的运行上时，保留那个原因。
    /// </summary>
    public bool KeepExistingError { get; init; }

    /// <summary>交付物；只有 Completed 才有，其余终态写 null。</summary>
    public string? Output { get; init; }

    /// <summary>执行耗时；null = 不改这一列。</summary>
    public long? DurationMs { get; init; }

    /// <summary>正常收尾才有的那几列；null = 不改。</summary>
    public CliRunCompletion? Completion { get; init; }

    /// <summary>
    /// 以 <see cref="CliRunFailureReason.UsageLimit"/>（CLI 背后账号的上限）或 <see cref="CliRunFailureReason.UserUsageLimit"/>
    /// （框架给用户设的上限）结束时，上限的重置时间（UTC）；其余终态为 null。
    /// </summary>
    public DateTime? UsageLimitResetsAt { get; init; }
}

/// <summary>
/// 跑完一次进程才有的终态列。
/// </summary>
/// <param name="MessageCount">最后一条事件的序号。</param>
/// <param name="ProviderSessionId">下一轮续接用的会话指针；续接被拒时为 null（清掉失效的指针）。</param>
/// <param name="UsageJson">这一轮按模型分组的用量；null = 没有用量，用量与成本两列都不改。</param>
/// <param name="EstimatedCostUsd">这一轮的成本。</param>
internal sealed record CliRunCompletion(int MessageCount, string? ProviderSessionId, string? UsageJson, decimal? EstimatedCostUsd)
{
    /// <summary>这次运行在 provider 会话里留下的最后一个条目（以后截断续接用）；续接被拒时为 null。</summary>
    public string? ProviderSessionEntryId { get; init; }

    /// <summary>写进线程的回复消息；没写时为 null。</summary>
    public Guid? AssistantMessageId { get; init; }

    /// <summary>这一轮结束时的会话累计用量（下一轮续接时的基线）；null = 不改这一列。</summary>
    public string? SessionUsageJson { get; init; }
}
