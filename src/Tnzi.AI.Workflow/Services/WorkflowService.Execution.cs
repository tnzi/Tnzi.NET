namespace Tnzi.AI.Workflow.Services;

/// <summary>
/// 工作流服务 - 执行驱动 (RunAsync / RunStreamingAsync) + 执行驱动相关私有助手。
/// HITL 控制（Resume/Approve/Reject/Cancel）见 WorkflowService.Hitl.cs；
/// 执行查询投影（GetExecution*/反序列化助手）见 WorkflowService.Queries.cs。
/// </summary>
public partial class WorkflowService
{
    public async Task<Result<WorkflowExecutionResultDto>> RunAsync(Guid workflowId, string input, Guid? userId = null, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        const string provider = "Workflow";
        const string fallbackErrorType = "workflow_unsuccessful";
        var workflowDef = await _repository.GetAsync(workflowId, ct);
        if (workflowDef == null) return Fail<WorkflowExecutionResultDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);
        if (!workflowDef.IsEnabled) return Fail<WorkflowExecutionResultDto>("Workflow is disabled", 400, ErrorCodes.WorkflowDisabled);

        // HITL guard: approval/interrupt-bearing nodes can only pause/resume in DAG mode
        // (checkpoint store wiring). In Sequential/Parallel mode such a node would set
        // AwaitingApproval=true and break out of the engine loop with no checkpoint to resume
        // from - a silent hang. Fail fast instead of hanging.
        var hitlGuard = EnsureHitlNodesRunInDagMode<WorkflowExecutionResultDto>(workflowDef);
        if (hitlGuard != null) return hitlGuard;

        string? executionId = null;

        QuotaReservation? reservation = null;
        if (userId.HasValue)
        {
            var estimatedTokens = EstimateWorkflowTokens(workflowDef.Steps, input);
            var reserveResult = await _quotaService.ReserveQuotaAsync(userId.Value, estimatedTokens, ct);
            if (!reserveResult.Succeeded)
            {
                return Fail<WorkflowExecutionResultDto>(
                    reserveResult.Message ?? "Quota reservation failed",
                    reserveResult.Code ?? 500,
                    reserveResult.Code == 429 ? ErrorCodes.QuotaExceeded : ErrorCodes.QuotaCheckFailed);
            }
            reservation = reserveResult.Data;
        }

