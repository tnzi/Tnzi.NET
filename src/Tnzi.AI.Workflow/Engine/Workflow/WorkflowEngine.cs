namespace Tnzi.AI.Engine.Workflow;

/// <summary>
/// 工作流引擎 — 统一协调器，基于 WorkflowGraph 拓扑排序 + 就绪队列模型执行工作流
/// </summary>
/// <remarks>
/// <para>
/// 职责：遍历 WorkflowGraph，逐层执行就绪节点（委托给 WorkflowNodeExecutor），
/// 处理条件边路由、循环检测、检查点和人工审批中断。
/// </para>
/// <para>
/// 条件边和循环场景自动启用 Run tracking（强制 Trace）。
/// </para>
/// </remarks>
public partial class WorkflowEngine
{
    private readonly WorkflowNodeExecutor _nodeExecutor;
    private readonly ILogger<WorkflowEngine> _logger;

    public WorkflowEngine(WorkflowNodeExecutor nodeExecutor, ILogger<WorkflowEngine> logger)
    {
        _nodeExecutor = Check.NotNull(nodeExecutor);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 执行工作流图
    /// </summary>
    /// <param name="graph">工作流图</param>
    /// <param name="initialInput">初始输入</param>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="options">执行选项（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    public async Task<WorkflowEngineResult> ExecuteAsync(
        WorkflowGraph graph,
        string initialInput,
        IServiceProvider serviceProvider,
        WorkflowExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(graph);
        Check.NotNullOrEmpty(initialInput);
        Check.NotNull(serviceProvider);

        var executionId = options?.ExecutionId ?? Guid.NewGuid().ToString("N");
        var checkpointStore = options?.CheckpointStore;
        var interruptHandler = options?.InterruptHandler;

        // 复杂工作流默认启用 Run tracking：条件边、循环、检查点/HITL 都需要可观测的运行实例
        var hasConditionalEdges = graph.ConditionalEdges.Count > 0;
        var hasLoops = graph.Loops.Count > 0;
        var requiresApproval = graph.Nodes.Any(RequiresHumanApproval);
        var shouldTrackRun = hasConditionalEdges || hasLoops || checkpointStore != null || requiresApproval;
        AgentRun? run = null;
        IRunStore? runStore = null;

        if (shouldTrackRun)
        {
            runStore = serviceProvider.GetService<IRunStore>();
            if (runStore != null)
            {
                run = await GetOrCreateRunAsync(runStore, options, executionId, initialInput, cancellationToken);
            }
        }

        // 从检查点恢复或创建新状态
        var (state, completed, stepResults) = await RestoreOrCreateStateAsync(
            executionId, initialInput, options, checkpointStore, cancellationToken);

        // 跟踪循环迭代次数
        var loopIterations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int totalInputTokens = 0, totalOutputTokens = 0;
        var failed = false;
        var awaitingApproval = false;
        string? awaitingApprovalStepId = null;
        DateTime? checkpointCreatedAt = null;

        var nodeOrderIndex = graph.Nodes
            .Select((node, index) => new { node.StepId, Index = index })
            .Where(x => !string.IsNullOrWhiteSpace(x.StepId))
            .ToDictionary(x => x.StepId!, x => x.Index, StringComparer.OrdinalIgnoreCase);

        while (completed.Count < graph.Nodes.Count)
        {
            // 获取就绪节点
            var readyNodes = graph.GetReadyNodes(completed);
            if (readyNodes.Count == 0) break;

            // 并行执行就绪节点
            var executionTasks = readyNodes.Select(async step =>
            {
                var stepId = step.StepId!;
                var nodeRecord = await EnsureRunNodeAsync(
                    runStore,
                    run,
                    step,
                    nodeOrderIndex.GetValueOrDefault(stepId),
                    BuildNodeInputSummary(step, state),
                    cancellationToken);

                // 评估条件
                if (!string.IsNullOrWhiteSpace(step.Condition))
                {
                    var evaluatedCondition = state.ResolveTemplate(step.Condition);
                    if (!EvaluateCondition(evaluatedCondition))
                    {
                        var skippedResult = new WorkflowNodeResult
                        {
                            Output = string.Empty,
                            IsSuccess = true
                        };

                        await UpdateRunNodeAsync(runStore, nodeRecord, AgentRunNodeStatus.Skipped, skippedResult, null, cancellationToken);
                        return (stepId, nodeRecord, result: skippedResult, skipped: true);
                    }
                }

                if (nodeRecord != null)
                {
                    nodeRecord.Status = AgentRunNodeStatus.Running;
                    await runStore!.UpdateNodeAsync(nodeRecord, cancellationToken);
                }

                var result = await _nodeExecutor.ExecuteAsync(step, state, run, cancellationToken);
                return (stepId, nodeRecord, result, skipped: false);
            }).ToList();

            var results = await Task.WhenAll(executionTasks);

            foreach (var (stepId, nodeRecord, result, skipped) in results)
            {
                completed.Add(stepId);
                state.SetOutput(stepId, result.Output);

                stepResults.Add(new WorkflowStepResultDto
                {
                    StepId = stepId,
                    Output = result.Output.Text,
                    Skipped = skipped
                });

                // 聚合 token 用量
                if (result.Usage != null)
                {
                    totalInputTokens += result.Usage.InputTokens;
                    totalOutputTokens += result.Usage.OutputTokens;
                }

                if (!result.IsSuccess)
                {
                    failed = true;
                    _logger.LogWarning("Workflow node '{StepId}' failed: {Error}", stepId, result.Error);
                }

                 if (!skipped)
                 {
                     var nodeStatus = result.AwaitingApproval
                         ? AgentRunNodeStatus.AwaitingApproval
                         : result.IsSuccess
                             ? AgentRunNodeStatus.Completed
                             : AgentRunNodeStatus.Failed;

                     await UpdateRunNodeAsync(runStore, nodeRecord, nodeStatus, result, result.Error, cancellationToken);
                 }

                // 处理节点级审批暂停（ApprovalNode 返回 AwaitingApproval=true）
                if (result.AwaitingApproval && !failed)
                {
                    awaitingApproval = true;
                    awaitingApprovalStepId = stepId;
                    if (checkpointStore != null)
                    {
                        checkpointCreatedAt ??= DateTime.UtcNow;
                        await SaveCheckpointAsync(checkpointStore, executionId, state, completed,
                            WorkflowExecutionStatus.AwaitingApproval, [stepId], checkpointCreatedAt, cancellationToken);
                    }
                }

                // 处理条件边路由
                if (!skipped && !failed)
                {
                    await HandleConditionalEdgeAsync(graph, stepId, result, state, completed, runStore, run, nodeOrderIndex, cancellationToken);
                }

                // 处理循环
                if (!skipped && !failed)
                {
                    HandleLoop(graph, stepId, state, completed, loopIterations);
                }
            }

            // 如果有节点请求审批暂停，跳出主循环
            if (awaitingApproval) break;

            // 默认失败策略：Fail Fast。
            // 当前 DTO/配置尚未公开 ContinueOnError 等策略，因此只要本层有节点失败，
            // 就在处理完当前就绪层后终止后续节点执行，避免把“失败后继续跑”变成隐式行为。
            if (failed) break;

            // HITL：检查本层是否有步骤需要人工审批
            if (!failed)
            {
                var approvalNodes = readyNodes.Where(s => s.RequiresApproval).ToList();
                foreach (var approvalNode in approvalNodes)
                {
                    var approvalStepId = approvalNode.StepId!;
                    var stepOutput = state.GetOutput(approvalStepId)?.Text ?? string.Empty;

                    if (interruptHandler != null)
                    {
                        var interruptResult = await interruptHandler.HandleInterruptAsync(new WorkflowInterruptContext
                        {
                            ExecutionId = executionId,
                            StepId = approvalStepId,
                            AgentName = approvalNode.StepId ?? "unknown",
                            StepOutput = stepOutput
                        }, cancellationToken);

                        if (!interruptResult.Approved)
                        {
                            var rejectionOutput = new WorkflowStepOutput
                            {
                                Text = $"[Rejected: {interruptResult.Feedback ?? "No feedback provided"}]"
                            };
                            state.SetOutput(approvalStepId, rejectionOutput);
                            UpdateStepResult(stepResults, approvalStepId, rejectionOutput.Text);
                            await UpdateRunNodeAsync(runStore, run?.Nodes.FirstOrDefault(n =>
                                string.Equals(n.NodeName, approvalStepId, StringComparison.OrdinalIgnoreCase)),
                                AgentRunNodeStatus.Rejected,
                                new WorkflowNodeResult { Output = rejectionOutput, IsSuccess = false, Error = interruptResult.Feedback },
                                interruptResult.Feedback,
                                cancellationToken);
                            failed = true;
                        }
                        else if (interruptResult.ModifiedInput != null)
                        {
                            state.SetOutput(approvalStepId, interruptResult.ModifiedInput);
                            UpdateStepResult(stepResults, approvalStepId, interruptResult.ModifiedInput);
                        }
                    }
                    else if (checkpointStore != null)
                    {
                        awaitingApproval = true;
                        awaitingApprovalStepId = approvalStepId;
                        await UpdateRunNodeAsync(runStore, run?.Nodes.FirstOrDefault(n =>
                            string.Equals(n.NodeName, approvalStepId, StringComparison.OrdinalIgnoreCase)),
                            AgentRunNodeStatus.AwaitingApproval,
                            new WorkflowNodeResult { Output = state.GetOutput(approvalStepId) ?? string.Empty, AwaitingApproval = true },
                            null,
                            cancellationToken);
                        checkpointCreatedAt ??= DateTime.UtcNow;
                        await SaveCheckpointAsync(checkpointStore, executionId, state, completed,
                            WorkflowExecutionStatus.AwaitingApproval, [approvalStepId], checkpointCreatedAt, cancellationToken);
                        break;
                    }
                }

                if (awaitingApproval) break;
            }

            // 每层完成后保存检查点
            if (checkpointStore != null && !awaitingApproval)
            {
                var status = failed ? WorkflowExecutionStatus.Failed : (completed.Count >= graph.Nodes.Count ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running);
                checkpointCreatedAt ??= DateTime.UtcNow;
                await SaveCheckpointAsync(checkpointStore, executionId, state, completed, status, null, checkpointCreatedAt, cancellationToken);
            }
        }

        // 最终检查点
        if (checkpointStore != null && !awaitingApproval)
        {
            checkpointCreatedAt ??= DateTime.UtcNow;
            var finalStatus = failed ? WorkflowExecutionStatus.Failed : WorkflowExecutionStatus.Completed;
            await SaveCheckpointAsync(checkpointStore, executionId, state, completed, finalStatus, null, checkpointCreatedAt, cancellationToken);
        }

        // 更新 Run 状态
        if (run != null)
        {
            runStore ??= serviceProvider.GetService<IRunStore>();
            if (runStore != null)
            {
                run.Status = failed ? AgentRunStatus.Failed : (awaitingApproval ? AgentRunStatus.AwaitingApproval : AgentRunStatus.Completed);
                run.TotalInputTokens = totalInputTokens;
                run.TotalOutputTokens = totalOutputTokens;
                run.OutputSummary = stepResults.LastOrDefault(r => !r.Skipped)?.Output;
                await runStore.UpdateAsync(run, cancellationToken);
            }
        }

        var finalOutput = stepResults
            .Where(r => !r.Skipped)
            .LastOrDefault()?.Output ?? initialInput;

        return new WorkflowEngineResult
        {
            ExecutionId = executionId,
            FinalOutput = finalOutput,
            StepResults = stepResults,
            State = state,
            Usage = totalInputTokens > 0 || totalOutputTokens > 0
                ? new TokenUsageDto
                {
                    InputTokens = totalInputTokens,
                    OutputTokens = totalOutputTokens,
                    TotalTokens = totalInputTokens + totalOutputTokens
                }
                : null,
            HasFailure = failed,
            AwaitingApproval = awaitingApproval,
            AwaitingApprovalStepId = awaitingApprovalStepId,
            RunId = run?.Id
        };
    }

}

