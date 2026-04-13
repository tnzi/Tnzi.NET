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
    private readonly ILogger<SubAgentExecutionService> _logger;

    public SubAgentExecutionService(
        IAgentResolver agentResolver,
        IRunStore runStore,
        ISubAgentRegistry subAgentRegistry,
        IAgentExecutionContextAccessor executionContextAccessor,
        IServiceScopeFactory scopeFactory,
        ILogger<SubAgentExecutionService> logger,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _agentResolver = Check.NotNull(agentResolver);
        _runStore = Check.NotNull(runStore);
        _subAgentRegistry = Check.NotNull(subAgentRegistry);
        _executionContextAccessor = Check.NotNull(executionContextAccessor);
        _scopeFactory = Check.NotNull(scopeFactory);
        _logger = Check.NotNull(logger);
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

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runtime = scope.ServiceProvider.GetRequiredService<IAgentRuntime>();
                await runtime.RunAsync(runtimeRequest, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background agent run failed: RunId={RunId}", run.Id);
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
        var rootRunId = parentRunId;

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
