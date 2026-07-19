namespace Tnzi.AI.Workflow.Engine;

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
    private readonly ILogger<WorkflowEngine> _logger;

    public WorkflowEngine(ILogger<WorkflowEngine> logger)
    {
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
        var cancelled = false;
        var awaitingApproval = false;
        string? awaitingApprovalStepId = null;
        WorkflowInterrupt? awaitingInterrupt = null;
        DateTime? checkpointCreatedAt = null;

        var nodeOrderIndex = graph.Nodes
            .Select((node, index) => new { node.StepId, Index = index })
            .Where(x => !string.IsNullOrWhiteSpace(x.StepId))
            .ToDictionary(x => x.StepId!, x => x.Index, StringComparer.OrdinalIgnoreCase);

        while (completed.Count < graph.Nodes.Count)
        {
            var signalResult = await ApplyPendingSignalsAsync(
                executionId,
                serviceProvider,
                checkpointStore,
                state,
                completed,
                checkpointCreatedAt,
                run,
                runStore,
                cancellationToken);

            checkpointCreatedAt = signalResult.CheckpointCreatedAt ?? checkpointCreatedAt;
            if (signalResult.Cancelled)
            {
                cancelled = true;
                break;
            }

            // 获取就绪节点
            var readyNodes = graph.GetReadyNodes(completed);
            if (readyNodes.Count == 0) break;

            // Phase 1 (sequential, outer scope) — create/track node records, evaluate conditions.
            // All run-store mutations happen here on a single DbContext, never under Task.WhenAll.
            var prepared = new List<NodePreparation>(readyNodes.Count);
            foreach (var step in readyNodes)
            {
                var stepId = step.StepId!;
                var nodeRecord = await EnsureRunNodeAsync(
                    runStore,
                    run,
                    step,
                    nodeOrderIndex.GetValueOrDefault(stepId),
                    BuildNodeInputSummary(step, state),
                    cancellationToken);

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
                        prepared.Add(new NodePreparation(step, stepId, nodeRecord, ResumeData: null, Skipped: true, SkippedResult: skippedResult));
                        continue;
                    }
                }

                if (nodeRecord != null)
                {
                    nodeRecord.Status = AgentRunNodeStatus.Running;
                    await runStore!.UpdateNodeAsync(nodeRecord, cancellationToken);
                }

                Dictionary<string, object>? resumeData = null;
                if (options?.ResumeStepId != null
                    && string.Equals(options.ResumeStepId, stepId, StringComparison.OrdinalIgnoreCase)
                    && options.ResumeData != null)
                {
                    resumeData = options.ResumeData;
                }

                prepared.Add(new NodePreparation(step, stepId, nodeRecord, resumeData, Skipped: false, SkippedResult: null));
            }

            // Phase 2 (parallel) — execute the actual node logic.
            // Each task gets its own DI scope so that scoped dependencies (DbContext, ChatClient,
            // tool middlewares, etc.) cannot interfere across concurrent fan-out nodes.
            var executionTasks = prepared
                .Where(p => !p.Skipped)
                .Select(async p =>
                {
                    using var scope = serviceProvider.CreateScope();
                    var scopedExecutor = scope.ServiceProvider.GetRequiredService<WorkflowNodeExecutor>();
                    var result = await scopedExecutor.ExecuteAsync(p.Step, state, run, p.ResumeData, cancellationToken);
                    return (p.StepId, p.NodeRecord, result, skipped: false);
                })
                .ToList();

            var executedResults = await Task.WhenAll(executionTasks);
            var executedById = executedResults.ToDictionary(r => r.StepId, StringComparer.OrdinalIgnoreCase);

            // Phase 3 (sequential) — merge skipped + executed in ready-order for deterministic
            // post-processing (state writes, conditional edges, loops, run-store updates).
            var results = prepared
                .Select(p => p.Skipped
                    ? (p.StepId, p.NodeRecord, p.SkippedResult!, skipped: true)
                    : executedById[p.StepId])
                .ToArray();

            foreach (var (stepId, nodeRecord, result, skipped) in results)
            {
                completed.Add(stepId);

                // 节点级错误策略：在失败时决定输出内容与是否中止
                var step = prepared.First(p => string.Equals(p.StepId, stepId, StringComparison.OrdinalIgnoreCase)).Step;
                var effectiveResult = result;
                if (!result.IsSuccess && !skipped)
                {
                    switch (step.OnError)
                    {
                        case NodeErrorPolicy.Skip:
                            // 跳过：空输出，不中止工作流
                            effectiveResult = new WorkflowNodeResult
                            {
                                Output = string.Empty,
                                IsSuccess = true,
                                Error = result.Error,
                                DurationMs = result.DurationMs
                            };
                            _logger.LogWarning("Workflow node '{StepId}' failed (OnError=Skip, continuing): {Error}", stepId, result.Error);
                            break;

                        case NodeErrorPolicy.Continue:
                            // Continue：以错误文本作为输出，不中止工作流
                            effectiveResult = new WorkflowNodeResult
                            {
                                Output = result.Output.Text.Length > 0 ? result.Output : new WorkflowStepOutput { Text = result.Error ?? string.Empty },
                                IsSuccess = true,
                                Error = result.Error,
                                DurationMs = result.DurationMs,
                                Usage = result.Usage
                            };
                            _logger.LogWarning("Workflow node '{StepId}' failed (OnError=Continue, continuing): {Error}", stepId, result.Error);
                            break;

                        default:
                            // Fail（默认）：中止工作流
                            failed = true;
                            _logger.LogWarning("Workflow node '{StepId}' failed: {Error}", stepId, result.Error);
                            break;
                    }
                }

                state.SetOutput(stepId, effectiveResult.Output);

                stepResults.Add(new WorkflowStepResultDto
                {
                    StepId = stepId,
                    Output = effectiveResult.Output.Text,
                    Skipped = skipped
                });

                // 聚合 token 用量
                if (effectiveResult.Usage != null)
                {
                    totalInputTokens += effectiveResult.Usage.InputTokens;
                    totalOutputTokens += effectiveResult.Usage.OutputTokens;
                }

                 if (!skipped)
                 {
                     // 使用原始结果决定 RunNode 状态（Skip/Continue 策略下原始失败也记录为 Completed，让 Error 字段保留）
                     var nodeStatus = result.AwaitingApproval
                         ? AgentRunNodeStatus.AwaitingApproval
                         : result.AwaitingInterrupt != null
                             ? AgentRunNodeStatus.AwaitingApproval // 通用中断也使用 AwaitingApproval 状态
                             : effectiveResult.IsSuccess
                                 ? AgentRunNodeStatus.Completed
                                 : AgentRunNodeStatus.Failed;

                     await UpdateRunNodeAsync(runStore, nodeRecord, nodeStatus, effectiveResult, result.Error, cancellationToken);
                 }

                // 处理节点级审批暂停和通用中断
                (awaitingApproval, awaitingApprovalStepId, awaitingInterrupt, checkpointCreatedAt) =
                    await HandleApprovalInterruptAsync(
                        effectiveResult, stepId, failed, awaitingApproval, awaitingApprovalStepId, awaitingInterrupt,
                        checkpointStore, executionId, state, completed, checkpointCreatedAt, cancellationToken);

                // 处理条件边路由
                if (!skipped && !failed)
                {
                    await HandleConditionalEdgeAsync(graph, stepId, effectiveResult, state, completed, runStore, run, nodeOrderIndex, cancellationToken);
                }

                // 处理循环
                if (!skipped && !failed)
                {
                    HandleLoop(graph, stepId, state, completed, loopIterations);
                }
            }

            // 如果有节点请求审批暂停或通用中断，跳出主循环
            if (awaitingApproval || awaitingInterrupt != null) break;

            // 节点策略为 Fail 时，失败后终止工作流（Skip/Continue 策略的节点已在上方将 failed 置 false）
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
            if (checkpointStore != null && !awaitingApproval && awaitingInterrupt == null)
            {
                var status = failed ? WorkflowExecutionStatus.Failed : (completed.Count >= graph.Nodes.Count ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running);
                checkpointCreatedAt ??= DateTime.UtcNow;
                await SaveCheckpointAsync(checkpointStore, executionId, state, completed, status, null, checkpointCreatedAt, cancellationToken);
            }
        }

        return await BuildFinalResultAsync(
            executionId, initialInput, serviceProvider, state, completed, stepResults,
            totalInputTokens, totalOutputTokens, failed, cancelled, awaitingApproval, awaitingApprovalStepId,
            awaitingInterrupt, checkpointStore, checkpointCreatedAt, run, runStore, cancellationToken);
    }

    /// <summary>
    /// Node preparation result captured in the sequential phase before parallel execution.
    /// Carries either the prepared step (to execute in its own scope) or a pre-computed skipped result.
    /// </summary>
    private sealed record NodePreparation(
        WorkflowStepDto Step,
        string StepId,
        AgentRunNode? NodeRecord,
        Dictionary<string, object>? ResumeData,
        bool Skipped,
        WorkflowNodeResult? SkippedResult);

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

    /// <summary>是否已取消</summary>
    public bool Cancelled { get; init; }

    /// <summary>是否因等待审批而暂停</summary>
    public bool AwaitingApproval { get; init; }

    /// <summary>等待审批的步骤 ID</summary>
    public string? AwaitingApprovalStepId { get; init; }

    /// <summary>关联的 Run ID（条件边/循环时自动创建）</summary>
    public Guid? RunId { get; init; }

    /// <summary>
    /// 当前等待中的通用中断（如人工输入、外部事件等）
    /// </summary>
    [ExperimentalApi(Reason = "Generic workflow interrupt is in preview")]
    public WorkflowInterrupt? AwaitingInterrupt { get; init; }

    /// <summary>
    /// 根据执行结果推导状态文本（用于 DTO 层）
    /// </summary>
    public string StatusText => AwaitingInterrupt != null
        ? nameof(WorkflowExecutionStatus.AwaitingInput)
        : AwaitingApproval
            ? nameof(WorkflowExecutionStatus.AwaitingApproval)
            : Cancelled
                ? nameof(WorkflowExecutionStatus.Cancelled)
            : HasFailure
                ? nameof(WorkflowExecutionStatus.Failed)
                : nameof(WorkflowExecutionStatus.Completed);
}
