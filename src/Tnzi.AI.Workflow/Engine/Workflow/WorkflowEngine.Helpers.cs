namespace Tnzi.AI.Engine.Workflow;

/// <summary>
/// WorkflowEngine — 条件边路由、循环处理、检查点、Run 节点管理等辅助方法
/// </summary>
public partial class WorkflowEngine
{
    /// <summary>
    /// 处理节点级审批暂停和通用中断
    /// </summary>
    private async Task<(bool awaitingApproval, string? awaitingApprovalStepId, WorkflowInterrupt? awaitingInterrupt, DateTime? checkpointCreatedAt)>
        HandleApprovalInterruptAsync(
            WorkflowNodeResult result,
            string stepId,
            bool failed,
            bool awaitingApproval,
            string? awaitingApprovalStepId,
            WorkflowInterrupt? awaitingInterrupt,
            IWorkflowCheckpointStore? checkpointStore,
            string executionId,
            WorkflowState state,
            HashSet<string> completed,
            DateTime? checkpointCreatedAt,
            CancellationToken cancellationToken)
    {
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

        // 处理通用中断（CheckInterruptAsync 返回 AwaitingInterrupt）
        if (result.AwaitingInterrupt != null && !failed && !awaitingApproval)
        {
            awaitingInterrupt = result.AwaitingInterrupt;

            // Approval 类型中断保持向后兼容
            if (awaitingInterrupt.Type == InterruptType.Approval)
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
            else if (checkpointStore != null)
            {
                checkpointCreatedAt ??= DateTime.UtcNow;
                await SaveCheckpointWithInterruptAsync(checkpointStore, executionId, state, completed,
                    awaitingInterrupt, checkpointCreatedAt, cancellationToken);
            }
        }

        return (awaitingApproval, awaitingApprovalStepId, awaitingInterrupt, checkpointCreatedAt);
    }

