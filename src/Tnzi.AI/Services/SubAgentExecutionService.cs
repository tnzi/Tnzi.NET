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
        var capsResult = await CheckSpawnCapsAsync(prepared.ParentRunId, prepared.RootRunId, cancellationToken);
        if (!capsResult.Succeeded)
        {
            return Result.Failure<AgentRunControlStateDto>(
                capsResult.Message ?? "Sub-agent spawn rejected",
                capsResult.Code ?? 429,
                capsResult.ErrorCode ?? ErrorCodes.AgentRunFailed);
        }

        var run = await _runStore.CreateAsync(new AgentRun
        {
            AgentId = prepared.AgentId,
            ThreadId = prepared.ThreadId,
            WorkflowDefinitionId = prepared.WorkflowId,
            Status = AgentRunStatus.Pending,
            ExecutionMode = prepared.ExecutionMode,
            InputSummary = prepared.InputSummary,
            ParentRunId = prepared.ParentRunId,
            RootRunId = prepared.RootRunId,
            LastHeartbeatAt = DateTime.UtcNow
        }, cancellationToken);

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
            WorkflowId = prepared.Request.WorkflowId,
            WorkflowInputs = prepared.Request.WorkflowInputs,
            EnableRunTracking = true,
            ExistingRunId = run.Id,
            ParentRunId = prepared.ParentRunId,
            RootRunId = prepared.RootRunId ?? run.Id,
            UserId = prepared.Request.UserId,
            ReasoningEffort = prepared.Request.ReasoningEffort,
            Attachments = prepared.Request.Attachments,
            Metadata = prepared.Request.Metadata,
            PlanMode = prepared.Request.PlanMode,
            StreamMode = prepared.Request.StreamMode
        };

        var cts = new CancellationTokenSource();
        _cancellationRegistry?.Register(run.Id, cts);

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runtime = scope.ServiceProvider.GetRequiredService<IAgentRuntime>();
                await runtime.RunAsync(runtimeRequest, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Background agent run was cancelled: RunId={RunId}", run.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background agent run failed: RunId={RunId}", run.Id);
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
    /// 检查深度和后代数量上限。
    /// 深度：通过 ParentRunId 链向上遍历（最多 MaxDepth + 1 步，避免无界 DB 遍历）。
    /// 后代：直接统计 RootRunId 下已有的 Run 数量。
    /// </summary>
    private async Task<Result> CheckSpawnCapsAsync(Guid? parentRunId, Guid? rootRunId, CancellationToken ct)
    {
        var options = _subAgentOptions.CurrentValue;

        // Depth check: walk up the parent chain from parentRunId
        // We need to count hops: depth of NEW run = depth(parentRun) + 1
        // A root run (no parent) has depth 1. Its direct child has depth 2, etc.
        if (parentRunId.HasValue)
        {
            var depth = 1; // the parent itself is at least depth 1
            var currentId = parentRunId;
            var maxWalk = options.MaxDepth + 1; // bounded walk — never more than MaxDepth+1 steps

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
        }

        return Result.Success();
    }

    private async Task<Result<SpawnPreparation>> BuildRequestAsync(SpawnAgentRunInput input, CancellationToken cancellationToken)
    {
        var toolGroups = input.ToolGroups;
        var provider = input.Provider;
        var model = input.Model;

        if (!string.IsNullOrWhiteSpace(input.SubAgentType))
        {
            var definition = _subAgentRegistry.Get(input.SubAgentType);
            if (definition == null)
            {
                return Result.Failure<SpawnPreparation>("Sub-agent type not found", 404, ErrorCodes.AgentNotFound);
            }

            toolGroups ??= definition.ToolGroups.ToList();
            model ??= definition.DefaultModel;
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
            InputSummary = input.Message.Length <= 500 ? input.Message : input.Message[..500] + "...",
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
                EnableRunTracking = true,
                ParentRunId = parentRunId,
                RootRunId = rootRunId,
                UserId = input.UserId,
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
