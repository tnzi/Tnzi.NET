namespace Tnzi.AI.Services;

/// <summary>
/// 子 Agent / 后台 AgentRun 启动服务
/// </summary>
public class SubAgentExecutionService : ApplicationService, ISubAgentExecutionService
{
    private readonly IAgentResolver _agentResolver;
    private readonly IRunStore _runStore;
    private readonly ISubAgentRegistry _subAgentRegistry;
    private readonly IAgentExecutionContextAccessor _executionContextAccessor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<SubAgentOptions> _subAgentOptions;
    private readonly ISubAgentRunCancellationRegistry? _cancellationRegistry;
    private readonly ILogger<SubAgentExecutionService> _logger;

    public SubAgentExecutionService(
        IAgentResolver agentResolver,
        IRunStore runStore,
        ISubAgentRegistry subAgentRegistry,
        IAgentExecutionContextAccessor executionContextAccessor,
        IServiceScopeFactory scopeFactory,
        ILogger<SubAgentExecutionService> logger,
        IServiceProvider serviceProvider,
        IOptionsMonitor<SubAgentOptions> subAgentOptions,
        ISubAgentRunCancellationRegistry? cancellationRegistry = null)
        : base(serviceProvider)
    {
        _agentResolver = Check.NotNull(agentResolver);
        _runStore = Check.NotNull(runStore);
        _subAgentRegistry = Check.NotNull(subAgentRegistry);
        _executionContextAccessor = Check.NotNull(executionContextAccessor);
        _scopeFactory = Check.NotNull(scopeFactory);
        _logger = Check.NotNull(logger);
        _subAgentOptions = Check.NotNull(subAgentOptions);
        _cancellationRegistry = cancellationRegistry;
    }