    /// <summary>
    /// 构建最终执行结果（最终检查点 + Run 状态更新 + 结果对象）
    /// </summary>
    private static async Task<WorkflowEngineResult> BuildFinalResultAsync(
        string executionId,
        string initialInput,
        IServiceProvider serviceProvider,
        WorkflowState state,
        HashSet<string> completed,
        List<WorkflowStepResultDto> stepResults,
        int totalInputTokens,
        int totalOutputTokens,
        bool failed,
        bool cancelled,
        bool awaitingApproval,
        string? awaitingApprovalStepId,
        WorkflowInterrupt? awaitingInterrupt,
        IWorkflowCheckpointStore? checkpointStore,
        DateTime? checkpointCreatedAt,
        AgentRun? run,
        IRunStore? runStore,
        CancellationToken cancellationToken)
    {
        // 最终检查点
        if (checkpointStore != null && !awaitingApproval && awaitingInterrupt == null)
        {
            checkpointCreatedAt ??= DateTime.UtcNow;
            var finalStatus = cancelled
                ? WorkflowExecutionStatus.Cancelled
                : failed
                    ? WorkflowExecutionStatus.Failed
                    : WorkflowExecutionStatus.Completed;
            await SaveCheckpointAsync(checkpointStore, executionId, state, completed, finalStatus, null, checkpointCreatedAt, cancellationToken);
        }

        // 更新 Run 状态
        if (run != null)
        {
            runStore ??= serviceProvider.GetService<IRunStore>();
            if (runStore != null)
            {
                run.Status = failed
                    ? AgentRunStatus.Failed
                    : cancelled
                        ? AgentRunStatus.Cancelled
                        : awaitingApproval || awaitingInterrupt != null
                            ? AgentRunStatus.AwaitingApproval
                            : AgentRunStatus.Completed;
                run.TotalInputTokens = totalInputTokens;
                run.TotalOutputTokens = totalOutputTokens;
                run.OutputSummary = stepResults.LastOrDefault(r => !r.Skipped)?.Output;
                run.LastHeartbeatAt = DateTime.UtcNow;
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
            Cancelled = cancelled,
            AwaitingApproval = awaitingApproval,
            AwaitingApprovalStepId = awaitingApprovalStepId,
            AwaitingInterrupt = awaitingInterrupt,
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
    /// 简单条件评估（fail-closed）：
    /// 空/纯空白 = 无条件 → 执行；非空时仅白名单 "true"/"1"/"yes" 判真；
    /// 含未解析模板占位符（{{...}}）或任意其他文本（如 LLM 自由输出）一律判假并告警。
    /// </summary>
    private bool EvaluateCondition(string condition)
    {
        // 无条件 = 执行（语义不变）
        if (string.IsNullOrWhiteSpace(condition)) return true;

        var trimmed = condition.Trim();

        // 模板未解析（仍含 {{...}}）→ fail-closed 跳过节点
        if (trimmed.Contains("{{", StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Workflow step condition contains unresolved template placeholders; treating as false (fail-closed): {Condition}",
                Truncate(trimmed, 200));
            return false;
        }

        var normalized = trimmed.ToLowerInvariant();
        if (normalized is "true" or "1" or "yes") return true;

        _logger.LogWarning(
            "Workflow step condition did not match the truthy whitelist (true/1/yes); treating as false (fail-closed): {Condition}",
            Truncate(trimmed, 200));
        return false;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "…";

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
            && string.Equals(nodeType, WorkflowNodeTypes.Approval, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetNodeType(WorkflowStepDto step)
    {
        if (step.Configuration != null
            && step.Configuration.TryGetValue("nodeType", out var nodeType)
            && !string.IsNullOrWhiteSpace(nodeType))
        {
            return nodeType;
        }

        return WorkflowNodeTypes.Agent;
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
            NodeKey = step.StepId,
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
        node.AwaitingInputKind = result.AwaitingInterrupt?.Type switch
        {
            InterruptType.Approval => "approval",
            InterruptType.HumanInput => "human_input",
            InterruptType.ExternalEvent => "external_event",
            _ => result.AwaitingApproval ? "approval" : null
        };
        if (result.Usage != null)
        {
            node.InputTokens = result.Usage.InputTokens;
            node.OutputTokens = result.Usage.OutputTokens;
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
        WorkflowExecutionStatus status,
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
    /// 保存包含通用中断信息的检查点
    /// </summary>
    private static async Task SaveCheckpointWithInterruptAsync(
        IWorkflowCheckpointStore store,
        string executionId,
        WorkflowState state,
        HashSet<string> completed,
        WorkflowInterrupt interrupt,
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
            Status = WorkflowExecutionStatus.AwaitingInput,
            PendingInterruptJson = JsonSerializer.Serialize(interrupt, TnziJsonDefaults.Options)
        };
        await store.SaveCheckpointAsync(checkpoint, ct);
    }

    [ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
    private static async Task<WorkflowSignalProcessingResult> ApplyPendingSignalsAsync(
        string executionId,
        IServiceProvider serviceProvider,
        IWorkflowCheckpointStore? checkpointStore,
        WorkflowState state,
        HashSet<string> completed,
        DateTime? checkpointCreatedAt,
        AgentRun? run,
        IRunStore? runStore,
        CancellationToken ct)
    {
        var mailbox = serviceProvider.GetService<IWorkflowExecutionMailbox>();
        if (mailbox == null)
        {
            return WorkflowSignalProcessingResult.None;
        }

        var signals = await mailbox.GetPendingSignalsAsync(executionId, ct);
        if (signals.Count == 0)
        {
            return WorkflowSignalProcessingResult.None;
        }

        var consumedIds = new List<string>();
        var cancelled = false;
        foreach (var signal in signals)
        {
            switch (signal.Type)
            {
                case WorkflowExecutionSignalTypes.Cancel:
                    cancelled = true;
                    consumedIds.Add(signal.SignalId);
                    break;
                case WorkflowExecutionSignalTypes.ResumeInput:
                    consumedIds.Add(signal.SignalId);
                    break;
            }
        }

        if (consumedIds.Count > 0)
        {
            await mailbox.AcknowledgeSignalsAsync(executionId, consumedIds, ct);
        }

        if (!cancelled)
        {
            return WorkflowSignalProcessingResult.None;
        }

        if (checkpointStore != null)
        {
            checkpointCreatedAt ??= DateTime.UtcNow;
            await SaveCheckpointAsync(
                checkpointStore,
                executionId,
                state,
                completed,
                WorkflowExecutionStatus.Cancelled,
                null,
                checkpointCreatedAt,
                ct);
        }

        if (run != null && runStore != null)
        {
            run.Status = AgentRunStatus.Cancelled;
            run.LastHeartbeatAt = DateTime.UtcNow;
            await runStore.UpdateAsync(run, ct);
        }

        return new WorkflowSignalProcessingResult
        {
            Cancelled = true,
            CheckpointCreatedAt = checkpointCreatedAt
        };
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

    [ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
    private sealed class WorkflowSignalProcessingResult
    {
        public static WorkflowSignalProcessingResult None { get; } = new();

        public bool Cancelled { get; init; }

        public DateTime? CheckpointCreatedAt { get; init; }
    }
}
