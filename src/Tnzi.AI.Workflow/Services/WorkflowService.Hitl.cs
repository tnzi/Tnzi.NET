namespace Tnzi.AI.Workflow.Services;

/// <summary>
/// 工作流服务 - HITL（人在回路）执行控制：Resume / ResumeWithInput / Approve / Reject /
/// Cancel / 待处理中断与信号查询。仅 DAG 模式（带 CheckpointStore）支持暂停-恢复语义。
/// </summary>
public partial class WorkflowService
{
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
        // The optimistic concurrency stamp (B13) is the compare-and-swap token - two
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
}
