namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 外部 agent 调度入口的真实实现。
/// </summary>
public class CliAgentDispatcher : ApplicationService, ICliAgentDispatcher
{
    private readonly IRepository<CliRun, Guid> _runRepository;
    private readonly IRepository<CliRunMessage, Guid> _messageRepository;
    private readonly IRepository<CliAgentBinding, Guid> _bindingRepository;
    private readonly IRepository<Entities.CliRuntime, Guid> _runtimeRepository;
    private readonly CliRunSignalHub _signalHub;
    private readonly CliRunCancellationRegistry _cancellationRegistry;
    private readonly IOptionsMonitor<CliAgentOptions> _options;
    private readonly IBudgetService? _budgetService;

    /// <summary>初始化调度器。</summary>
    public CliAgentDispatcher(
        IRepository<CliRun, Guid> runRepository,
        IRepository<CliRunMessage, Guid> messageRepository,
        IRepository<CliAgentBinding, Guid> bindingRepository,
        IRepository<Entities.CliRuntime, Guid> runtimeRepository,
        CliRunSignalHub signalHub,
        CliRunCancellationRegistry cancellationRegistry,
        IOptionsMonitor<CliAgentOptions> options,
        IServiceProvider serviceProvider,
        IBudgetService? budgetService = null)
        : base(serviceProvider)
    {
        _runRepository = Check.NotNull(runRepository);
        _messageRepository = Check.NotNull(messageRepository);
        _bindingRepository = Check.NotNull(bindingRepository);
        _runtimeRepository = Check.NotNull(runtimeRepository);
        _signalHub = Check.NotNull(signalHub);
        _cancellationRegistry = Check.NotNull(cancellationRegistry);
        _options = Check.NotNull(options);
        _budgetService = budgetService;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> EnqueueAsync(CliRunRequestDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (!_options.CurrentValue.Enabled)
        {
            return Fail<Guid>(
                "External CLI agent execution is disabled (AI:Cli:Enabled=false).", 501, ErrorCodes.CliDisabled);
        }

        var binding = await _bindingRepository.FirstOrDefaultAsync(b => b.AgentId == request.AgentId, cancellationToken);
        if (binding is null)
        {
            return Fail<Guid>(
                "The agent has no external CLI runtime binding.", 404, ErrorCodes.CliBindingNotFound);
        }

        var runtimeExists = await _runtimeRepository.AnyAsync(
            r => r.Id == binding.CliRuntimeId && r.Status != CliRuntimeStatus.Disabled, cancellationToken);
        if (!runtimeExists)
        {
            return Fail<Guid>(
                "The bound external CLI runtime is unavailable.", 409, ErrorCodes.CliRuntimeNotFound);
        }

        // 排队前先问预算：让调用方当场知道，而不是排完队再失败。
        // 这不是唯一的门 —— 队列可能积压很久，真正的执法在认领之后（见 CliRunExecutor）。
        var budget = await CheckBudgetAsync(request.AgentId, cancellationToken);
        if (budget is { IsAllowed: false })
        {
            return Fail<Guid>(
                budget.Reason ?? "The AI cost budget is exhausted.", 402, ErrorCodes.CliBudgetExceeded);
        }

        // 同一 Agent + 同一 Thread 的上一轮会话 ID 是本轮的续接指针。找不到就是新会话。
        // PerRun 每轮换目录，CLI 在新目录里找不到上一轮的会话存档 —— 明知必被拒还发
        // --resume，只会白跑一次重试并让用户为同一个问题付两次钱。
        var canResume = binding.WorkDirectoryMode != CliWorkDirectoryMode.PerRun;

        var previousSessionId = canResume && request.ThreadId is { } threadId
            ? await _runRepository.AsQueryable()
                .Where(r => r.ThreadId == threadId
                            && r.AgentId == request.AgentId
                            && r.ProviderSessionId != null)
                .OrderByDescending(r => r.CreationTime)
                .Select(r => r.ProviderSessionId)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var run = new CliRun
        {
            AgentId = request.AgentId,
            CliRuntimeId = binding.CliRuntimeId,
            AgentRunId = request.AgentRunId,
            ThreadId = request.ThreadId,
            Status = CliRunStatus.Queued,
            Priority = request.Priority,
            Prompt = request.Prompt ?? string.Empty,
            PerTurnContext = request.PerTurnContext,
            ProviderSessionId = previousSessionId,
            ResumeExpected = !string.IsNullOrWhiteSpace(previousSessionId)
        };

        await _runRepository.InsertAsync(run, cancellationToken);
        await _runRepository.SaveChangesAsync(cancellationToken);

        Logger.LogInformation(
            "Enqueued CLI run {RunId} for agent {AgentId} (thread={ThreadId}, resume={Resume})",
            run.Id, request.AgentId, request.ThreadId, run.ResumeExpected);

        return Ok(run.Id);
    }

    /// <summary>
    /// 查预算。返回 null 表示这个部署没有预算服务，也就没有预算可超。
    /// </summary>
    /// <remarks>
    /// 外部执行按红线①不进中间件管线，因此内建路径上那道长在 <c>QuotaMiddleware</c> 里的预算门
    /// 对它不生效，必须在这里显式再设一道。缺了这道门，外部执行域就是绕过预算的免费通道，
    /// 而它恰恰是两条路里更贵的那条。
    /// <para>
    /// 服务缺失时放行：<c>IBudgetService</c> 由 <c>AIModule</c> 注册，正常情况下必然存在；
    /// 真的没有它，就是宿主根本没装预算能力，此时「允许」是正确答案而不是降级。
    /// 预算本身关着（<c>AI:Budget:Enabled=false</c>，默认）时该服务恒返回允许。
    /// </para>
    /// </remarks>
    private async Task<BudgetCheckResult?> CheckBudgetAsync(Guid agentId, CancellationToken cancellationToken)
    {
        if (_budgetService is null) return null;

        var currentUser = CurrentUser;
        var tenantId = ServiceProvider.GetService<ICurrentTenant>()?.Id ?? currentUser?.TenantId;

        return await _budgetService.CheckBudgetAsync(currentUser?.Id, tenantId, agentId, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CliAgentEvent> StreamAsync(
        Guid runId, int fromSequence = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lastSequence = fromSequence;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await _messageRepository.AsQueryable()
                .Where(m => m.RunId == runId && m.Sequence > lastSequence)
                .OrderBy(m => m.Sequence)
                .Take(200)
                .ToListAsync(cancellationToken);

            foreach (var message in batch)
            {
                lastSequence = message.Sequence;
                yield return ToEvent(message);
            }

            // 还有整批要补，先接着补完再谈等待。
            if (batch.Count == 200)
            {
                continue;
            }

            var status = await _runRepository.AsQueryable()
                .Where(r => r.Id == runId)
                .Select(r => (CliRunStatus?)r.Status)
                .FirstOrDefaultAsync(cancellationToken);

            if (status is null)
            {
                yield break;
            }

            if (IsTerminal(status.Value))
            {
                // 终态之后再查一次：最后几条事件可能在上一次查询与状态落库之间写入。
                var tail = await _messageRepository.AsQueryable()
                    .Where(m => m.RunId == runId && m.Sequence > lastSequence)
                    .OrderBy(m => m.Sequence)
                    .ToListAsync(cancellationToken);

                foreach (var message in tail)
                {
                    yield return ToEvent(message);
                }

                yield break;
            }

            // 等信号或等超时都行 —— 信号只是提示，超时后重新查库同样正确
            //（多副本下执行者的信号根本到不了这个进程）。
            await _signalHub.WaitAsync(runId, _options.CurrentValue.PollInterval, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<Result<CliRunDto>> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetAsync(runId, cancellationToken);
        if (run is null)
        {
            return Fail<CliRunDto>("CLI run not found.", 404, ErrorCodes.CliRunNotFound);
        }

        var dto = run.MapTo<CliRunDto>();
        dto.ProviderKey = await _runtimeRepository.AsQueryable()
            .Where(r => r.Id == run.CliRuntimeId)
            .Select(r => r.ProviderKey)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(dto);
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<CliRunDto>>> GetListAsync(
        CliRunQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _runRepository
            .WhereIf(r => r.AgentId == query.AgentId, query.AgentId.HasValue)
            .WhereIf(r => r.CliRuntimeId == query.CliRuntimeId, query.CliRuntimeId.HasValue)
            .WhereIf(r => r.Status == query.Status!.Value, query.Status.HasValue)
            .WhereIf(r => r.ThreadId == query.ThreadId, query.ThreadId.HasValue)
            .WhereIf(r => r.CreationTime >= query.StartTime!.Value, query.StartTime.HasValue)
            .WhereIf(r => r.CreationTime <= query.EndTime!.Value, query.EndTime.HasValue)
            .OrderByDescending(r => r.CreationTime);

        var paged = await queryable.ProjectTo<CliRun, CliRunDto>().CreateAsync(query);
        return Ok(paged);
    }

    /// <inheritdoc />
    public async Task<Result<List<CliRunMessageDto>>> GetMessagesAsync(
        Guid runId, int fromSequence = 0, CancellationToken cancellationToken = default)
    {
        var exists = await _runRepository.AnyAsync(r => r.Id == runId, cancellationToken);
        if (!exists)
        {
            return Fail<List<CliRunMessageDto>>("CLI run not found.", 404, ErrorCodes.CliRunNotFound);
        }

        var messages = await _messageRepository.AsQueryable()
            .Where(m => m.RunId == runId && m.Sequence > fromSequence)
            .OrderBy(m => m.Sequence)
            .ProjectTo<CliRunMessage, CliRunMessageDto>()
            .ToListAsync(cancellationToken);

        return Ok(messages);
    }

    /// <inheritdoc />
    public async Task<Result> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetAsync(runId, cancellationToken);
        if (run is null)
        {
            return Fail("CLI run not found.", 404, ErrorCodes.CliRunNotFound);
        }

        if (IsTerminal(run.Status))
        {
            return Fail("The CLI run has already finished.", 409, ErrorCodes.CliRunInvalidState);
        }

        // 两处都要写：数据库是跨副本的权威事实，进程内登记让<b>本副本正在跑的</b>那条
        // 立刻中止而不是等下一个续期周期 —— 一个正在烧预算的进程，多跑几秒都是钱。
        await _runRepository.AsQueryable()
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true), cancellationToken);

        var cancelledLocally = _cancellationRegistry.TryCancel(runId);

        if (run.Status == CliRunStatus.Queued)
        {
            // 还没被认领，直接判终态，不用等任何宿主来处理。
            await _runRepository.AsQueryable()
                .Where(r => r.Id == runId && r.Status == CliRunStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, CliRunStatus.Cancelled)
                    .SetProperty(r => r.FailureReason, (CliRunFailureReason?)CliRunFailureReason.Cancelled)
                    .SetProperty(r => r.CompletedAt, (DateTime?)DateTime.UtcNow), cancellationToken);
        }

        _signalHub.Signal(runId);

        Logger.LogInformation(
            "Cancellation requested for CLI run {RunId} (cancelled locally: {Local})", runId, cancelledLocally);

        return Ok();
    }

    private static bool IsTerminal(CliRunStatus status)
        => status is CliRunStatus.Completed or CliRunStatus.Failed
            or CliRunStatus.Cancelled or CliRunStatus.TimedOut;

    private static CliAgentEvent ToEvent(CliRunMessage message) => new()
    {
        Type = message.Type,
        Content = message.Content,
        Tool = message.Tool,
        CallId = message.CallId,
        Input = message.InputJson is null
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(message.InputJson, TnziJsonDefaults.Options),
        Output = message.Output,
        Status = message.Status,
        Level = message.Level
    };
}
