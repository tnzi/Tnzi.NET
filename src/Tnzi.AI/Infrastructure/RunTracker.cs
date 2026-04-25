namespace Tnzi.AI.Infrastructure;

public class RunTracker : IRunTracker
{
    private readonly IRunStore _runStore;
    private readonly ITraceStore _traceStore;
    private readonly ILogger<RunTracker> _logger;

    public RunTracker(IRunStore runStore, ITraceStore traceStore, ILogger<RunTracker> logger)
    {
        _runStore = Check.NotNull(runStore);
        _traceStore = Check.NotNull(traceStore);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRun> CreateRunAsync(AgentRunRequest request, AgentResolution resolution, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var run = new AgentRun
        {
            AgentId = request.AgentId ?? resolution.AgentId,
            ThreadId = request.ThreadId,
            WorkflowDefinitionId = request.WorkflowId,
            Status = AgentRunStatus.Running,
            ExecutionMode = resolution.ExecutionMode,
            InputSummary = StringTruncator.Truncate(request.UserMessage, 500),
            ParentRunId = request.ParentRunId,
            RootRunId = request.RootRunId ?? request.ParentRunId,
            LastHeartbeatAt = now
        };

        await _runStore.CreateAsync(run, ct);
        return run;
    }

    public async Task<AgentRun> GetOrCreateRunAsync(AgentRunRequest request, AgentResolution resolution, CancellationToken ct)
    {
        if (!request.ExistingRunId.HasValue)
            return await CreateRunAsync(request, resolution, ct);

        var run = await _runStore.GetAsync(request.ExistingRunId.Value, ct)
            ?? throw new BusinessException("Tracked run not found", ErrorCodes.RunNotFound, 404);

        run.AgentId ??= request.AgentId ?? resolution.AgentId;
        run.ThreadId ??= request.ThreadId;
        run.WorkflowDefinitionId ??= request.WorkflowId;
        run.ExecutionMode = resolution.ExecutionMode;
        run.InputSummary = string.IsNullOrWhiteSpace(run.InputSummary)
            ? StringTruncator.Truncate(request.UserMessage, 500)
            : run.InputSummary;
        run.ParentRunId ??= request.ParentRunId;
        run.RootRunId ??= request.RootRunId ?? request.ParentRunId ?? run.Id;
        run.Status = AgentRunStatus.Running;
        run.LastHeartbeatAt = DateTime.UtcNow;
        await _runStore.UpdateAsync(run, ct);
        return run;
    }

    public Task<AgentRun?> GetWithNodesAsync(Guid runId, CancellationToken ct)
        => _runStore.GetWithNodesAsync(runId, ct);

    public Task UpdateAsync(AgentRun run, CancellationToken ct)
        => _runStore.UpdateAsync(run, ct);

    public Task UpdateNodeAsync(AgentRunNode node, CancellationToken ct)
        => _runStore.UpdateNodeAsync(node, ct);

    public async Task RecordTraceAsync(Guid runId, Guid? nodeId, string eventType, object? eventData, long durationMs, CancellationToken ct)
    {
        try
        {
            var trace = new AgentRunTrace
            {
                RunId = runId,
                NodeId = nodeId,
                EventType = eventType,
                EventData = eventData?.ToJsonString(camelCase: true),
                DurationMs = durationMs
            };
            await _traceStore.AddAsync(trace, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record trace for Run {RunId}", runId);
        }
    }

    public async Task UpdateRunOnCompletionAsync(AgentRun run, AgentRunResult result, long durationMs, CancellationToken ct)
    {
        run.Status = result.Status!.Value;
        run.Error = run.Status == AgentRunStatus.Failed ? result.Response : null;
        run.OutputSummary = StringTruncator.Truncate(result.Response, 500);
        run.DurationMs = durationMs;
        if (result.Usage != null)
        {
            run.TotalInputTokens = result.Usage.InputTokens;
            run.TotalOutputTokens = result.Usage.OutputTokens;
        }
        run.LastHeartbeatAt = DateTime.UtcNow;

        await _runStore.UpdateAsync(run, ct);
        await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.RunCompleted,
            result, durationMs, ct);
    }

    public async Task UpdateRunOnFailureAsync(AgentRun run, Exception? ex, long durationMs, CancellationToken ct)
    {
        run.Status = AgentRunStatus.Failed;
        run.Error = StringTruncator.Truncate(ex?.Message, 1000);
        run.DurationMs = durationMs;
        run.LastHeartbeatAt = DateTime.UtcNow;
        await _runStore.UpdateAsync(run, ct);

        await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.Error,
            new { error = run.Error, type = ex?.GetType().Name },
            durationMs, ct);
    }

    public async Task FinalizeStreamingCompletedAsync(AgentRun run, AgentRunResult streamResult,
        int totalInputTokens, int totalOutputTokens, long durationMs, CancellationToken ct)
    {
        run.DurationMs = durationMs;
        run.TotalInputTokens = totalInputTokens;
        run.TotalOutputTokens = totalOutputTokens;
        run.LastHeartbeatAt = DateTime.UtcNow;
        run.Status = streamResult.Status!.Value;
        run.Error = run.Status == AgentRunStatus.Failed ? streamResult.Response : null;
        run.OutputSummary = StringTruncator.Truncate(streamResult.Response, 500);
        await _runStore.UpdateAsync(run, ct);

        await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.StreamCompleted,
            new { finishReason = streamResult.FinishReason, inputTokens = totalInputTokens, outputTokens = totalOutputTokens },
            durationMs, ct);
    }

    public async Task FinalizeStreamingCancelledAsync(AgentRun run, string? lastFinishReason, long durationMs, CancellationToken ct)
    {
        run.DurationMs = durationMs;
        run.LastHeartbeatAt = DateTime.UtcNow;
        run.Status = AgentRunStatus.Cancelled;
        run.Error = "Streaming was cancelled by the caller";
        await _runStore.UpdateAsync(run, ct);

        await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.StreamCancelled,
            new { finishReason = lastFinishReason },
            durationMs, ct);
    }

    public async Task FinalizeStreamingFailedAsync(AgentRun run, string? lastFinishReason, long durationMs, CancellationToken ct)
    {
        run.DurationMs = durationMs;
        run.LastHeartbeatAt = DateTime.UtcNow;
        run.Status = AgentRunStatus.Failed;
        run.Error = "Streaming execution failed";
        await _runStore.UpdateAsync(run, ct);

        await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.Error,
            new { error = run.Error, finishReason = lastFinishReason },
            durationMs, ct);
    }
}