        try
        {
            WorkflowExecutionResultDto resultDto;
            int actualInputTokens = 0, actualOutputTokens = 0;
            WorkflowEngineResult engineResult;
            WorkflowExecutionOptions options;

            // All execution modes create a WorkflowExecution row for observability
            // (watchdog / GetExecutions / GetExecutionStats). Only DAG mode wires a
            // CheckpointStore (durable HITL pause/resume); Sequential/Parallel record
            // terminal status only (Running → Completed/Failed, no resume semantics).
            executionId = await EnsureWorkflowExecutionAsync(workflowDef.Id, input, ct);
            options = workflowDef.ExecutionMode == WorkflowExecutionMode.Dag
                ? new WorkflowExecutionOptions
                {
                    ExecutionId = executionId,
                    WorkflowDefinitionId = workflowDef.Id,
                    CheckpointStore = _checkpointStore
                }
                : new WorkflowExecutionOptions
                {
                    ExecutionId = executionId,
                    WorkflowDefinitionId = workflowDef.Id
                };

            var graph = BuildWorkflowGraph(workflowDef);
            engineResult = await _workflowEngine.ExecuteAsync(graph, input, Check.NotNull(ServiceProvider), options, ct);

            if (engineResult.Usage != null)
            {
                actualInputTokens = engineResult.Usage.InputTokens;
                actualOutputTokens = engineResult.Usage.OutputTokens;
            }

            resultDto = new WorkflowExecutionResultDto
            {
                ExecutionId = executionId,
                RunId = engineResult.RunId,
                Output = engineResult.FinalOutput,
                Status = engineResult.StatusText,
                StepResults = engineResult.StepResults.Count > 0 ? engineResult.StepResults : null
            };

            // 更新执行状态（包括耗时计算）
            var executionStatus = MapExecutionStatus(engineResult.StatusText);
            await TryUpdateWorkflowExecutionStatusAsync(executionId, executionStatus, CancellationToken.None);

            var actualTotalTokens = actualInputTokens + actualOutputTokens;
            var isSuccess = IsSuccessfulWorkflowStatus(executionStatus);
            var errorMessage = isSuccess ? null : BuildWorkflowFailureMessage(engineResult.StatusText, engineResult.FinalOutput);
            await _usageLogService.LogUsageAsync(
                AIOperationType.WorkflowRun,
                provider,
                workflowDef.Name,
                actualInputTokens,
                actualOutputTokens,
                stopwatch.ElapsedMilliseconds,
                isSuccess,
                errorMessage,
                ct: ct);
            RecordWorkflowTelemetry(AIOperationType.WorkflowRun, workflowDef.Name, actualInputTokens, actualOutputTokens, stopwatch.Elapsed, isSuccess, isSuccess ? null : engineResult.StatusText ?? fallbackErrorType);

            // 结算配额（使用实际 token 用量）
            if (userId.HasValue && reservation != null)
            {
                try { await _quotaService.SettleQuotaAsync(userId.Value, reservation, actualTotalTokens, ct); }
                catch (Exception settleEx) { Logger.LogError(settleEx, "Failed to settle workflow quota: WorkflowId={WorkflowId}", workflowId); }
            }

            return Ok(resultDto);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(executionId))
            {
                await TryUpdateWorkflowExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, CancellationToken.None);
            }

            Logger.LogError(ex, "Workflow execution failed: {WorkflowId}", workflowId);
            await _usageLogService.LogUsageAsync(AIOperationType.WorkflowRun, provider, workflowDef.Name, 0, 0, stopwatch.ElapsedMilliseconds, false, ex.Message, ct: ct);
            RecordWorkflowTelemetry(AIOperationType.WorkflowRun, workflowDef.Name, 0, 0, stopwatch.Elapsed, false, ex.GetType().Name);

            // 释放预留配额（错误时按 0 结算，退回预留）
            if (userId.HasValue && reservation != null)
            {
                try { await _quotaService.SettleQuotaAsync(userId.Value, reservation, 0, CancellationToken.None); }
                catch (Exception settleEx) { Logger.LogError(settleEx, "Failed to release workflow quota reservation"); }
            }

            return Fail<WorkflowExecutionResultDto>("Workflow execution failed.", 500, ErrorCodes.WorkflowFailed);
        }
    }

    /// <summary>
    /// 流式运行工作流。
    /// 当前统一由 WorkflowEngine 驱动，输出节点完成事件和最终汇总事件。
    /// token 级流式尚未接入 WorkflowEngine，因此 Sequential/Parallel/DAG 目前都是步骤级流式。
    /// </summary>
    public async IAsyncEnumerable<WorkflowExecutionResultDto> RunStreamingAsync(Guid workflowId, string input, Guid? userId = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        const string provider = "Workflow";
        var workflowDef = await _repository.GetAsync(workflowId, ct);
        if (workflowDef == null)
            throw new BusinessException("Workflow not found", ErrorCodes.WorkflowNotFound, 404);
        if (!workflowDef.IsEnabled)
            throw new BusinessException("Workflow is disabled", ErrorCodes.WorkflowDisabled, 400);

        // HITL guard (see RunAsync): approval/interrupt nodes require DAG execution mode.
        if (HasHitlNode(workflowDef))
            throw new BusinessException("HITL nodes require DAG execution mode", ErrorCodes.WorkflowExecutionInvalidState, 400);

        // 配额预留
        QuotaReservation? reservation = null;
        if (userId.HasValue)
        {
            var estimatedTokens = EstimateWorkflowTokens(workflowDef.Steps, input);
            var reserveResult = await _quotaService.ReserveQuotaAsync(userId.Value, estimatedTokens, ct);
            if (!reserveResult.Succeeded)
            {
                throw new BusinessException(
                    reserveResult.Message ?? "Quota reservation failed",
                    reserveResult.Code == 429 ? ErrorCodes.QuotaExceeded : ErrorCodes.QuotaCheckFailed,
                    reserveResult.Code ?? 500);
            }
            reservation = reserveResult.Data;
        }

        int actualInputTokens = 0, actualOutputTokens = 0;
        bool streamCompleted = false;
        var finalOutput = input;
        string? finalStatusOverride = null;
        string? executionId = null;
        var aggregatedStepResults = new List<WorkflowStepResultDto>();

        try
        {
            // All execution modes create a WorkflowExecution row for observability; only
            // DAG mode wires a CheckpointStore for durable HITL pause/resume.
            executionId = await EnsureWorkflowExecutionAsync(workflowDef.Id, input, ct);
            var options = workflowDef.ExecutionMode == WorkflowExecutionMode.Dag
                ? new WorkflowExecutionOptions
                {
                    ExecutionId = executionId,
                    WorkflowDefinitionId = workflowDef.Id,
                    CheckpointStore = _checkpointStore
                }
                : new WorkflowExecutionOptions
                {
                    ExecutionId = executionId,
                    WorkflowDefinitionId = workflowDef.Id
                };

            var graph = BuildWorkflowGraph(workflowDef);
            var engineResult = await _workflowEngine.ExecuteAsync(graph, input, Check.NotNull(ServiceProvider), options, ct);
            AggregateUsage(engineResult.Usage, ref actualInputTokens, ref actualOutputTokens);
            aggregatedStepResults.AddRange(engineResult.StepResults);
            finalOutput = engineResult.FinalOutput;
            finalStatusOverride = engineResult.StatusText;

            foreach (var stepResult in engineResult.StepResults)
            {
                yield return new WorkflowExecutionResultDto
                {
                    ExecutionId = executionId,
                    RunId = engineResult.RunId,
                    Output = stepResult.Output,
                    Status = $"Step '{stepResult.StepId}'" + (stepResult.Skipped ? " (skipped)" : string.Empty),
                    StepResults = [stepResult]
                };
            }

            yield return new WorkflowExecutionResultDto
            {
                ExecutionId = executionId,
                RunId = engineResult.RunId,
                Output = finalOutput,
                Status = finalStatusOverride ?? "Completed",
                StepResults = aggregatedStepResults.Count > 0 ? aggregatedStepResults : null
            };

            // 更新执行状态（包括耗时计算）
            await TryUpdateWorkflowExecutionStatusAsync(executionId, MapExecutionStatus(finalStatusOverride), CancellationToken.None);

            streamCompleted = true;
        }
        finally
        {
            // 使用 CancellationToken.None 防止客户端断连导致配额泄漏和数据丢失
            var actualTotalTokens = actualInputTokens + actualOutputTokens;

            try
            {
                var executionStatus = MapExecutionStatus(finalStatusOverride);
                var isSuccess = streamCompleted && IsSuccessfulWorkflowStatus(executionStatus);
                var errorMessage = isSuccess
                    ? null
                    : streamCompleted
                        ? BuildWorkflowFailureMessage(finalStatusOverride, finalOutput)
                        : "Workflow stream interrupted by exception";

                await _usageLogService.LogUsageAsync(
                    AIOperationType.WorkflowRunStreaming, provider, workflowDef.Name,
                    actualInputTokens, actualOutputTokens, stopwatch.ElapsedMilliseconds,
                    isSuccess,
                    errorMessage: errorMessage,
                    ct: CancellationToken.None);

                RecordWorkflowTelemetry(
                    AIOperationType.WorkflowRunStreaming,
                    workflowDef.Name,
                    actualInputTokens,
                    actualOutputTokens,
                    stopwatch.Elapsed,
                    isSuccess,
                    isSuccess ? null : streamCompleted ? finalStatusOverride ?? "workflow_unsuccessful" : "workflow_stream_interrupted");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to log workflow streaming usage: WorkflowId={WorkflowId}", workflowId);
            }

            if (userId.HasValue && reservation != null)
            {
                try
                {
                    // 失败时按 0 结算（退回预留），成功时按实际用量结算
                    var settleTokens = streamCompleted ? actualTotalTokens : 0;
                    await _quotaService.SettleQuotaAsync(userId.Value, reservation, settleTokens, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to settle workflow streaming quota: WorkflowId={WorkflowId}", workflowId);
                }
            }

            if (!streamCompleted && !string.IsNullOrWhiteSpace(executionId))
            {
                await TryUpdateWorkflowExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// HITL guard: when a non-DAG (Sequential/Parallel) workflow declares any node with
    /// human-approval / interrupt semantics, return a failed Result. Returns <c>null</c>
    /// when the workflow is allowed to run. DAG mode is always allowed.
    /// </summary>
    private static Result<T>? EnsureHitlNodesRunInDagMode<T>(WorkflowDefinition workflowDef)
    {
        return HasHitlNode(workflowDef)
            ? Result<T>.Failure("HITL nodes require DAG execution mode", 400, ErrorCodes.WorkflowExecutionInvalidState)
            : null;
    }

    /// <summary>
    /// Detects whether the workflow contains any node that needs human approval / interrupt
    /// semantics outside DAG mode. Mirrors the engine's RequiresHumanApproval check:
    /// either <see cref="WorkflowStepDto.RequiresApproval"/> is set or the node type is "approval".
    /// DAG mode wires a checkpoint store so such nodes can pause/resume; other modes cannot.
    /// </summary>
    private static bool HasHitlNode(WorkflowDefinition workflowDef)
    {
        if (workflowDef.ExecutionMode == WorkflowExecutionMode.Dag)
            return false;

        if (string.IsNullOrWhiteSpace(workflowDef.Steps))
            return false;

        List<WorkflowStepDto>? steps;
        try
        {
            steps = JsonSerializer.Deserialize<List<WorkflowStepDto>>(workflowDef.Steps, TnziJsonDefaults.Options);
        }
        catch
        {
            // Malformed JSON is caught later during graph build; do not block here.
            return false;
        }

        if (steps == null) return false;

        foreach (var step in steps)
        {
            if (step.RequiresApproval) return true;

            if (step.Configuration != null
                && step.Configuration.TryGetValue("nodeType", out var nodeType)
                && string.Equals(nodeType, WorkflowNodeTypes.Approval, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AggregateUsage(TokenUsageDto? usage, ref int actualInputTokens, ref int actualOutputTokens)
    {
        if (usage == null) return;
        actualInputTokens += usage.InputTokens;
        actualOutputTokens += usage.OutputTokens;
    }

    private static bool IsSuccessfulWorkflowStatus(WorkflowExecutionStatus status)
    {
        return status == WorkflowExecutionStatus.Completed;
    }

    private static string BuildWorkflowFailureMessage(string? statusText, string? output)
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            return output;
        }

        return string.IsNullOrWhiteSpace(statusText)
            ? "Workflow execution finished unsuccessfully."
            : $"Workflow execution finished unsuccessfully: {statusText}";
    }

    private static void RecordWorkflowTelemetry(
        string operationType,
        string workflowName,
        int inputTokens,
        int outputTokens,
        TimeSpan elapsed,
        bool isSuccess,
        string? errorType)
    {
        AIActivitySource.RecordChatRequest("Workflow", workflowName, operationType);
        AIActivitySource.RecordTokenUsage("Workflow", workflowName, inputTokens, outputTokens, operationType);
        AIActivitySource.RecordChatLatency("Workflow", workflowName, elapsed.TotalSeconds, operationType);

        if (!isSuccess && !string.IsNullOrWhiteSpace(errorType))
        {
            AIActivitySource.RecordError("Workflow", workflowName, errorType);
        }
    }

    private async Task<string> EnsureWorkflowExecutionAsync(Guid workflowDefinitionId, string input, CancellationToken ct)
    {
        var executionId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        await _executionRepository.InsertAsync(new WorkflowExecution
        {
            ExecutionId = executionId,
            WorkflowDefinitionId = workflowDefinitionId,
            InitialInput = input,
            Status = WorkflowExecutionStatus.Running,
            StartedAt = now,
            UpdatedTime = now
        }, ct);

        return executionId;
    }

    private static int EstimateWorkflowTokens(string? steps, string input)
    {
        var stepCount = 1;
        if (!string.IsNullOrWhiteSpace(steps))
        {
            try
            {
                using var doc = JsonDocument.Parse(steps);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    stepCount = Math.Max(1, doc.RootElement.GetArrayLength());
            }
            catch { /* 解析失败时退化为单步估算 */ }
        }
        return (input.Length / 4 + 2000) * stepCount;
    }
}