    public async Task<Result<AgentRunControlStateDto>> SpawnAsync(SpawnAgentRunInput input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        Check.NotNullOrWhiteSpace(input.Message);

        // 管理端可热改的总开关：关掉就是关掉，不是「保存 200 然后照常起后台运行」
        if (!_subAgentOptions.CurrentValue.Enabled)
        {
            _logger.LogWarning("Sub-agent spawn rejected: AI:SubAgent:Enabled is false");
            return Result.Failure<AgentRunControlStateDto>(
                "Sub-agents are disabled (AI:SubAgent:Enabled = false).", 403, ErrorCodes.SubAgentsDisabled);
        }

        var buildResult = await BuildRequestAsync(input, cancellationToken);
        if (!buildResult.Succeeded || buildResult.Data == null)
        {
            return Result.Failure<AgentRunControlStateDto>(
                buildResult.Message ?? "Failed to build background run request",
                buildResult.Code ?? 500,
                buildResult.ErrorCode ?? ErrorCodes.AgentRunFailed);
        }

        var prepared = buildResult.Data;

        // Enforce depth and descendant caps before creating the run
        var capsResult = await CheckSpawnCapsAsync(prepared.ParentRunId, prepared.RootRunId, prepared.Request.UserId, cancellationToken);
        if (!capsResult.Succeeded)
        {
            return Result.Failure<AgentRunControlStateDto>(
                capsResult.Message ?? "Sub-agent spawn rejected",
                capsResult.Code ?? 429,
                capsResult.ErrorCode ?? ErrorCodes.AgentRunFailed);
        }

        // 在调用方作用域快照租户：HttpContext 派生的租户随父请求结束一起消失（holder 被置空），
        // 后台作用域里的 ICurrentTenant 会读到 null，多租户下子运行写的每一行都落成 TenantId=null、
        // 后续按租户过滤的查询取不到行。与 Channels 的 ChangeTenantScope 同形；单租户下 tenantId 为 null 不切换。
        var tenantId = ServiceProvider.GetService<ICurrentTenant>()?.Id;

        // 归属人显式写进 CreatorId（审计钩子只在为 null 时才用环境用户填）：spawn_agent 在父运行的
        // 作用域里跑，环境里未必有当前用户；而 task 工具组按 CreatorId 认「谁的运行」，漏写 = 无主 = 谁都动得了。
        // 请求快照由 RunTracker.GetOrCreateRunAsync 在后台运行起步时按同一个 runtimeRequest 写入
        // （它需要 run.Id 才能建，这里还没有）；续跑按快照重建，模板 spawn 的运行才不会续跑成无工具的默认 agent。
        var run = await CreateRunDetachedAsync(new AgentRun
        {
            AgentId = prepared.AgentId,
            ThreadId = prepared.ThreadId,
            WorkflowDefinitionId = prepared.WorkflowId,
            Status = AgentRunStatus.Pending,
            ExecutionMode = prepared.ExecutionMode,
            InputSummary = prepared.InputSummary,
            ParentRunId = prepared.ParentRunId,
            RootRunId = prepared.RootRunId,
            CreatorId = prepared.Request.UserId,
            LastHeartbeatAt = DateTime.UtcNow
        }, tenantId, cancellationToken);

        var runtimeRequest = new AgentRunRequest
        {
            OperationType = prepared.Request.OperationType,
            AgentId = prepared.Request.AgentId,
            Provider = prepared.Request.Provider,
            Model = prepared.Request.Model,
            UserMessage = prepared.Request.UserMessage,
            ContentParts = prepared.Request.ContentParts,
            ThreadId = prepared.Request.ThreadId,
            ToolGroups = prepared.Request.ToolGroups,
            TrustedToolSelection = prepared.Request.TrustedToolSelection,
            WorkflowId = prepared.Request.WorkflowId,
            WorkflowInputs = prepared.Request.WorkflowInputs,
            EnableRunTracking = true,
            ExistingRunId = run.Id,
            IsBackground = true,
            ParentRunId = prepared.ParentRunId,
            RootRunId = prepared.RootRunId ?? run.Id,
            // 后台运行在新作用域、新执行流里跑，拿不到调用方的属性包，
            // 子 Agent 名字只能随请求一起传（AgentRuntime 据此打上 IsSubAgent / SubAgentName）
            SubAgentName = input.SubAgentType,
            SubAgentInstructions = prepared.Request.SubAgentInstructions,
            MaxToolIterations = prepared.Request.MaxToolIterations,
            ExcludedToolGroups = prepared.Request.ExcludedToolGroups,
            UserId = prepared.Request.UserId,
            ReasoningEffort = prepared.Request.ReasoningEffort,
            Attachments = prepared.Request.Attachments,
            Metadata = prepared.Request.Metadata,
            PlanMode = prepared.Request.PlanMode,
            StreamMode = prepared.Request.StreamMode
        };

        // kill_agent 经注册表触发 cts；TimeoutSeconds 也经注册表到点取消（同一个 cts，但注册表记下的是「超时」而不是「被 kill」，
        // AgentRuntime 据此把超时收尾成 Failed、kill 收尾成 Cancelled）—— 没有超时的后台运行会一直跑到进程结束。
        // 没有注册表（消费方拆掉了它）时退回裸 CancelAfter：那种部署本来也 kill 不了。
        var timeout = TimeSpan.FromSeconds(_subAgentOptions.CurrentValue.TimeoutSeconds);
        var cts = new CancellationTokenSource();
        if (_cancellationRegistry != null)
        {
            _cancellationRegistry.Register(run.Id, cts, timeout);
        }
        else
        {
            cts.CancelAfter(timeout);
        }

        _ = Task.Run(async () =>
        {
            var startedAt = DateTime.UtcNow;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                using var tenantScope = ChangeTenantScope(scope.ServiceProvider, tenantId);
                var runtime = scope.ServiceProvider.GetRequiredService<IAgentRuntime>();
                await runtime.RunAsync(runtimeRequest, cts.Token);
            }
            catch (OperationCanceledException) when (DateTime.UtcNow - startedAt >= timeout)
            {
                _logger.LogWarning("Background agent run timed out after {TimeoutSeconds}s and was cancelled: RunId={RunId}",
                    timeout.TotalSeconds, run.Id);
                await CloseAbandonedRunAsync(run.Id, tenantId, AgentRunStatus.Failed,
                    $"The run timed out after {timeout.TotalSeconds:0} seconds.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Background agent run was cancelled: RunId={RunId}", run.Id);
                await CloseAbandonedRunAsync(run.Id, tenantId, AgentRunStatus.Cancelled, "The run was cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background agent run failed: RunId={RunId}", run.Id);
                await CloseAbandonedRunAsync(run.Id, tenantId, AgentRunStatus.Failed, ex.Message);
            }
            finally
            {
                var registeredCts = _cancellationRegistry?.Unregister(run.Id);
                registeredCts?.Dispose();
                cts.Dispose();
            }
        }, CancellationToken.None);

