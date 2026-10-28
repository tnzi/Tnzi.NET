namespace Tnzi.AI.Infrastructure;

public class RunTracker : IRunTracker
{
    /// <summary>运行摘要保留的字符数（列宽更大，摘要只给列表看个大概）。</summary>
    internal const int InputSummaryMaxLength = 500;

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
            InputSummary = SummarizeInput(request.UserMessage),
            RequestSnapshot = AgentRunRequestSnapshot.From(request).Serialize(),
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
            ? SummarizeInput(request.UserMessage)
            : run.InputSummary;
        // 只补不覆盖：建行方可能已经记了快照；SpawnAsync 建的行在这里首次拿到快照（它建行时还没有 run.Id）
        run.RequestSnapshot ??= AgentRunRequestSnapshot.From(request).Serialize();
        run.ParentRunId ??= request.ParentRunId;
        run.RootRunId ??= request.RootRunId ?? request.ParentRunId ?? run.Id;
        run.Status = AgentRunStatus.Running;
        run.LastHeartbeatAt = DateTime.UtcNow;
        await _runStore.UpdateAsync(run, ct);
        return run;
    }

    /// <summary>
    /// 把一段用户文本截成运行摘要。建行时取请求原文；管道里有人改写过本轮输入时，
    /// 运行时在收尾前用改写后的文本重算一遍（见 <c>AgentRuntime.ApplyEffectiveInputSummary</c>）。
    /// </summary>
    internal static string SummarizeInput(string? text) => StringTruncator.Truncate(text, InputSummaryMaxLength);

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

    /// <summary>
    /// 别的作用域（<c>kill_agent</c> / 管理端 cancel）已经把这一行写成 Cancelled 了吗。
    /// </summary>
    /// <remarks>
    /// 取消先写行、再触发取消令牌；运行若恰好在两步之间跑完（或运行不在取消注册表里、令牌根本不会被触发），
    /// 收尾时手里那份实体还是 Running，照常写入就把 Cancelled 覆盖成 Completed / Failed，
    /// 调用方被告知「已取消」，行却说跑完了。取消与收尾赛跑时以取消为准。
    /// </remarks>
    private async Task<bool> IsCancelledInStoreAsync(AgentRun run, CancellationToken ct)
    {
        try
        {
            return await _runStore.GetStatusAsync(run.Id, ct) == AgentRunStatus.Cancelled;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 读不到就按原样收尾：这一步只为不覆盖别人的取消，不该让收尾本身失败
            _logger.LogWarning(ex, "Failed to read the stored status of Run {RunId} before finalizing it", run.Id);
            return false;
        }
    }

    public async Task UpdateRunOnCompletionAsync(AgentRun run, AgentRunResult result, long durationMs, CancellationToken ct)
    {
        // HistoryMiddleware 在 Run 记录建好之后才建线程：不回填，ResumeAsync 会给续跑的请求一个空 ThreadId，
        // 续跑在一条没有历史的新线程上进行
        run.ThreadId ??= result.ThreadId;
        if (await IsCancelledInStoreAsync(run, ct))
        {
            run.Status = AgentRunStatus.Cancelled;
        }
        else
        {
            run.Status = result.Status!.Value;
            run.Error = run.Status == AgentRunStatus.Failed ? result.Response : null;
        }
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
        if (await IsCancelledInStoreAsync(run, ct))
        {
            run.Status = AgentRunStatus.Cancelled;
        }
        else
        {
            run.Status = AgentRunStatus.Failed;
            run.Error = StringTruncator.Truncate(ex?.Message, 1000);
        }
        run.DurationMs = durationMs;
        run.LastHeartbeatAt = DateTime.UtcNow;
        await _runStore.UpdateAsync(run, ct);

        await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.Error,
            new { error = run.Error, type = ex?.GetType().Name },
            durationMs, ct);
    }

    public async Task UpdateRunOnCancelledAsync(AgentRun run, string reason, long durationMs, CancellationToken ct)
    {
        run.Status = AgentRunStatus.Cancelled;
        run.Error = StringTruncator.Truncate(reason, 1000);
        run.DurationMs = durationMs;
        run.LastHeartbeatAt = DateTime.UtcNow;
        await _runStore.UpdateAsync(run, ct);

        await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.RunCancelled,
            new { reason = run.Error }, durationMs, ct);
    }

    public async Task FinalizeStreamingCompletedAsync(AgentRun run, AgentRunResult streamResult,
        int totalInputTokens, int totalOutputTokens, long durationMs, CancellationToken ct)
    {
        run.DurationMs = durationMs;
        run.TotalInputTokens = totalInputTokens;
        run.TotalOutputTokens = totalOutputTokens;
        run.LastHeartbeatAt = DateTime.UtcNow;
        run.ThreadId ??= streamResult.ThreadId;
        if (await IsCancelledInStoreAsync(run, ct))
        {
            run.Status = AgentRunStatus.Cancelled;
        }
        else
        {
            run.Status = streamResult.Status!.Value;
            run.Error = run.Status == AgentRunStatus.Failed ? streamResult.Response : null;
        }
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
        if (await IsCancelledInStoreAsync(run, ct))
        {
            run.Status = AgentRunStatus.Cancelled;
        }
        else
        {
            run.Status = AgentRunStatus.Failed;
            run.Error = "Streaming execution failed";
        }
        await _runStore.UpdateAsync(run, ct);

        await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.Error,
            new { error = run.Error, finishReason = lastFinishReason },
            durationMs, ct);
    }
}
