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

        var unclaimable = RefuseUnclaimableExecution(executionEntity,
            WorkflowExecutionStatus.Paused, WorkflowExecutionStatus.AwaitingApproval, WorkflowExecutionStatus.Failed);
        if (unclaimable != null)
            return unclaimable;

        var workflowDef = await _repository.GetAsync(executionEntity.WorkflowDefinitionId.Value, ct);
        if (workflowDef == null)
            return Fail<WorkflowExecutionResultDto>("Associated workflow definition not found", 404, ErrorCodes.WorkflowNotFound);

        // 仅 DAG 模式支持恢复
        if (workflowDef.ExecutionMode != WorkflowExecutionMode.Dag)
            return Fail<WorkflowExecutionResultDto>("Resume is only supported for DAG execution mode", 400, ErrorCodes.WorkflowExecutionInvalidState);

        // The resumed segment runs LLM nodes like the first one did, so it is reserved, logged
        // and settled like RunAsync (see ReserveResumeQuotaAsync). Reserved before the CAS:
        // a refused reservation must not leave the execution flipped to Running.
        var stopwatch = Stopwatch.StartNew();
        var (userId, reservation, quotaFailure) = await ReserveResumeQuotaAsync(workflowDef, checkpoint, ct);
        if (quotaFailure != null)
            return quotaFailure;

        // CAS guard: transition the execution to Running BEFORE invoking the engine.
        // The optimistic concurrency stamp (B13) is the compare-and-swap token - two
        // concurrent Resume POSTs both pass the status guard above, but only one wins the
        // status flip; the loser's UpdateAsync throws DbUpdateConcurrencyException and we
        // return 409 instead of re-running the engine (which would re-execute nodes and
        // double-charge tokens).
        if (!await TryClaimResumeAsync(executionEntity, ct))
            return Fail<WorkflowExecutionResultDto>("Workflow execution is already being resumed by another request", 409, ErrorCodes.WorkflowExecutionInvalidState);

        var usage = new WorkflowUsageMeter();
        try
        {
            // The engine restores from the store, so the release is saved first; the status is the
            // Running the CAS just wrote (the checkpoint object still carries the status it was read with).
            if (ReleaseRejectedSteps(checkpoint))
            {
                checkpoint.Status = WorkflowExecutionStatus.Running;
                checkpoint.UpdatedAt = DateTime.UtcNow;
                await _checkpointStore.SaveCheckpointAsync(checkpoint, ct);
            }

            var graph = BuildWorkflowGraph(workflowDef);
            var existingRun = await _runRepository.FirstOrDefaultAsync(r => r.WorkflowExecutionId == executionId, ct);

            // ★ 审批节点（nodeType=approval）经 CheckInterruptAsync 暂停时**没有执行过**，不在
            // CompletedStepIds 里；恢复时它会重新就绪。ApproveStepAsync 只在它的输出元数据上记下
            // "已批准 + 反馈"，这里把它翻译成 ResumeStepId/ResumeData 送进引擎，节点的恢复分支才会
            // 拿到 approved=true 并把上游内容（或反馈）作为真实输出交给下游 —— 否则它会再次中断，
            // approve → resume 变成死循环。RequiresApproval 标记的步骤已真实执行过（在 completed 里），
            // ResumeData 对它无效也无害。同层多个审批节点各自批准后要一次全带上，不然没带的那个
            // 会再次中断、用占位符盖掉自己的已批准元数据、再要一次审批。
            var approvalResumes = BuildApprovalResumes(checkpoint);

            var options = new WorkflowExecutionOptions
            {
                ExecutionId = executionId,
                WorkflowDefinitionId = workflowDef.Id,
                RunId = existingRun?.Id,
                Resume = true,
                CheckpointStore = _checkpointStore,
                ResumeDataByStep = approvalResumes.Count > 0 ? approvalResumes : null
            };

            var dagResult = await _workflowEngine.ExecuteAsync(graph, checkpoint.InitialInput, Check.NotNull(ServiceProvider), options, usage, ct);

            // Same as RunAsync: the checkpoint store writes the status but not DurationMs, which only the
            // terminal update computes. Without it every execution that went through an approval ended
            // with no duration and dropped out of the execution stats.
            await TryUpdateWorkflowExecutionStatusAsync(executionId, MapExecutionStatus(dagResult.StatusText), CancellationToken.None);

            await AccountResumedSegmentAsync(workflowDef, userId, reservation, usage, stopwatch, dagResult);

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
            // Not left Running for the watchdog to find: Failed is resumable, so the operator can retry.
            await TryUpdateWorkflowExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, CancellationToken.None);
            await AccountWorkflowUsageAsync(AIOperationType.WorkflowRun, workflowDef.Name, userId, reservation, usage,
                stopwatch.Elapsed, isSuccess: false, ex.Message, ex.GetType().Name);
            return Fail<WorkflowExecutionResultDto>("Failed to resume workflow execution", 500, ErrorCodes.WorkflowFailed);
        }
    }

    /// <summary>
    /// Check the usage limit for a resumed segment, charged to the user who resumes it (the same rule
    /// the run endpoints follow: usage is booked to the signed-in caller). No signed-in user (a background
    /// resume) means no metering, as for a run without a user; usage is still logged.
    /// </summary>
    private async Task<(Guid? UserId, UsageLimitReservation? Reservation, Result<WorkflowExecutionResultDto>? Failure)> ReserveResumeQuotaAsync(
        WorkflowDefinition workflowDef, WorkflowCheckpoint checkpoint, CancellationToken ct)
    {
        var userId = CurrentUser?.Id;
        if (!userId.HasValue)
            return (null, null, null);

        var begun = await _usageLimits.BeginAsync(userId.Value, ct);
        if (!begun.Succeeded)
            return (userId, null, UsageLimitFailure<WorkflowExecutionResultDto>(begun));

        return (userId, begun.Data, null);
    }

    /// <summary>
    /// The resume CAS (see <see cref="TryTransitionToRunningAsync"/>). A resume that does not go ahead
    /// (lost race or failed write) has nothing to give back: a usage window reservation holds no amount,
    /// and a segment that never ran records no cost.
    /// </summary>
    private Task<bool> TryClaimResumeAsync(WorkflowExecution executionEntity, CancellationToken ct)
        => TryTransitionToRunningAsync(executionEntity, ct);

    private Task AccountResumedSegmentAsync(
        WorkflowDefinition workflowDef, Guid? userId, UsageLimitReservation? reservation,
        WorkflowUsageMeter usage, Stopwatch stopwatch, WorkflowEngineResult dagResult)
    {
        var isSuccess = IsSuccessfulWorkflowStatus(MapExecutionStatus(dagResult.StatusText));
        var errorMessage = isSuccess ? null : BuildWorkflowFailureMessage(dagResult.StatusText, dagResult.FinalOutput);
        return AccountWorkflowUsageAsync(AIOperationType.WorkflowRun, workflowDef.Name, userId, reservation, usage,
            stopwatch.Elapsed, isSuccess, errorMessage, dagResult.StatusText ?? WorkflowUnsuccessfulErrorType);
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

        // 把审批结论记在步骤输出的元数据上；ResumeAsync 据此为尚未执行的审批节点构造 ResumeData
        // （见 BuildApprovalResumes）。
        // ★ 反馈只在步骤**尚未执行**时替换输出文本：审批节点（nodeType=approval）经中断暂停、不在
        // CompletedStepIds 里，它的文本只是执行器的占位符，恢复时节点按 ResumeData 重新出结果。
        // run-then-gate 步骤（内容节点上的 requiresApproval）暂停前已经执行完，它的输出就是被批准的
        // 内容、下游等的就是它，恢复时也不会重跑；反馈是对这份内容的评语，只进元数据。此前一律替换，
        // 下游拿到的是「Looks good」而不是草稿，草稿也从检查点里消失了。
        var alreadyExecuted = checkpoint.CompletedStepIds.Contains(stepId);
        var existingOutput = checkpoint.StepOutputs.GetValueOrDefault(stepId);
        var metadata = existingOutput?.Metadata != null
            ? new Dictionary<string, string>(existingOutput.Metadata, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        metadata[ApprovalStatusMetadataKey] = ApprovalStatusApproved;
        if (!string.IsNullOrWhiteSpace(feedback))
        {
            metadata[ApprovalFeedbackMetadataKey] = feedback;
        }

        checkpoint.StepOutputs[stepId] = new WorkflowStepOutput
        {
            Text = !alreadyExecuted && !string.IsNullOrWhiteSpace(feedback) ? feedback : existingOutput?.Text ?? string.Empty,
            Metadata = metadata
        };

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

        // 标记步骤输出为拒绝信息。拒绝让整个执行失败，还在等审批的步骤一并清掉。
        // ★ 被拒绝的步骤与同批还没批准的步骤都必须移出 completed。审批节点本来就不在里面，恢复时
        // （Failed 可恢复）会重新中断；run-then-gate 步骤（内容节点上的 requiresApproval）暂停前已执行完、
        // 在 completed 里，留着它，恢复时引擎直接越过闸门：下游拿着拒绝文本（或同批从未批准的内容）继续跑，
        // 整条报 Completed 而没有任何人批准过。移出后，恢复时它们重新生成内容、再次请求审批。已批准的同批
        // 步骤已不在 StepsAwaitingApproval 里，保持完成。
        foreach (var pendingStepId in checkpoint.StepsAwaitingApproval)
        {
            checkpoint.CompletedStepIds.Remove(pendingStepId);
        }

        checkpoint.StepOutputs[stepId] = new WorkflowStepOutput
        {
            Text = $"[Rejected: {reason}]",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ApprovalStatusMetadataKey] = ApprovalStatusRejected,
                ["approval_reason"] = reason
            }
        };
        checkpoint.StepsAwaitingApproval.Clear();
        checkpoint.Status = WorkflowExecutionStatus.Failed;
        checkpoint.UpdatedAt = DateTime.UtcNow;

        await _checkpointStore.SaveCheckpointAsync(checkpoint, ct);
        await TryCloseWaitingAgentRunAsync(executionId, AgentRunStatus.Failed, reason);

        return Ok();
    }

    /// <summary>
    /// Ends the waiting <see cref="AgentRun"/> of an execution that just left its wait here (see
    /// <see cref="WaitingAgentRunCloser"/>). The execution's own transition has already been written; a failure
    /// here leaves only the run projection behind, so it is logged rather than failing the call.
    /// </summary>
    private async Task TryCloseWaitingAgentRunAsync(string executionId, AgentRunStatus status, string? error)
    {
        try
        {
            await WaitingAgentRunCloser.CloseAsync(_runRepository, executionId, status, error, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Workflow execution {ExecutionId} left its wait, but its agent run could not be marked {Status}", executionId, status);
        }
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

        var unclaimable = RefuseUnclaimableExecution(executionEntity, WorkflowExecutionStatus.AwaitingInput);
        if (unclaimable != null)
            return unclaimable;

        var workflowDef = await _repository.GetAsync(executionEntity.WorkflowDefinitionId.Value, ct);
        if (workflowDef == null)
            return Fail<WorkflowExecutionResultDto>("Associated workflow definition not found", 404, ErrorCodes.WorkflowNotFound);

        if (workflowDef.ExecutionMode != WorkflowExecutionMode.Dag)
            return Fail<WorkflowExecutionResultDto>("Resume with input is only supported for DAG execution mode", 400, ErrorCodes.WorkflowExecutionInvalidState);

        // Reserved, logged and settled like any other segment (see ReserveResumeQuotaAsync).
        var stopwatch = Stopwatch.StartNew();
        var (userId, reservation, quotaFailure) = await ReserveResumeQuotaAsync(workflowDef, checkpoint, ct);
        if (quotaFailure != null)
            return quotaFailure;

        // CAS guard: flip the execution to Running BEFORE running the engine so two
        // concurrent ResumeWithInput POSTs cannot both execute. The concurrency stamp
        // (B13) is the compare-and-swap token; the losing writer gets 409.
        if (!await TryClaimResumeAsync(executionEntity, ct))
            return Fail<WorkflowExecutionResultDto>("Workflow execution is already being resumed by another request", 409, ErrorCodes.WorkflowExecutionInvalidState);

        var usage = new WorkflowUsageMeter();
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

            var dagResult = await _workflowEngine.ExecuteAsync(graph, checkpoint.InitialInput, Check.NotNull(ServiceProvider), options, usage, ct);

            // 更新执行状态。按引擎的结论映射，与 RunAsync / ResumeAsync 同口径：此前只分 Failed / Completed，
            // 恢复途中经信号取消的执行被记成 Completed。
            await TryUpdateWorkflowExecutionStatusAsync(executionId, MapExecutionStatus(dagResult.StatusText), CancellationToken.None);

            await AccountResumedSegmentAsync(workflowDef, userId, reservation, usage, stopwatch, dagResult);

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
            await AccountWorkflowUsageAsync(AIOperationType.WorkflowRun, workflowDef.Name, userId, reservation, usage,
                stopwatch.Elapsed, isSuccess: false, ex.Message, ex.GetType().Name);
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
            await TryCloseWaitingAgentRunAsync(executionId, AgentRunStatus.Cancelled, error: null);
            return Ok();
        }

        // The enqueue re-reads the row and writes the signal together with the "cancel_requested"
        // wait reason. ★ Nothing is written back from `entity` afterwards: it is the copy read before
        // the enqueue. Saving it failed on the stale concurrency stamp (a 500 after the signal was
        // stored), and inside a transaction, where both writes wait for the commit, it overwrote the
        // pending signal list, so the call answered 200 and the engine never saw the cancel.
        await mailbox.EnqueueSignalAsync(executionId, new WorkflowExecutionSignal
        {
            Type = WorkflowExecutionSignalTypes.Cancel,
            Reason = reason ?? "Cancelled by operator"
        }, ct);

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

    private const string ApprovalStatusMetadataKey = "approval_status";
    private const string ApprovalFeedbackMetadataKey = "approval_feedback";
    private const string ApprovalStatusApproved = "approved";
    private const string ApprovalStatusRejected = "rejected";

    /// <summary>
    /// Take every step whose output carries the rejection marker out of the completed set, so a
    /// resume produces it again and puts it to approval instead of handing the rejection text to
    /// its dependents. <see cref="RejectStepAsync"/> already removes it, but the checkpoint store
    /// union-merges the completed set when a save hits a concurrency conflict: a step of the same
    /// pause approved concurrently with the rejection puts the rejected step back. A checkpoint
    /// rejected before rejections removed anything has the same shape. Returns whether anything changed.
    /// </summary>
    private static bool ReleaseRejectedSteps(WorkflowCheckpoint checkpoint)
    {
        var rejected = checkpoint.StepOutputs
            .Where(kv => checkpoint.CompletedStepIds.Contains(kv.Key)
                && kv.Value.Metadata != null
                && kv.Value.Metadata.TryGetValue(ApprovalStatusMetadataKey, out var status)
                && string.Equals(status, ApprovalStatusRejected, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var stepId in rejected)
        {
            checkpoint.CompletedStepIds.Remove(stepId);
        }

        return rejected.Count > 0;
    }

    /// <summary>
    /// Find every approval step that <see cref="ApproveStepAsync"/> approved but the engine has not
    /// executed yet (an interrupt-style approval node stays out of CompletedStepIds), and build the
    /// ResumeData its <c>ExecuteAsync</c> resume branch expects: <c>approved=true</c> plus the
    /// operator's feedback as <c>comment</c>. Empty when nothing qualifies (run-then-gate
    /// approvals are already completed and need no resume data; a rejected step must re-interrupt).
    /// </summary>
    private static Dictionary<string, Dictionary<string, object>> BuildApprovalResumes(WorkflowCheckpoint checkpoint)
    {
        var resumes = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (stepId, output) in checkpoint.StepOutputs)
        {
            if (checkpoint.CompletedStepIds.Contains(stepId))
                continue;

            if (output.Metadata == null
                || !output.Metadata.TryGetValue(ApprovalStatusMetadataKey, out var status)
                || !string.Equals(status, ApprovalStatusApproved, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var feedback = output.Metadata.GetValueOrDefault(ApprovalFeedbackMetadataKey) ?? string.Empty;
            resumes[stepId] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["approved"] = true,
                ["comment"] = feedback
            };
        }

        return resumes;
    }

    /// <summary>
    /// The status check that goes with the resume CAS. The status guard at the top of a resume reads
    /// the checkpoint; the CAS swaps on the concurrency stamp of <paramref name="executionEntity"/>,
    /// a second read. A concurrent resume that commits its claim between the two reads leaves this
    /// row Running under a fresh stamp, and swapping on that stamp succeeds: both requests would run
    /// the engine. So the status of the read that supplies the stamp decides whether it is claimable.
    /// </summary>
    private Result<WorkflowExecutionResultDto>? RefuseUnclaimableExecution(WorkflowExecution executionEntity, params WorkflowExecutionStatus[] claimable)
    {
        if (claimable.Contains(executionEntity.Status))
            return null;

        return executionEntity.Status == WorkflowExecutionStatus.Running
            ? Fail<WorkflowExecutionResultDto>("Workflow execution is already being resumed by another request", 409, ErrorCodes.WorkflowExecutionInvalidState)
            : Fail<WorkflowExecutionResultDto>($"Cannot resume execution in '{executionEntity.Status}' state", 400, ErrorCodes.WorkflowExecutionInvalidState);
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
            // The compare-and-swap happens when the UPDATE reaches the database. Inside an ambient
            // transaction that would be the commit, after this request had already run the engine
            // a second time; the flush makes the swap happen here, inside the transaction.
            await _executionRepository.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // The losing write stays tracked as Modified; left there, the request's own commit
            // would retry it and fail with a 500 after the 409 was already decided.
            _executionRepository.Discard(executionEntity);
            Logger.LogInformation(ex,
                "Resume CAS lost for execution {ExecutionId}: a concurrent resume already transitioned it",
                executionEntity.ExecutionId);
            return false;
        }
    }
}