        // 发布子 Agent 启动事件
        try
        {
            if (EventBus != null)
            {
                await EventBus.PublishAsync(new SubAgentSpawnedEvent
                {
                    ParentRunId = prepared.ParentRunId,
                    ChildRunId = run.Id,
                    SubAgentType = input.SubAgentType,
                    AgentId = run.AgentId,
                    ThreadId = run.ThreadId
                }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish SubAgentSpawnedEvent for RunId={RunId}", run.Id);
        }

        return Result.Success(new AgentRunControlStateDto
        {
            RunId = run.Id,
            AgentId = run.AgentId,
            ThreadId = run.ThreadId,
            WorkflowDefinitionId = run.WorkflowDefinitionId,
            ExecutionMode = run.ExecutionMode,
            Status = run.Status,
            InputSummary = run.InputSummary,
            CanCancel = true,
            CreationTime = run.CreationTime,
            LastModificationTime = run.LastModificationTime
        });
    }

    /// <summary>
    /// 在独立作用域里建运行行并<b>立刻提交</b>，再启动后台任务。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 运行行绝不能落在调用方的环境事务里。宿主开着 <c>EnableGlobalUnitOfWork</c> 时，请求的事务要到 action 结束才提交，
    /// 而 spawn_agent 在一轮聊天里被调用，那一轮（和它的事务）要等整个 LLM 回合跑完；后台任务却立刻在另一个作用域、
    /// 另一条连接上按 <c>ExistingRunId</c> 找这一行，找不到（未提交的行在别的连接上不可见）就抛出、被吞成一条日志，
    /// 子运行从不开始，请求提交后这一行永远停在 Pending 并占着并发上限。
    /// </para>
    /// <para>
    /// 语义上这也是对的：后台运行的生命周期长于发起它的请求，这一行在不在不该由请求的事务决定；
    /// 调用方请求事后回滚，已经起跑的子运行也照样存在。与 <c>AgentDispatchFacade.EnqueueDetachedAsync</c> 同形。
    /// </para>
    /// </remarks>
    private async Task<AgentRun> CreateRunDetachedAsync(AgentRun run, Guid? tenantId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        using var tenantScope = ChangeTenantScope(scope.ServiceProvider, tenantId);
        var runStore = scope.ServiceProvider.GetRequiredService<IRunStore>();
        return await runStore.CreateAsync(run, cancellationToken);
    }

    /// <summary>
    /// 后台运行异常结束时，把仍停在 Pending / Running 的运行行收尾。
    /// </summary>
    /// <remarks>
    /// 运行在建立追踪之前就失败（Agent 解析失败、找不到运行行、起步前就被取消或超时）时，没有任何东西会写它的终态：
    /// 行一直停在 Pending，get_agent_run / wait_agent 一直报「在跑」，并发上限也一直把它算进去，直到孤儿回收按心跳超时收掉。
    /// 这里在执行者自己知道它已经结束的那一刻就收尾。条件更新：运行时自己已写下的终态与暂停态（等待审批 / 澄清）一律不动。
    /// </remarks>
    private async Task CloseAbandonedRunAsync(Guid runId, Guid? tenantId, AgentRunStatus status, string error)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            using var tenantScope = ChangeTenantScope(scope.ServiceProvider, tenantId);
            var runStore = scope.ServiceProvider.GetRequiredService<IRunStore>();
            if (await runStore.CloseIfStillActiveAsync(runId, status, error, CancellationToken.None))
            {
                _logger.LogWarning("Background agent run {RunId} ended before its runtime recorded a final state; marked {Status}", runId, status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not close background agent run {RunId}; the orphan sweep will mark it once its heartbeat goes stale", runId);
        }
    }

    /// <summary>注册表是进程级单例、按租户分桶；调用方作用域的租户决定查哪个桶</summary>
    private string CurrentTenantKey => SubAgentTenantKey.From(ServiceProvider.GetService<ICurrentTenant>()?.Id);

    private static IDisposable? ChangeTenantScope(IServiceProvider scopedProvider, Guid? tenantId)
        => tenantId.HasValue
            ? scopedProvider.GetService<ICurrentTenant>()?.Change(tenantId)
            : null;

    /// <summary>
    /// 检查深度、后代数量与并发上限。
    /// 深度：通过 ParentRunId 链向上遍历（最多 MaxDepth + 1 步，避免无界 DB 遍历）。
    /// 后代：直接统计 RootRunId 下已有的 Run 数量。
    /// 并发（树内 spawn）：统计 RootRunId 下仍在跑（Pending / Running）的后代数量，达到 MaxConcurrentSubAgents 即拒绝。
    /// 并发（顶层 spawn）：没有父运行的 spawn 自己就是一棵新树的根，树内计数恒为 0 —— 管理端 spawn 端点与
    /// 未开运行追踪的聊天（<c>ChatService</c> 不设 EnableRunTracking）里的 <c>spawn_agent</c> 走的全是这条路，
    /// 上限此前在这里一次都不会触发。改按归属人统计仍在跑的根运行数（每棵树算一个，树内展开由树内那条兜住）。
    /// </summary>
    private async Task<Result> CheckSpawnCapsAsync(Guid? parentRunId, Guid? rootRunId, Guid? ownerUserId, CancellationToken ct)
    {
        var options = _subAgentOptions.CurrentValue;

        if (!parentRunId.HasValue)
        {
            var activeRoots = await _runStore.CountActiveRootRunsByOwnerAsync(ownerUserId, ct);
            if (activeRoots >= options.MaxConcurrentSubAgents)
            {
                _logger.LogWarning("Sub-agent spawn rejected: owner {OwnerUserId} has {Count} root run(s) still running >= MaxConcurrentSubAgents {Max}",
                    ownerUserId, activeRoots, options.MaxConcurrentSubAgents);
                return Result.Failure(
                    $"Sub-agent spawn rejected: {activeRoots} background runs are still running for this user (limit: {options.MaxConcurrentSubAgents}).",
                    429, ErrorCodes.SubAgentLimitExceeded);
            }
        }

        // Depth check: walk up the parent chain from parentRunId
        // We need to count hops: depth of NEW run = depth(parentRun) + 1
        // A root run (no parent) has depth 1. Its direct child has depth 2, etc.
        if (parentRunId.HasValue)
        {
            var depth = 1; // the parent itself is at least depth 1
            var currentId = parentRunId;
            var maxWalk = options.MaxDepth + 1; // bounded walk - never more than MaxDepth+1 steps

            for (var i = 0; i < maxWalk && currentId.HasValue; i++)
            {
                var pid = await _runStore.GetParentRunIdAsync(currentId.Value, ct);
                if (pid == null)
                    break; // reached root
                depth++;
                currentId = pid;
            }

            // The new child will be at depth + 1
            if (depth + 1 > options.MaxDepth)
            {
                _logger.LogWarning("Sub-agent spawn rejected: depth {Depth} exceeds MaxDepth {Max}", depth + 1, options.MaxDepth);
                return Result.Failure(
                    $"Sub-agent spawn rejected: maximum nesting depth of {options.MaxDepth} would be exceeded.",
                    429, ErrorCodes.AgentRunFailed);
            }
        }

        // Descendant count check: count existing descendants under rootRunId
        var effectiveRoot = rootRunId ?? parentRunId;
        if (effectiveRoot.HasValue)
        {
            var descendantCount = await _runStore.CountDescendantsAsync(effectiveRoot.Value, ct);
            if (descendantCount >= options.MaxDescendantsPerRoot)
            {
                _logger.LogWarning("Sub-agent spawn rejected: descendant count {Count} >= MaxDescendantsPerRoot {Max}", descendantCount, options.MaxDescendantsPerRoot);
                return Result.Failure(
                    $"Sub-agent spawn rejected: the root run already has {descendantCount} descendants (limit: {options.MaxDescendantsPerRoot}).",
                    429, ErrorCodes.AgentRunFailed);
            }

            var activeCount = await _runStore.CountActiveDescendantsAsync(effectiveRoot.Value, ct);
            if (activeCount >= options.MaxConcurrentSubAgents)
            {
                _logger.LogWarning("Sub-agent spawn rejected: active descendant count {Count} >= MaxConcurrentSubAgents {Max}", activeCount, options.MaxConcurrentSubAgents);
                return Result.Failure(
                    $"Sub-agent spawn rejected: {activeCount} sub-agents are still running under this root (limit: {options.MaxConcurrentSubAgents}).",
                    429, ErrorCodes.SubAgentLimitExceeded);
            }
        }

        return Result.Success();
    }

    private async Task<Result<SpawnPreparation>> BuildRequestAsync(SpawnAgentRunInput input, CancellationToken cancellationToken)
    {
        var toolGroups = input.ToolGroups;
        var provider = input.Provider;
        var model = input.Model;
        string? templateInstructions = null;
        int? maxToolIterations = null;
        List<string>? excludedToolGroups = null;

        if (!string.IsNullOrWhiteSpace(input.SubAgentType))
        {
            var definition = _subAgentRegistry.GetForTenant(input.SubAgentType, CurrentTenantKey);
            if (definition == null)
            {
                return Result.Failure<SpawnPreparation>("Sub-agent type not found", 404, ErrorCodes.AgentNotFound);
            }

            // 模板的每个字段都在这里定下并随请求走（续跑按快照取回，不回查注册表）：
            // 系统指令只给没有自己 Agent 的模板运行，已定义的 Agent 用自己的指令；
            // 上限与排除的工具组对两种 spawn 都生效，排除的组也从请求的工具组里拿掉（管理端 spawn 自带的组同样过这一道）。
            // DefaultApprovalMode 刻意不在这里套用，见 docs/modules/ai.md 的 AI_SubAgentType。
            toolGroups ??= definition.ToolGroups.ToList();
            model ??= definition.DefaultModel;
            templateInstructions = input.AgentId.HasValue || string.IsNullOrWhiteSpace(definition.Instructions)
                ? null
                : definition.Instructions;
            maxToolIterations = definition.MaxTurns > 0 ? definition.MaxTurns : null;
            if (definition.ExcludedToolGroups.Count > 0)
            {
                excludedToolGroups = definition.ExcludedToolGroups.ToList();
                toolGroups = toolGroups
                    .Where(g => !excludedToolGroups.Contains(g, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        var resolution = await _agentResolver.ResolveAgentAsync(
            input.AgentId,
            provider,
            model,
            toolGroups,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return Result.Failure<SpawnPreparation>(
                "Failed to resolve background agent",
                resolution.ErrorCode == ErrorCodes.AgentNotFound ? 404 : 400,
                resolution.ErrorCode ?? ErrorCodes.AgentRunFailed);
        }

        var parentRunId = TryGetCurrentRunId();

        // 发起人：正在执行的父请求的用户（spawn_agent 工具）> 环境用户（管理端 spawn 端点的调用者本人）。
        // 后台运行的配额计量、权限检查、权限规则评估、线程归属以及 task 工具组的归属判定全按它认人，
        // 所以它绝不取自输入：管理端 spawn 的输入就是 HTTP 请求体。
        var ownerUserId = _executionContextAccessor.CurrentRequest?.UserId ?? CurrentUser?.Id;

        // Inherit the true root from the parent run so that MaxDescendantsPerRoot
        // bounds the whole tree rather than just per-immediate-parent fan-out.
        // If the parent has no RootRunId yet (it is the root itself), fall back to parentRunId.
        Guid? rootRunId = null;
        if (parentRunId.HasValue)
        {
            var parentRun = await _runStore.GetAsync(parentRunId.Value, cancellationToken);
            rootRunId = parentRun?.RootRunId ?? parentRunId;
        }

        return Result.Success(new SpawnPreparation
        {
            AgentId = input.AgentId ?? resolution.AgentId,
            ThreadId = input.ThreadId,
            WorkflowId = null,
            ExecutionMode = resolution.ExecutionMode,
            InputSummary = input.Message.Truncate(500),
            ParentRunId = parentRunId,
            RootRunId = rootRunId,
            Request = new AgentRunRequest
            {
                OperationType = AIOperationType.AgentRun,
                AgentId = input.AgentId ?? resolution.AgentId,
                Provider = provider ?? resolution.Provider,
                Model = model ?? resolution.Model,
                UserMessage = input.Message,
                ThreadId = input.ThreadId,
                ToolGroups = toolGroups,
                // 后台运行的工具组来自子 Agent 模板（管理员定义）或管理端 spawn 端点（ai.agentRun.execute 门控），
                // 不是匿名可达的 HTTP 请求体；spawn_agent 工具本身不接受工具组参数
                TrustedToolSelection = true,
                SubAgentInstructions = templateInstructions,
                MaxToolIterations = maxToolIterations,
                ExcludedToolGroups = excludedToolGroups,
                EnableRunTracking = true,
                ParentRunId = parentRunId,
                RootRunId = rootRunId,
                UserId = ownerUserId,
                Metadata = input.Metadata
            }
        });
    }

    private Guid? TryGetCurrentRunId()
    {
        if (_executionContextAccessor.Properties.TryGetValue(ContextPropertyKeys.CurrentRunId, out var value)
            && value is Guid runId)
        {
            return runId;
        }

        return null;
    }

    private sealed class SpawnPreparation
    {
        public Guid? AgentId { get; init; }
        public Guid? ThreadId { get; init; }
        public Guid? WorkflowId { get; init; }
        public AgentExecutionMode ExecutionMode { get; init; }
        public string InputSummary { get; init; } = string.Empty;
        public Guid? ParentRunId { get; init; }
        public Guid? RootRunId { get; init; }
        public AgentRunRequest Request { get; init; } = null!;
    }
}
