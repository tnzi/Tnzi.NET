namespace Tnzi.AI.Services;

/// <summary>
/// Agent 运行管理服务实现
/// </summary>
public class AgentRunService : ApplicationService, IAgentRunService
{
    private readonly IRepository<AgentRun, Guid> _repository;
    private readonly IRepository<AgentRunNode, Guid> _nodeRepository;
    private readonly IAgentRuntime _runtime;

    public AgentRunService(
        IRepository<AgentRun, Guid> repository,
        IRepository<AgentRunNode, Guid> nodeRepository,
        IAgentRuntime runtime,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _nodeRepository = Check.NotNull(nodeRepository);
        _runtime = Check.NotNull(runtime);
    }

    public async Task<Result<AgentRunStatsDto>> GetStatsAsync()
    {
        var totalRuns = await _repository.CountAsync();
        var pendingRuns = await _repository.CountAsync(r => r.Status == AgentRunStatus.Pending);
        var runningRuns = await _repository.CountAsync(r => r.Status == AgentRunStatus.Running);
        var awaitingApprovalRuns = await _repository.CountAsync(r => r.Status == AgentRunStatus.AwaitingApproval);
        var completedRuns = await _repository.CountAsync(r => r.Status == AgentRunStatus.Completed);
        var failedRuns = await _repository.CountAsync(r => r.Status == AgentRunStatus.Failed);
        var cancelledRuns = await _repository.CountAsync(r => r.Status == AgentRunStatus.Cancelled);

        var totalInputTokens = await _repository.SumAsync(r => (decimal)r.TotalInputTokens);
        var totalOutputTokens = await _repository.SumAsync(r => (decimal)r.TotalOutputTokens);

        var completedLikeRuns = completedRuns + failedRuns + cancelledRuns;
        var averageDuration = completedLikeRuns > 0
            ? await _repository.AverageAsync(
                r => (decimal)r.DurationMs,
                r => r.Status == AgentRunStatus.Completed
                    || r.Status == AgentRunStatus.Failed
                    || r.Status == AgentRunStatus.Cancelled)
            : 0m;

        var successRate = completedLikeRuns > 0
            ? decimal.Round((decimal)completedRuns / completedLikeRuns, 4, MidpointRounding.AwayFromZero)
            : 0m;

        return Ok(new AgentRunStatsDto
        {
            TotalRuns = totalRuns,
            PendingRuns = pendingRuns,
            RunningRuns = runningRuns,
            AwaitingApprovalRuns = awaitingApprovalRuns,
            CompletedRuns = completedRuns,
            FailedRuns = failedRuns,
            CancelledRuns = cancelledRuns,
            TotalInputTokens = decimal.ToInt64(totalInputTokens),
            TotalOutputTokens = decimal.ToInt64(totalOutputTokens),
            AverageDurationMs = decimal.ToInt64(decimal.Round(averageDuration, 0, MidpointRounding.AwayFromZero)),
            SuccessRate = successRate,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task<Result<AgentRunDto>> GetAsync(Guid runId)
    {
        var entity = await _repository.GetAsync(runId);
        if (entity == null)
            return Fail<AgentRunDto>("Run not found", 404, ErrorCodes.RunNotFound);

        return Ok(entity.MapTo<AgentRunDto>());
    }

    public async Task<Result<IPagedList<AgentRunDto>>> GetListAsync(AgentRunQueryDto input)
    {
        Check.NotNull(input);

        var queryable = _repository
            .WhereIf(r => r.AgentId == input.AgentId, input.AgentId.HasValue)
            .WhereIf(r => r.Status == input.Status!.Value, input.Status.HasValue)
            .WhereIf(r => r.CreationTime >= input.StartTime!.Value, input.StartTime.HasValue)
            .WhereIf(r => r.CreationTime <= input.EndTime!.Value, input.EndTime.HasValue)
            .OrderByDescending(r => r.CreationTime);

        var pagedList = await queryable.ProjectTo<AgentRun, AgentRunDto>().CreateAsync(input);
        return Ok(pagedList);
    }

    public async Task<Result<List<AgentRunNodeDto>>> GetNodesAsync(Guid runId)
    {
        var runExists = await _repository.AnyAsync(r => r.Id == runId);
        if (!runExists)
            return Fail<List<AgentRunNodeDto>>("Run not found", 404, ErrorCodes.RunNotFound);

        var nodes = await _nodeRepository
            .Where(n => n.RunId == runId)
            .OrderBy(n => n.OrderIndex)
            .ProjectTo<AgentRunNode, AgentRunNodeDto>()
            .ToListAsync();

        return Ok(nodes);
    }

    public async Task<Result<AgentRunNodeDto>> GetNodeAsync(Guid runId, Guid nodeId)
    {
        var node = await _nodeRepository
            .Where(n => n.RunId == runId && n.Id == nodeId)
            .ProjectTo<AgentRunNode, AgentRunNodeDto>()
            .FirstOrDefaultAsync();

        if (node == null)
            return Fail<AgentRunNodeDto>("Node not found", 404, ErrorCodes.NodeNotFound);

        return Ok(node);
    }

    public async Task<Result> CancelAsync(Guid runId)
    {
        var entity = await _repository.GetAsync(runId);
        if (entity == null)
            return Fail("Run not found", 404, ErrorCodes.RunNotFound);

        if (entity.Status is not (AgentRunStatus.Pending or AgentRunStatus.Running or AgentRunStatus.AwaitingApproval))
            return Fail("Run is not in a cancellable state", 400, ErrorCodes.RunInvalidState);

        entity.Status = AgentRunStatus.Cancelled;
        await _repository.UpdateAsync(entity);

        return Ok();
    }

    public async Task<Result> ApproveAsync(Guid runId, string? comment)
    {
        var entity = await _repository.GetAsync(runId);
        if (entity == null)
            return Fail("Run not found", 404, ErrorCodes.RunNotFound);

        if (entity.Status != AgentRunStatus.AwaitingApproval)
            return Fail("Run is not awaiting approval", 400, ErrorCodes.RunInvalidState);

        await _runtime.ResumeAsync(runId, new ResumeRunInput
        {
            ApprovalDecision = "approve",
            ApprovalComment = comment
        });

        Logger.LogInformation("Run {RunId} approved with comment: {Comment}", runId, comment);
        return Ok();
    }

    public async Task<Result> RejectAsync(Guid runId, string? feedback)
    {
        var entity = await _repository.GetAsync(runId);
        if (entity == null)
            return Fail("Run not found", 404, ErrorCodes.RunNotFound);

        if (entity.Status != AgentRunStatus.AwaitingApproval)
            return Fail("Run is not awaiting approval", 400, ErrorCodes.RunInvalidState);

        await _runtime.ResumeAsync(runId, new ResumeRunInput
        {
            ApprovalDecision = "reject",
            ApprovalComment = feedback
        });

        Logger.LogInformation("Run {RunId} rejected with feedback: {Feedback}", runId, feedback);
        return Ok();
    }

    public async Task<Result> RetryNodeAsync(Guid runId, Guid nodeId)
    {
        var entity = await _repository.GetAsync(runId);
        if (entity == null)
            return Fail("Run not found", 404, ErrorCodes.RunNotFound);

        var node = await _nodeRepository.GetAsync(nodeId);

        if (node == null || node.RunId != runId)
            return Fail("Node not found", 404, ErrorCodes.NodeNotFound);

        if (node.Status != AgentRunNodeStatus.Failed)
            return Fail("Node is not in a failed state", 400, ErrorCodes.RunInvalidState);

        if (entity.Status != AgentRunStatus.Failed)
            return Fail("Run must be in failed state before retrying a node", 400, ErrorCodes.RunInvalidState);

        await _runtime.ResumeAsync(runId, new ResumeRunInput
        {
            RetryNodeId = nodeId
        });

        Logger.LogInformation("Node {NodeId} in Run {RunId} retried via runtime resume", nodeId, runId);
        return Ok();
    }
}