/// <summary>
/// 工作流引擎执行结果
/// </summary>
public class WorkflowEngineResult
{
    /// <summary>执行实例 ID</summary>
    public string ExecutionId { get; init; } = string.Empty;

    /// <summary>最终输出文本</summary>
    public string FinalOutput { get; init; } = string.Empty;

    /// <summary>各步骤执行结果</summary>
    public List<WorkflowStepResultDto> StepResults { get; init; } = [];

    /// <summary>工作流状态</summary>
    public WorkflowState State { get; init; } = null!;

    /// <summary>聚合 Token 用量</summary>
    public TokenUsageDto? Usage { get; init; }

    /// <summary>是否存在失败节点</summary>
    public bool HasFailure { get; init; }

    /// <summary>是否因等待审批而暂停</summary>
    public bool AwaitingApproval { get; init; }

    /// <summary>等待审批的步骤 ID</summary>
    public string? AwaitingApprovalStepId { get; init; }

    /// <summary>关联的 Run ID（条件边/循环时自动创建）</summary>
    public Guid? RunId { get; init; }

    /// <summary>
    /// 根据执行结果推导状态文本（用于 DTO 层）
    /// </summary>
    public string StatusText => AwaitingApproval
        ? nameof(WorkflowExecutionStatus.AwaitingApproval)
        : HasFailure
            ? nameof(WorkflowExecutionStatus.Failed)
            : nameof(WorkflowExecutionStatus.Completed);
}
