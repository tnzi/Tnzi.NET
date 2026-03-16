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
public class WorkflowEngine
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

        int totalPromptTokens = 0, totalCompletionTokens = 0;
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
                    totalPromptTokens += result.Usage.PromptTokens;
                    totalCompletionTokens += result.Usage.CompletionTokens;
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
                            "awaiting_approval", [stepId], checkpointCreatedAt, cancellationToken);
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
                            "awaiting_approval", [approvalStepId], checkpointCreatedAt, cancellationToken);
                        break;
                    }
                }

                if (awaitingApproval) break;
            }

            // 每层完成后保存检查点
            if (checkpointStore != null && !awaitingApproval)
            {
                var status = failed ? "failed" : (completed.Count >= graph.Nodes.Count ? "completed" : "running");
                checkpointCreatedAt ??= DateTime.UtcNow;
                await SaveCheckpointAsync(checkpointStore, executionId, state, completed, status, null, checkpointCreatedAt, cancellationToken);
            }
        }

        // 最终检查点
        if (checkpointStore != null && !awaitingApproval)
        {
            checkpointCreatedAt ??= DateTime.UtcNow;
            var finalStatus = failed ? "failed" : "completed";
            await SaveCheckpointAsync(checkpointStore, executionId, state, completed, finalStatus, null, checkpointCreatedAt, cancellationToken);
        }

        // 更新 Run 状态
        if (run != null)
        {
            runStore ??= serviceProvider.GetService<IRunStore>();
            if (runStore != null)
            {
                run.Status = failed ? AgentRunStatus.Failed : (awaitingApproval ? AgentRunStatus.AwaitingApproval : AgentRunStatus.Completed);
                run.TotalInputTokens = totalPromptTokens;
                run.TotalOutputTokens = totalCompletionTokens;
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
            Usage = totalPromptTokens > 0 || totalCompletionTokens > 0
                ? new TokenUsageDto
                {
                    PromptTokens = totalPromptTokens,
                    CompletionTokens = totalCompletionTokens,
                    TotalTokens = totalPromptTokens + totalCompletionTokens
                }
                : null,
            HasFailure = failed,
            AwaitingApproval = awaitingApproval,
            AwaitingApprovalStepId = awaitingApprovalStepId,
            RunId = run?.Id
        };
    }

    /// <summary>
    /// 处理条件边路由
    /// </summary>
    private async Task HandleConditionalEdgeAsync(
        WorkflowGraph graph,
        string fromNodeId,
        WorkflowNodeResult result,
        WorkflowState state,
        HashSet<string> completed,
        IRunStore? runStore,
        AgentRun? run,
        IReadOnlyDictionary<string, int> nodeOrderIndex,
        CancellationToken ct)
    {
        var edge = graph.GetConditionalEdge(fromNodeId);
        if (edge == null) return;

        var outputText = result.Output.Text;
        string? targetNodeId = null;

        // 优先使用节点结果中的 RouteTo
        if (result.RouteTo != null)
        {
            targetNodeId = result.RouteTo;
        }
        else
        {
            targetNodeId = edge.ConditionType switch
            {
                EdgeConditionType.OutputContains => EvaluateOutputContains(edge, outputText),
                EdgeConditionType.OutputEquals => EvaluateOutputEquals(edge, outputText),
                EdgeConditionType.JsonPath => EvaluateJsonPath(edge, outputText),
                _ => edge.DefaultTarget
            };
        }

        if (targetNodeId == null)
        {
            targetNodeId = edge.DefaultTarget;
        }

        if (targetNodeId != null)
        {
            _logger.LogDebug("Conditional edge from '{FromNode}' routing to '{TargetNode}'", fromNodeId, targetNodeId);

            var selectedReachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { targetNodeId };
            foreach (var selectedDownstream in graph.GetTransitiveDownstream(targetNodeId))
            {
                selectedReachable.Add(selectedDownstream);
            }

            var alternateRoots = edge.Routes.Values
                .Append(edge.DefaultTarget)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(x => !string.Equals(x, targetNodeId, StringComparison.OrdinalIgnoreCase));

            foreach (var alternateRoot in alternateRoots)
            {
                var alternateReachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { alternateRoot! };
                foreach (var downstream in graph.GetTransitiveDownstream(alternateRoot!))
                {
                    alternateReachable.Add(downstream);
                }

                foreach (var downstream in alternateReachable)
                {
                    if (selectedReachable.Contains(downstream) || completed.Contains(downstream))
                    {
                        continue;
                    }

                    completed.Add(downstream);
                    _logger.LogDebug("Conditional edge: skipping non-target node '{SkippedNode}'", downstream);

                    if (runStore != null && run != null && graph.GetNode(downstream) != null)
                    {
                        var skippedStep = graph.GetNode(downstream)!;
                        var skippedNode = await EnsureRunNodeAsync(
                            runStore,
                            run,
                            skippedStep,
                            nodeOrderIndex.GetValueOrDefault(downstream),
                            BuildNodeInputSummary(skippedStep, state),
                            ct);

                        await UpdateRunNodeAsync(
                            runStore,
                            skippedNode,
                            AgentRunNodeStatus.Skipped,
                            new WorkflowNodeResult { Output = string.Empty, IsSuccess = true },
                            null,
                            ct);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 处理循环
    /// </summary>
    private void HandleLoop(
        WorkflowGraph graph,
        string nodeId,
        WorkflowState state,
        HashSet<string> completed,
        Dictionary<string, int> loopIterations)
    {
        var (inLoop, loopId) = graph.IsInLoop(nodeId);
        if (!inLoop || loopId == null) return;

        var loopDef = graph.Loops[loopId];
        var lastNodeInLoop = loopDef.NodeIds[^1];

        // 只在循环的最后一个节点完成后检查是否需要继续循环
        if (!string.Equals(nodeId, lastNodeInLoop, StringComparison.OrdinalIgnoreCase)) return;

        var currentIteration = loopIterations.GetValueOrDefault(loopId, 0) + 1;
        loopIterations[loopId] = currentIteration;

        if (currentIteration >= loopDef.MaxIterations)
        {
            _logger.LogInformation("Loop '{LoopId}' reached max iterations ({Max})", loopId, loopDef.MaxIterations);
            return;
        }

        // 检查循环终止条件（通过最后一个节点的输出元数据）
        var lastOutput = state.GetOutput(lastNodeInLoop);
        if (lastOutput?.Metadata != null
            && lastOutput.Metadata.TryGetValue("loop_done", out var doneValue)
            && string.Equals(doneValue, "true", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Loop '{LoopId}' terminated by node output (iteration {Iteration})", loopId, currentIteration);
            return;
        }

        // 重置循环内节点为未完成，使其可以再次执行
        _logger.LogDebug("Loop '{LoopId}' continuing iteration {Iteration}/{Max}", loopId, currentIteration, loopDef.MaxIterations);
        foreach (var loopNodeId in loopDef.NodeIds)
        {
            completed.Remove(loopNodeId);
        }
    }

    /// <summary>
    /// 简单条件评估
    /// </summary>
    private static bool EvaluateCondition(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        var trimmed = condition.Trim().ToLowerInvariant();
        return trimmed is not ("false" or "0" or "skip" or "no");
    }

    private static string? EvaluateOutputContains(ConditionalEdge edge, string output)
    {
        foreach (var (key, targetId) in edge.Routes)
        {
            if (output.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return targetId;
            }
        }
        return null;
    }

    private static string? EvaluateOutputEquals(ConditionalEdge edge, string output)
    {
        var trimmed = output.Trim();
        foreach (var (key, targetId) in edge.Routes)
        {
            if (string.Equals(trimmed, key, StringComparison.OrdinalIgnoreCase))
            {
                return targetId;
            }
        }
        return null;
    }

    private static string? EvaluateJsonPath(ConditionalEdge edge, string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);

            // 简单 JsonPath: Routes 的 Key 格式为 "$.property=value"
            foreach (var (key, targetId) in edge.Routes)
            {
                var parts = key.Split('=', 2);
                if (parts.Length != 2) continue;

                var path = parts[0].TrimStart('$', '.');
                var expectedValue = parts[1];

                if (doc.RootElement.TryGetProperty(path, out var element))
                {
                    var actualValue = element.ToString();
                    if (string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase))
                    {
                        return targetId;
                    }
                }
            }
        }
        catch
        {
            // JSON 解析失败，忽略
        }
        return null;
    }

    private static bool RequiresHumanApproval(WorkflowStepDto step)
    {
        if (step.RequiresApproval) return true;
        return step.Configuration != null
            && step.Configuration.TryGetValue("nodeType", out var nodeType)
            && string.Equals(nodeType, "approval", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetNodeType(WorkflowStepDto step)
    {
        if (step.Configuration != null
            && step.Configuration.TryGetValue("nodeType", out var nodeType)
            && !string.IsNullOrWhiteSpace(nodeType))
        {
            return nodeType;
        }

        return "agent";
    }

    private static string BuildNodeInputSummary(WorkflowStepDto step, WorkflowState state)
    {
        if (step.DependsOn == null || step.DependsOn.Count == 0)
        {
            return state.InitialInput;
        }

        if (step.DependsOn.Count == 1)
        {
            return state.GetOutput(step.DependsOn[0])?.Text ?? state.InitialInput;
        }

        var builder = new StringBuilder();
        foreach (var depId in step.DependsOn)
        {
            var output = state.GetOutput(depId);
            if (output == null || string.IsNullOrWhiteSpace(output.Text)) continue;

            if (builder.Length > 0) builder.AppendLine();
            builder.AppendLine($"[{depId}]");
            builder.AppendLine(output.Text);
        }

        return builder.Length > 0 ? builder.ToString().TrimEnd() : state.InitialInput;
    }

    private static async Task<AgentRun> GetOrCreateRunAsync(
        IRunStore runStore,
        WorkflowExecutionOptions? options,
        string executionId,
        string initialInput,
        CancellationToken ct)
    {
        if (options?.RunId.HasValue == true)
        {
            var existingRun = await runStore.GetWithNodesAsync(options.RunId.Value, ct);
            if (existingRun == null)
            {
                throw new InvalidOperationException($"Workflow run '{options.RunId}' was not found.");
            }

            return existingRun;
        }

        return await runStore.CreateAsync(new AgentRun
        {
            Status = AgentRunStatus.Running,
            ExecutionMode = AgentExecutionMode.Single,
            InputSummary = initialInput.Length > 500 ? initialInput[..500] : initialInput,
            WorkflowExecutionId = executionId,
            WorkflowDefinitionId = options?.WorkflowDefinitionId
        }, ct);
    }

    private static async Task<AgentRunNode?> EnsureRunNodeAsync(
        IRunStore? runStore,
        AgentRun? run,
        WorkflowStepDto step,
        int orderIndex,
        string inputSummary,
        CancellationToken ct)
    {
        if (runStore == null || run == null || string.IsNullOrWhiteSpace(step.StepId))
        {
            return null;
        }

        var existingNode = run.Nodes.FirstOrDefault(n =>
            string.Equals(n.NodeName, step.StepId, StringComparison.OrdinalIgnoreCase));

        if (existingNode != null)
        {
            if (existingNode.InputSummary != inputSummary)
            {
                existingNode.InputSummary = inputSummary;
                await runStore.UpdateNodeAsync(existingNode, ct);
            }

            return existingNode;
        }

        var node = new AgentRunNode
        {
            RunId = run.Id,
            NodeType = GetNodeType(step),
            NodeName = step.StepId,
            AgentId = step.AgentId,
            Status = AgentRunNodeStatus.Pending,
            InputSummary = inputSummary,
            OrderIndex = orderIndex
        };

        await runStore.AddNodeAsync(node, ct);
        run.Nodes.Add(node);
        return node;
    }

    private static async Task UpdateRunNodeAsync(
        IRunStore? runStore,
        AgentRunNode? node,
        AgentRunNodeStatus status,
        WorkflowNodeResult result,
        string? error,
        CancellationToken ct)
    {
        if (runStore == null || node == null) return;

        node.Status = status;
        node.Output = result.Output.Text;
        node.DurationMs = result.DurationMs;
        node.Error = error;
        if (result.Usage != null)
        {
            node.InputTokens = result.Usage.PromptTokens;
            node.OutputTokens = result.Usage.CompletionTokens;
        }

        await runStore.UpdateNodeAsync(node, ct);
    }

    /// <summary>
    /// 从检查点恢复或创建新状态
    /// </summary>
    private static async Task<(WorkflowState state, HashSet<string> completed, List<WorkflowStepResultDto> stepResults)>
        RestoreOrCreateStateAsync(
            string executionId,
            string initialInput,
            WorkflowExecutionOptions? options,
            IWorkflowCheckpointStore? checkpointStore,
            CancellationToken ct)
    {
        if (options?.Resume == true && checkpointStore != null)
        {
            var checkpoint = await checkpointStore.GetCheckpointAsync(executionId, ct);
            if (checkpoint != null)
            {
                var state = WorkflowState.FromCheckpoint(checkpoint);
                var completed = new HashSet<string>(checkpoint.CompletedStepIds, StringComparer.OrdinalIgnoreCase);
                var stepResults = checkpoint.CompletedStepIds
                    .Select(id => new WorkflowStepResultDto
                    {
                        StepId = id,
                        Output = checkpoint.StepOutputs.GetValueOrDefault(id)?.Text ?? string.Empty
                    })
                    .ToList();
                return (state, completed, stepResults);
            }
        }

        return (new WorkflowState(initialInput),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            []);
    }

    /// <summary>
    /// 保存检查点
    /// </summary>
    private static async Task SaveCheckpointAsync(
        IWorkflowCheckpointStore store,
        string executionId,
        WorkflowState state,
        HashSet<string> completed,
        string status,
        HashSet<string>? stepsAwaitingApproval,
        DateTime? createdAt,
        CancellationToken ct)
    {
        var checkpoint = new WorkflowCheckpoint
        {
            ExecutionId = executionId,
            CompletedStepIds = new HashSet<string>(completed),
            StepOutputs = state.ToDictionary(),
            InitialInput = state.InitialInput,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Status = status,
            StepsAwaitingApproval = stepsAwaitingApproval ?? []
        };
        await store.SaveCheckpointAsync(checkpoint, ct);
    }

    /// <summary>
    /// 更新 stepResults 中指定步骤的输出
    /// </summary>
    private static void UpdateStepResult(List<WorkflowStepResultDto> stepResults, string stepId, string output)
    {
        var existing = stepResults.FirstOrDefault(r => string.Equals(r.StepId, stepId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Output = output;
        }
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
}
