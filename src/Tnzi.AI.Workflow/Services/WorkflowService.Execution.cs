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

        UsageLimitReservation? reservation = null;
        if (userId.HasValue)
        {
            var begun = await _usageLimits.BeginAsync(userId.Value, ct);
            if (!begun.Succeeded)
                return UsageLimitFailure<WorkflowExecutionResultDto>(begun);
            reservation = begun.Data;
        }

        var usage = new WorkflowUsageMeter();
        try
        {
            WorkflowExecutionResultDto resultDto;
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
            engineResult = await _workflowEngine.ExecuteAsync(graph, input, Check.NotNull(ServiceProvider), options, usage, ct);

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

            var isSuccess = IsSuccessfulWorkflowStatus(executionStatus);
            var errorMessage = isSuccess ? null : BuildWorkflowFailureMessage(engineResult.StatusText, engineResult.FinalOutput);
            await AccountWorkflowUsageAsync(AIOperationType.WorkflowRun, workflowDef.Name, userId, reservation, usage,
                stopwatch.Elapsed, isSuccess, errorMessage, engineResult.StatusText ?? WorkflowUnsuccessfulErrorType);

            return Ok(resultDto);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(executionId))
            {
                await TryUpdateWorkflowExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, CancellationToken.None);
            }

            Logger.LogError(ex, "Workflow execution failed: {WorkflowId}", workflowId);

            // Billed for what the finished nodes consumed, not 0: a run aborted by a client
            // disconnect or a storage error has already paid the provider for those nodes.
            await AccountWorkflowUsageAsync(AIOperationType.WorkflowRun, workflowDef.Name, userId, reservation, usage,
                stopwatch.Elapsed, isSuccess: false, ex.Message, ex.GetType().Name);

            return Fail<WorkflowExecutionResultDto>("Workflow execution failed.", 500, ErrorCodes.WorkflowFailed);
        }
    }

    /// <summary>
    /// 流式运行工作流。
    /// 当前统一由 WorkflowEngine 驱动：先发一个开始事件（带 ExecutionId、状态 <c>Running</c>），
    /// 之后每个步骤完成时发一个步骤事件，最后是汇总事件。
    /// token 级流式尚未接入 WorkflowEngine，因此 Sequential/Parallel/DAG 目前都是步骤级流式。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 步骤事件在步骤完成时发出，不是跑完再回放。此前整条工作流跑完才产出第一个事件：长流程在
    /// 反向代理的空闲超时下连响应头都等不到就被切断，请求取消让引擎中止、执行记成 Failed，而调用方
    /// 选流式恰恰是为了长任务。
    /// </para>
    /// <para>
    /// 引擎与消费者轮流走（见 <see cref="StreamingHandOff"/>）：消费者处理一个事件时引擎停在那个步骤后面，
    /// 消费者要下一个事件时引擎才继续。两边共用请求作用域里的 DbContext，这样它永远不会被同时使用。
    /// </para>
    /// <para>
    /// 执行的结局只取决于引擎与取消令牌，不取决于事件有没有送到：消费者提前停止枚举时，释放枚举器会
    /// 等引擎不再与它轮流、自己跑完，执行照样按引擎的结论落库与记账。要中止执行，取消令牌（HTTP 端点里
    /// 就是客户端断开）。
    /// </para>
    /// </remarks>
    public async IAsyncEnumerable<WorkflowExecutionResultDto> RunStreamingAsync(Guid workflowId, string input, Guid? userId = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var workflowDef = await _repository.GetAsync(workflowId, ct);
        if (workflowDef == null)
            throw new BusinessException("Workflow not found", ErrorCodes.WorkflowNotFound, 404);
        if (!workflowDef.IsEnabled)
            throw new BusinessException("Workflow is disabled", ErrorCodes.WorkflowDisabled, 400);

        // HITL guard (see RunAsync): approval/interrupt nodes require DAG execution mode.
        if (HasHitlNode(workflowDef))
            throw new BusinessException("HITL nodes require DAG execution mode", ErrorCodes.WorkflowExecutionInvalidState, 400);

        // 用量上限：开窗口（判上限）
        UsageLimitReservation? reservation = null;
        if (userId.HasValue)
        {
            var begun = await _usageLimits.BeginAsync(userId.Value, ct);
            if (!begun.Succeeded)
            {
                var failure = UsageLimitFailure<WorkflowExecutionResultDto>(begun);
                throw new BusinessException(failure.Message!, failure.ErrorCode ?? ErrorCodes.UsageLimitCheckFailed, failure.Code ?? 500)
                {
                    ErrorDetails = failure.ErrorDetails
                };
            }
            reservation = begun.Data;
        }

        var usage = new WorkflowUsageMeter();
        var engineFinished = false;
        var finalOutput = input;
        string? finalStatusOverride = null;
        string? executionId = null;
        using var handOff = new StreamingHandOff();
        Task<WorkflowEngineResult>? engineTask = null;

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

            // Started now, but it waits for the consumer's first request before doing anything.
            engineTask = RunEngineAsync(graph, options);

            // The run has started: the caller gets its execution id (to follow or cancel it) and the
            // HTTP endpoint commits its headers now, not when the first node finishes.
            yield return new WorkflowExecutionResultDto
            {
                ExecutionId = executionId,
                Status = nameof(WorkflowExecutionStatus.Running)
            };
            handOff.ConsumerReady();

            await foreach (var stepResult in handOff.Steps.ReadAllAsync(CancellationToken.None))
            {
                yield return new WorkflowExecutionResultDto
                {
                    ExecutionId = executionId,
                    Output = stepResult.Output,
                    Status = $"Step '{stepResult.StepId}'" + (stepResult.Skipped ? " (skipped)" : string.Empty),
                    StepResults = [stepResult]
                };
                handOff.ConsumerReady();
            }

            var engineResult = await engineTask;

            // The run's outcome is the engine's, recorded before the summary is written: a consumer
            // that disconnects now does not turn a finished run into a failed one.
            await RecordEngineOutcomeAsync(engineResult);

            yield return new WorkflowExecutionResultDto
            {
                ExecutionId = executionId,
                RunId = engineResult.RunId,
                Output = finalOutput,
                Status = finalStatusOverride ?? "Completed",
                StepResults = engineResult.StepResults.Count > 0 ? engineResult.StepResults : null
            };
        }
        finally
        {
            // The consumer stopped asking for events while the engine was still running: the engine
            // stops waiting for it and finishes on its own, and its outcome is recorded as on the
            // normal path. A failure or a cancelled token surfaces here as an exception: Failed below.
            if (engineTask != null && !engineFinished)
            {
                handOff.ConsumerGone();
                try
                {
                    await RecordEngineOutcomeAsync(await engineTask);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Streaming workflow run {ExecutionId} ended without a result", executionId);
                }
            }

            // Billed for what the nodes consumed on every exit path, interrupted streams included:
            // settling 0 refunded the whole reservation for nodes the provider had already billed.
            var isSuccess = engineFinished && IsSuccessfulWorkflowStatus(MapExecutionStatus(finalStatusOverride));
            var errorMessage = isSuccess
                ? null
                : engineFinished
                    ? BuildWorkflowFailureMessage(finalStatusOverride, finalOutput)
                    : "Workflow stream interrupted by exception";
            await AccountWorkflowUsageAsync(AIOperationType.WorkflowRunStreaming, workflowDef.Name, userId, reservation, usage,
                stopwatch.Elapsed, isSuccess, errorMessage,
                engineFinished ? finalStatusOverride ?? WorkflowUnsuccessfulErrorType : "workflow_stream_interrupted");

            if (!engineFinished && !string.IsNullOrWhiteSpace(executionId))
            {
                await TryUpdateWorkflowExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, CancellationToken.None);
            }
        }

        async Task<WorkflowEngineResult> RunEngineAsync(WorkflowGraph graph, WorkflowExecutionOptions options)
        {
            try
            {
                await handOff.WaitForConsumerAsync(ct);
                return await _workflowEngine.ExecuteAsync(graph, input, Check.NotNull(ServiceProvider), options, usage,
                    handOff.HandOverAsync, ct);
            }
            finally
            {
                handOff.Complete();
            }
        }

        async Task RecordEngineOutcomeAsync(WorkflowEngineResult engineResult)
        {
            finalOutput = engineResult.FinalOutput;
            finalStatusOverride = engineResult.StatusText;
            engineFinished = true;
            await TryUpdateWorkflowExecutionStatusAsync(executionId!, MapExecutionStatus(finalStatusOverride), CancellationToken.None);
        }
    }

    /// <summary>
    /// The engine and the consumer of a streaming run take turns. The engine hands over each step as
    /// it is recorded and then waits until the consumer asks for the next event; it also waits for the
    /// consumer's first request before it starts. The consumer's code between two events therefore
    /// never runs while the engine runs, and the request's DbContext, which both use, is never used
    /// concurrently. Once the consumer has gone the engine no longer waits.
    /// </summary>
    private sealed class StreamingHandOff : IDisposable
    {
        private readonly Channel<WorkflowStepResultDto> _steps = Channel.CreateUnbounded<WorkflowStepResultDto>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        private readonly SemaphoreSlim _consumerReady = new(0);
        private int _consumerGone;

        /// <summary>Steps as the engine records them; completes when the engine stops.</summary>
        public ChannelReader<WorkflowStepResultDto> Steps => _steps.Reader;

        /// <summary>Engine side: publish a step, then wait for the consumer to ask for the next event.</summary>
        public async Task HandOverAsync(WorkflowStepResultDto step, CancellationToken ct)
        {
            // A copy: the engine keeps its own result object and may still update it.
            await _steps.Writer.WriteAsync(new WorkflowStepResultDto { StepId = step.StepId, Output = step.Output, Skipped = step.Skipped }, ct);
            await WaitForConsumerAsync(ct);
        }

        /// <summary>Engine side: wait for the consumer's next request, unless it has gone.</summary>
        public Task WaitForConsumerAsync(CancellationToken ct)
            => Volatile.Read(ref _consumerGone) == 1 ? Task.CompletedTask : _consumerReady.WaitAsync(ct);

        /// <summary>Engine side: no more steps.</summary>
        public void Complete() => _steps.Writer.TryComplete();

        /// <summary>Consumer side: the consumer asks for the next event.</summary>
        public void ConsumerReady() => _consumerReady.Release();

        /// <summary>Consumer side: the consumer will not ask again; the engine stops waiting for it.</summary>
        public void ConsumerGone()
        {
            Volatile.Write(ref _consumerGone, 1);
            _consumerReady.Release();
        }

        public void Dispose() => _consumerReady.Dispose();
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

            if (string.Equals(WorkflowStepNodeType.Get(step), WorkflowNodeTypes.Approval, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private const string WorkflowUsageProvider = "Workflow";
    private const string WorkflowUnsuccessfulErrorType = "workflow_unsuccessful";

    /// <summary>
    /// The usage limit refused the run (429 <c>AI_USAGE_LIMIT_REACHED</c> with the window and reset time as the error details),
    /// or the meter itself failed (its own status and code, never presented as a limit).
    /// </summary>
    private Result<T> UsageLimitFailure<T>(Result<UsageLimitReservation> begun)
    {
        var reached = string.Equals(begun.ErrorCode, ErrorCodes.UsageLimitReached, StringComparison.Ordinal) || begun.Code == 429;
        return reached
            ? Fail<T>(begun.Message ?? "Usage limit reached.", 429, ErrorCodes.UsageLimitReached, begun.ErrorDetails)
            : Fail<T>(begun.Message ?? "The usage limit could not be checked.", begun.Code ?? 500,
                begun.ErrorCode ?? ErrorCodes.UsageLimitCheckFailed);
    }

    /// <summary>
    /// Log, record and settle what one engine call consumed, with the tokens the meter counted.
    /// </summary>
    /// <remarks>
    /// Called on every exit path of Run, RunStreaming, Resume and ResumeWithInput: success,
    /// failure and cancellation alike. Nodes that finished before a client disconnect or a
    /// storage error were billed by the provider, so settling 0 ("refund the reservation")
    /// was a quota bypass and left the usage log short. The cost is estimated from the metered
    /// tokens (the meter does not keep a per-model breakdown), so only a provider-wide or default
    /// cost rate applies. Never throws and never observes the request token, which may be the
    /// cancelled one.
    /// </remarks>
    private async Task AccountWorkflowUsageAsync(
        string operationType,
        string workflowName,
        Guid? userId,
        UsageLimitReservation? reservation,
        WorkflowUsageMeter usage,
        TimeSpan elapsed,
        bool isSuccess,
        string? errorMessage,
        string? errorType)
    {
        try
        {
            await _usageLogService.LogUsageAsync(
                operationType, WorkflowUsageProvider, workflowName,
                usage.InputTokens, usage.OutputTokens, (long)elapsed.TotalMilliseconds,
                isSuccess, errorMessage, ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to log workflow usage: {Workflow} ({OperationType})", workflowName, operationType);
        }

        RecordWorkflowTelemetry(operationType, workflowName, usage.InputTokens, usage.OutputTokens, elapsed, isSuccess,
            isSuccess ? null : errorType ?? WorkflowUnsuccessfulErrorType);

        if (userId.HasValue && reservation != null)
        {
            try
            {
                var cost = _costCalculator?.CalculateCost(
                    WorkflowUsageProvider, workflowName, usage.InputTokens, usage.OutputTokens, cachedInputTokens: 0, cacheWriteTokens: 0) ?? 0m;
                await _usageLimits.SettleAsync(reservation, cost, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to record workflow usage: {Workflow} ({OperationType})", workflowName, operationType);
            }
        }
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

        // The execution row is process state, not part of the caller's unit of work. Inside an
        // ambient transaction the insert would be deferred, and everything after it reads the row
        // with a database query: the checkpoint store would insert a second row for the same
        // ExecutionId (failing the request on the unique index at commit) and the terminal status
        // would find nothing to update. The flush stays inside the transaction.
        await _executionRepository.SaveChangesAsync(ct);

        return executionId;
    }
}
