namespace Tnzi.AI.Services;

/// <summary>
/// 工作流服务 - 执行相关方法 (Run/Stream/Resume/Approve/Reject + 执行状态/详情/列表查询)
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
            WorkflowExecutionOptions? options = null;

            if (workflowDef.ExecutionMode == WorkflowExecutionMode.Dag)
            {
                executionId = await EnsureWorkflowExecutionAsync(workflowDef.Id, input, ct);
                options = new WorkflowExecutionOptions
                {
                    ExecutionId = executionId,
                    WorkflowDefinitionId = workflowDef.Id,
                    CheckpointStore = _checkpointStore
                };
            }
            else
            {
                options = new WorkflowExecutionOptions
                {
                    WorkflowDefinitionId = workflowDef.Id
                };
            }

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
            if (!string.IsNullOrWhiteSpace(executionId))
            {
                await TryUpdateWorkflowExecutionStatusAsync(executionId, executionStatus, CancellationToken.None);
            }

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
            WorkflowExecutionOptions? options = null;

            if (workflowDef.ExecutionMode == WorkflowExecutionMode.Dag)
            {
                executionId = await EnsureWorkflowExecutionAsync(workflowDef.Id, input, ct);
                options = new WorkflowExecutionOptions
                {
                    ExecutionId = executionId,
                    WorkflowDefinitionId = workflowDef.Id,
                    CheckpointStore = _checkpointStore
                };
            }
            else
            {
                options = new WorkflowExecutionOptions
                {
                    WorkflowDefinitionId = workflowDef.Id
                };
            }

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
            if (!string.IsNullOrWhiteSpace(executionId))
            {
                await TryUpdateWorkflowExecutionStatusAsync(executionId, MapExecutionStatus(finalStatusOverride), CancellationToken.None);
            }

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

    public async Task<Result<WorkflowExecutionResultDto>> ResumeAsync(string executionId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        // 从检查点存储加载检查点
        var checkpoint = await _checkpointStore.GetCheckpointAsync(executionId, ct);
        if (checkpoint == null)
            return Fail<WorkflowExecutionResultDto>("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        // 允许从 paused、awaiting_approval 或 failed 状态恢复。
        // failed 场景用于节点重试：检查点里仍保留已完成步骤和失败前的状态，恢复后会从未完成节点继续。
        if (checkpoint.Status is not (WorkflowExecutionStatus.Paused or WorkflowExecutionStatus.AwaitingApproval or WorkflowExecutionStatus.Failed))
            return Fail<WorkflowExecutionResultDto>($"Cannot resume execution in '{checkpoint.Status}' state, expected 'Paused', 'AwaitingApproval' or 'Failed'", 400, ErrorCodes.WorkflowExecutionInvalidState);

        // 检查是否还有步骤等待审批
        if (checkpoint.StepsAwaitingApproval.Count > 0)
            return Fail<WorkflowExecutionResultDto>("Execution has steps awaiting approval, approve or reject them first", 400, ErrorCodes.WorkflowExecutionInvalidState);

        // 查找关联的工作流定义（通过 WorkflowExecution 的 WorkflowDefinitionId）
        var executionEntity = await _executionRepository
            .FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);

        if (executionEntity?.WorkflowDefinitionId == null)
            return Fail<WorkflowExecutionResultDto>("Cannot determine workflow definition for this execution", 400, ErrorCodes.WorkflowExecutionInvalidState);

        var workflowDef = await _repository.GetAsync(executionEntity.WorkflowDefinitionId.Value, ct);
        if (workflowDef == null)
            return Fail<WorkflowExecutionResultDto>("Associated workflow definition not found", 404, ErrorCodes.WorkflowNotFound);

        // 仅 DAG 模式支持恢复
        if (workflowDef.ExecutionMode != WorkflowExecutionMode.Dag)
            return Fail<WorkflowExecutionResultDto>("Resume is only supported for DAG execution mode", 400, ErrorCodes.WorkflowExecutionInvalidState);

        // CAS guard: transition the execution to Running BEFORE invoking the engine.
        // The optimistic concurrency stamp (B13) is the compare-and-swap token — two
        // concurrent Resume POSTs both pass the status guard above, but only one wins the
        // status flip; the loser's UpdateAsync throws DbUpdateConcurrencyException and we
        // return 409 instead of re-running the engine (which would re-execute nodes and
        // double-charge tokens).
        if (!await TryTransitionToRunningAsync(executionEntity, ct))
            return Fail<WorkflowExecutionResultDto>("Workflow execution is already being resumed by another request", 409, ErrorCodes.WorkflowExecutionInvalidState);

        try
        {
            var graph = BuildWorkflowGraph(workflowDef);
            var existingRun = await _runRepository.FirstOrDefaultAsync(r => r.WorkflowExecutionId == executionId, ct);

            var options = new WorkflowExecutionOptions
            {
                ExecutionId = executionId,
                WorkflowDefinitionId = workflowDef.Id,
                RunId = existingRun?.Id,
                Resume = true,
                CheckpointStore = _checkpointStore
            };

            var dagResult = await _workflowEngine.ExecuteAsync(graph, checkpoint.InitialInput, Check.NotNull(ServiceProvider), options, ct);

            return Ok(new WorkflowExecutionResultDto
            {
                ExecutionId = executionId,
                RunId = dagResult.RunId,
                Output = dagResult.FinalOutput,
                Status = dagResult.StatusText,
                StepResults = dagResult.StepResults
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to resume workflow execution: {ExecutionId}", executionId);
            return Fail<WorkflowExecutionResultDto>("Failed to resume workflow execution", 500, ErrorCodes.WorkflowFailed);
        }
    }

    public async Task<Result> ApproveStepAsync(string executionId, string stepId, string? feedback = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);
        Check.NotNullOrWhiteSpace(stepId);

        var checkpoint = await _checkpointStore.GetCheckpointAsync(executionId, ct);
        if (checkpoint == null)
            return Fail("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        if (checkpoint.Status != WorkflowExecutionStatus.AwaitingApproval)
            return Fail($"Execution is not awaiting approval, current status: '{checkpoint.Status}'", 400, ErrorCodes.WorkflowExecutionInvalidState);

        if (!checkpoint.StepsAwaitingApproval.Contains(stepId))
            return Fail($"Step '{stepId}' is not awaiting approval", 400, ErrorCodes.WorkflowStepNotAwaitingApproval);

        // 移除该步骤的审批等待
        checkpoint.StepsAwaitingApproval.Remove(stepId);

        // 如果有 feedback，替换步骤输出
        if (!string.IsNullOrWhiteSpace(feedback))
        {
            checkpoint.StepOutputs[stepId] = new WorkflowStepOutput
            {
                Text = feedback,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["approval_feedback"] = feedback
                }
            };
        }

        // 如果没有更多步骤等待审批，将状态改为 paused（等待 resume）
        checkpoint.Status = checkpoint.StepsAwaitingApproval.Count > 0 ? WorkflowExecutionStatus.AwaitingApproval : WorkflowExecutionStatus.Paused;
        checkpoint.UpdatedAt = DateTime.UtcNow;

        await _checkpointStore.SaveCheckpointAsync(checkpoint, ct);

        return Ok();
    }

    public async Task<Result> RejectStepAsync(string executionId, string stepId, string reason, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);
        Check.NotNullOrWhiteSpace(stepId);
        Check.NotNullOrWhiteSpace(reason);

        var checkpoint = await _checkpointStore.GetCheckpointAsync(executionId, ct);
        if (checkpoint == null)
            return Fail("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        if (checkpoint.Status != WorkflowExecutionStatus.AwaitingApproval)
            return Fail($"Execution is not awaiting approval, current status: '{checkpoint.Status}'", 400, ErrorCodes.WorkflowExecutionInvalidState);

        if (!checkpoint.StepsAwaitingApproval.Contains(stepId))
            return Fail($"Step '{stepId}' is not awaiting approval", 400, ErrorCodes.WorkflowStepNotAwaitingApproval);

        // 标记步骤输出为拒绝信息
        checkpoint.StepOutputs[stepId] = new WorkflowStepOutput
        {
            Text = $"[Rejected: {reason}]",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["approval_status"] = "rejected",
                ["approval_reason"] = reason
            }
        };
        checkpoint.StepsAwaitingApproval.Clear();
        checkpoint.Status = WorkflowExecutionStatus.Failed;
        checkpoint.UpdatedAt = DateTime.UtcNow;

        await _checkpointStore.SaveCheckpointAsync(checkpoint, ct);

        return Ok();
    }

    public async Task<Result<WorkflowExecutionStatusDto>> GetExecutionStatusAsync(string executionId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var entity = await _executionRepository
            .FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);
        if (entity == null)
            return Fail<WorkflowExecutionStatusDto>("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        var checkpoint = await _checkpointStore.GetCheckpointAsync(executionId, ct);
        if (checkpoint == null)
            return Fail<WorkflowExecutionStatusDto>("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        return Ok(new WorkflowExecutionStatusDto
        {
            ExecutionId = checkpoint.ExecutionId,
            Status = checkpoint.Status.ToString(),
            CompletedStepIds = checkpoint.CompletedStepIds.ToList(),
            StepsAwaitingApproval = checkpoint.StepsAwaitingApproval.ToList(),
            CreatedAt = checkpoint.CreatedAt,
            UpdatedAt = checkpoint.UpdatedAt,
            PendingSignalCount = entity.PendingSignalCount,
            CurrentWaitReason = entity.CurrentWaitReason
        });
    }

    [ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
    public async Task<Result<List<WorkflowExecutionSignal>>> GetPendingSignalsAsync(string executionId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var mailbox = ServiceProvider?.GetService<IWorkflowExecutionMailbox>();
        if (mailbox == null)
            return Fail<List<WorkflowExecutionSignal>>("Workflow mailbox is not available", 501, ErrorCodes.WorkflowFailed);

        var entity = await _executionRepository.FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);
        if (entity == null)
            return Fail<List<WorkflowExecutionSignal>>("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        return Ok(await mailbox.GetPendingSignalsAsync(executionId, ct));
    }

    public async Task<Result<WorkflowExecutionDetailDto>> GetExecutionDetailAsync(string executionId)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var entity = await _executionRepository
            .FirstOrDefaultAsync(e => e.ExecutionId == executionId);

        if (entity == null)
            return Fail<WorkflowExecutionDetailDto>("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        var completedStepIds = DeserializeJsonList(entity.CompletedSteps);
        var stepsAwaiting = DeserializeJsonList(entity.StepsAwaitingApproval);
        var stepOutputs = DeserializeJsonDict(entity.StepOutputs);

        // 终态（Completed/Failed/Cancelled）时不加载 pending signals，必然为空
        var isTerminal = entity.Status is WorkflowExecutionStatus.Completed
            or WorkflowExecutionStatus.Failed
            or WorkflowExecutionStatus.Cancelled;

        return Ok(new WorkflowExecutionDetailDto
        {
            Id = entity.Id,
            ExecutionId = entity.ExecutionId,
            WorkflowDefinitionId = entity.WorkflowDefinitionId,
            Status = entity.Status,
            InitialInput = entity.InitialInput,
            CompletedStepIds = completedStepIds,
            CompletedStepCount = completedStepIds.Count,
            StepsAwaitingApproval = stepsAwaiting,
            AwaitingApprovalCount = stepsAwaiting.Count,
            StepOutputs = stepOutputs,
            StartedAt = entity.StartedAt,
            DurationMs = entity.DurationMs,
            CreationTime = entity.CreationTime,
            CompletedTime = entity.CompletedTime,
            UpdatedTime = entity.UpdatedTime,
            PendingSignalCount = entity.PendingSignalCount,
            CurrentWaitReason = entity.CurrentWaitReason,
            PendingSignals = isTerminal ? [] : await LoadPendingSignalsAsync(entity.ExecutionId)
        });
    }

    public async Task<Result<IPagedList<WorkflowExecutionSummaryDto>>> GetExecutionsAsync(WorkflowExecutionQueryDto query)
    {
        Check.NotNull(query);

        var pagedList = await _executionRepository.AsQueryable()
            .WhereIf(e => e.WorkflowDefinitionId == query.WorkflowDefinitionId!.Value, query.WorkflowDefinitionId.HasValue)
            .WhereIf(e => e.Status == query.Status!.Value, query.Status.HasValue)
            .OrderByDescending(e => e.CreationTime)
            .Select(e => new WorkflowExecutionSummaryDto
            {
                Id = e.Id,
                ExecutionId = e.ExecutionId,
                WorkflowDefinitionId = e.WorkflowDefinitionId,
                Status = e.Status,
                CompletedStepCount = e.CompletedSteps.Length > 2 ? e.CompletedSteps.Length : 0, // rough estimation from JSON
                AwaitingApprovalCount = e.StepsAwaitingApproval.Length > 2 ? 1 : 0, // rough flag
                StartedAt = e.StartedAt,
                DurationMs = e.DurationMs,
                CreationTime = e.CreationTime,
                CompletedTime = e.CompletedTime,
                UpdatedTime = e.UpdatedTime,
                PendingSignalCount = e.PendingSignalCount,
                CurrentWaitReason = e.CurrentWaitReason
            })
            .CreateAsync(query);

        return Ok(pagedList);
    }

    public async Task<Result<WorkflowExecutionResultDto>> ResumeWithInputAsync(string executionId, string stepId, Dictionary<string, object> input, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);
        Check.NotNullOrWhiteSpace(stepId);
        Check.NotNull(input);

        // 从检查点存储加载检查点
        var checkpoint = await _checkpointStore.GetCheckpointAsync(executionId, ct);
        if (checkpoint == null)
            return Fail<WorkflowExecutionResultDto>("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        // 验证状态：仅 AwaitingInput 可用此方法恢复
        if (checkpoint.Status != WorkflowExecutionStatus.AwaitingInput)
            return Fail<WorkflowExecutionResultDto>($"Cannot resume with input in '{checkpoint.Status}' state, expected 'AwaitingInput'", 400, ErrorCodes.WorkflowExecutionInvalidState);

        // 验证 pending interrupt 存在且匹配 stepId
        if (string.IsNullOrWhiteSpace(checkpoint.PendingInterruptJson))
            return Fail<WorkflowExecutionResultDto>("No pending interrupt found for this execution", 400, ErrorCodes.WorkflowExecutionInvalidState);

        WorkflowInterrupt? pendingInterrupt;
        try
        {
            pendingInterrupt = JsonSerializer.Deserialize<WorkflowInterrupt>(checkpoint.PendingInterruptJson, TnziJsonDefaults.Options);
        }
        catch
        {
            return Fail<WorkflowExecutionResultDto>("Failed to parse pending interrupt data", 500, ErrorCodes.WorkflowFailed);
        }

        if (pendingInterrupt == null || !string.Equals(pendingInterrupt.StepId, stepId, StringComparison.OrdinalIgnoreCase))
            return Fail<WorkflowExecutionResultDto>($"Step '{stepId}' does not match the pending interrupt step '{pendingInterrupt?.StepId}'", 400, ErrorCodes.WorkflowExecutionInvalidState);

        // 查找关联的工作流定义
        var executionEntity = await _executionRepository
            .FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);

        if (executionEntity?.WorkflowDefinitionId == null)
            return Fail<WorkflowExecutionResultDto>("Cannot determine workflow definition for this execution", 400, ErrorCodes.WorkflowExecutionInvalidState);

        var workflowDef = await _repository.GetAsync(executionEntity.WorkflowDefinitionId.Value, ct);
        if (workflowDef == null)
            return Fail<WorkflowExecutionResultDto>("Associated workflow definition not found", 404, ErrorCodes.WorkflowNotFound);

        if (workflowDef.ExecutionMode != WorkflowExecutionMode.Dag)
            return Fail<WorkflowExecutionResultDto>("Resume with input is only supported for DAG execution mode", 400, ErrorCodes.WorkflowExecutionInvalidState);

        // CAS guard: flip the execution to Running BEFORE running the engine so two
        // concurrent ResumeWithInput POSTs cannot both execute. The concurrency stamp
        // (B13) is the compare-and-swap token; the losing writer gets 409.
        if (!await TryTransitionToRunningAsync(executionEntity, ct))
            return Fail<WorkflowExecutionResultDto>("Workflow execution is already being resumed by another request", 409, ErrorCodes.WorkflowExecutionInvalidState);

        try
        {
            // 从检查点恢复状态，移除中断步骤使其重新进入就绪队列
            checkpoint.CompletedStepIds.Remove(stepId);
            checkpoint.PendingInterruptJson = null;
            checkpoint.Status = WorkflowExecutionStatus.Running;
            checkpoint.UpdatedAt = DateTime.UtcNow;
            await _checkpointStore.SaveCheckpointAsync(checkpoint, ct);

            // 构建 WorkflowExecutionOptions，在恢复时传递 ResumeData
            var graph = BuildWorkflowGraph(workflowDef);
            var existingRun = await _runRepository.FirstOrDefaultAsync(r => r.WorkflowExecutionId == executionId, ct);

            var options = new WorkflowExecutionOptions
            {
                ExecutionId = executionId,
                WorkflowDefinitionId = workflowDef.Id,
                RunId = existingRun?.Id,
                Resume = true,
                CheckpointStore = _checkpointStore,
                ResumeStepId = stepId,
                ResumeData = input
            };

            var dagResult = await _workflowEngine.ExecuteAsync(graph, checkpoint.InitialInput, Check.NotNull(ServiceProvider), options, ct);

            // 更新执行状态
            if (dagResult.AwaitingInterrupt == null && !dagResult.AwaitingApproval)
            {
                var completedStatus = dagResult.HasFailure ? WorkflowExecutionStatus.Failed : WorkflowExecutionStatus.Completed;
                await TryUpdateWorkflowExecutionStatusAsync(executionId, completedStatus, CancellationToken.None);
            }

            return Ok(new WorkflowExecutionResultDto
            {
                ExecutionId = executionId,
                RunId = dagResult.RunId,
                Output = dagResult.FinalOutput,
                Status = dagResult.StatusText,
                StepResults = dagResult.StepResults
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to resume workflow with input: {ExecutionId}, StepId={StepId}", executionId, stepId);
            await TryUpdateWorkflowExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, CancellationToken.None);
            return Fail<WorkflowExecutionResultDto>("Failed to resume workflow with input", 500, ErrorCodes.WorkflowFailed);
        }
    }

    [ExperimentalApi(Reason = "Workflow execution control is in preview")]
    public async Task<Result> CancelAsync(string executionId, string? reason = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var entity = await _executionRepository.FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);
        if (entity == null)
            return Fail("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        if (entity.Status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Failed or WorkflowExecutionStatus.Cancelled)
            return Fail("Workflow execution is already in a terminal state", 400, ErrorCodes.WorkflowExecutionInvalidState);

        var mailbox = ServiceProvider?.GetService<IWorkflowExecutionMailbox>();
        if (mailbox == null)
            return Fail("Workflow mailbox is not available", 501, ErrorCodes.WorkflowFailed);

        if (entity.Status is WorkflowExecutionStatus.AwaitingApproval or WorkflowExecutionStatus.AwaitingInput or WorkflowExecutionStatus.Paused)
        {
            var checkpoint = await _checkpointStore.GetCheckpointAsync(executionId, ct);
            if (checkpoint != null)
            {
                checkpoint.Status = WorkflowExecutionStatus.Cancelled;
                checkpoint.PendingInterruptJson = null;
                checkpoint.StepsAwaitingApproval.Clear();
                checkpoint.UpdatedAt = DateTime.UtcNow;
                await _checkpointStore.SaveCheckpointAsync(checkpoint, ct);
            }

            await mailbox.ClearSignalsAsync(executionId, ct);
            await TryUpdateWorkflowExecutionStatusAsync(executionId, WorkflowExecutionStatus.Cancelled, CancellationToken.None, "cancelled");
            return Ok();
        }

        await mailbox.EnqueueSignalAsync(executionId, new WorkflowExecutionSignal
        {
            Type = WorkflowExecutionSignalTypes.Cancel,
            Reason = reason ?? "Cancelled by operator"
        }, ct);

        entity.CurrentWaitReason = "cancel_requested";
        entity.UpdatedTime = DateTime.UtcNow;
        await _executionRepository.UpdateAsync(entity, ct);
        return Ok();
    }

    public async Task<Result<WorkflowInterruptDto>> GetPendingInterruptAsync(string executionId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var checkpoint = await _checkpointStore.GetCheckpointAsync(executionId, ct);
        if (checkpoint == null)
            return Fail<WorkflowInterruptDto>("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        if (checkpoint.Status != WorkflowExecutionStatus.AwaitingInput || string.IsNullOrWhiteSpace(checkpoint.PendingInterruptJson))
            return Fail<WorkflowInterruptDto>("No pending interrupt for this execution", 404, ErrorCodes.WorkflowExecutionNotFound);

        WorkflowInterrupt? interrupt;
        try
        {
            interrupt = JsonSerializer.Deserialize<WorkflowInterrupt>(checkpoint.PendingInterruptJson, TnziJsonDefaults.Options);
        }
        catch
        {
            return Fail<WorkflowInterruptDto>("Failed to parse pending interrupt data", 500, ErrorCodes.WorkflowFailed);
        }

        if (interrupt == null)
            return Fail<WorkflowInterruptDto>("Pending interrupt data is corrupted", 500, ErrorCodes.WorkflowFailed);

        return Ok(new WorkflowInterruptDto
        {
            ExecutionId = executionId,
            StepId = interrupt.StepId,
            Reason = interrupt.Reason,
            Type = interrupt.Type.ToString(),
            RequestedInput = interrupt.RequestedInput,
            TimeoutSeconds = interrupt.Timeout?.TotalSeconds
        });
    }

    private static void AggregateUsage(TokenUsageDto? usage, ref int actualInputTokens, ref int actualOutputTokens)
    {
        if (usage == null) return;
        actualInputTokens += usage.InputTokens;
        actualOutputTokens += usage.OutputTokens;
    }

    private static WorkflowExecutionStatus MapExecutionStatus(string? statusText)
    {
        if (string.IsNullOrWhiteSpace(statusText))
        {
            return WorkflowExecutionStatus.Running;
        }

        if (statusText.StartsWith("PartialFailure", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowExecutionStatus.Failed;
        }

        return statusText switch
        {
            nameof(WorkflowExecutionStatus.Completed) => WorkflowExecutionStatus.Completed,
            nameof(WorkflowExecutionStatus.Failed) => WorkflowExecutionStatus.Failed,
            nameof(WorkflowExecutionStatus.Cancelled) => WorkflowExecutionStatus.Cancelled,
            nameof(WorkflowExecutionStatus.Paused) => WorkflowExecutionStatus.Paused,
            nameof(WorkflowExecutionStatus.AwaitingApproval) => WorkflowExecutionStatus.AwaitingApproval,
            nameof(WorkflowExecutionStatus.AwaitingInput) => WorkflowExecutionStatus.AwaitingInput,
            _ => WorkflowExecutionStatus.Running
        };
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

    /// <summary>
    /// Atomically transition a (resumable) execution to <see cref="WorkflowExecutionStatus.Running"/>
    /// using the optimistic concurrency stamp as a compare-and-swap token. Returns
    /// <c>false</c> when a concurrent Resume already flipped the status (stale token →
    /// <see cref="DbUpdateConcurrencyException"/>), so the caller can return 409 instead of
    /// re-running the engine and double-executing nodes. The detached entity read by the
    /// caller carries the stamp it observed; a successful UpdateAsync bumps it.
    /// </summary>
    private async Task<bool> TryTransitionToRunningAsync(WorkflowExecution executionEntity, CancellationToken ct)
    {
        executionEntity.Status = WorkflowExecutionStatus.Running;
        executionEntity.CurrentWaitReason = null;
        executionEntity.UpdatedTime = DateTime.UtcNow;
        if (executionEntity.StartedAt == null)
        {
            executionEntity.StartedAt = DateTime.UtcNow;
        }

        try
        {
            await _executionRepository.UpdateAsync(executionEntity, ct);
            return true;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            Logger.LogInformation(ex,
                "Resume CAS lost for execution {ExecutionId}: a concurrent resume already transitioned it",
                executionEntity.ExecutionId);
            return false;
        }
    }

    private async Task TryUpdateWorkflowExecutionStatusAsync(string executionId, WorkflowExecutionStatus status, CancellationToken ct, string? currentWaitReason = null)
    {
        try
        {
            var entity = await _executionRepository.FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);
            if (entity == null)
            {
                return;
            }

            entity.Status = status;
            entity.CurrentWaitReason = currentWaitReason;
            var now = DateTime.UtcNow;
            entity.UpdatedTime = now;
            if (status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Failed or WorkflowExecutionStatus.Cancelled)
            {
                entity.CompletedTime ??= now;
                // 计算执行耗时
                if (entity.StartedAt.HasValue)
                {
                    entity.DurationMs = (long)(entity.CompletedTime.Value - entity.StartedAt.Value).TotalMilliseconds;
                }
            }

            await _executionRepository.UpdateAsync(entity, ct);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to update workflow execution status: {ExecutionId} -> {Status}", executionId, status);
        }
    }

    private async Task<List<WorkflowExecutionSignal>> LoadPendingSignalsAsync(string executionId)
    {
        var mailbox = ServiceProvider?.GetService<IWorkflowExecutionMailbox>();
        if (mailbox == null)
        {
            return [];
        }

        return await mailbox.GetPendingSignalsAsync(executionId, CancellationToken.None);
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

    private static List<string> DeserializeJsonList(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return [];
        try { return JsonSerializer.Deserialize<List<string>>(json, TnziJsonDefaults.Options) ?? []; }
        catch { return []; }
    }

    private static Dictionary<string, string> DeserializeJsonDict(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new();
        try
        {
            // StepOutputs is stored as Dictionary<string, WorkflowStepOutput> in checkpoint,
            // but in execution entity it's a flat JSON object. Parse flexibly.
            using var doc = JsonDocument.Parse(json);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? string.Empty
                    : prop.Value.GetRawText();
            }
            return dict;
        }
        catch { return new(); }
    }
}
