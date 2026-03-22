namespace Tnzi.AI.Engine.Workflow;

/// <summary>
/// WorkflowEngine — 条件边路由、循环处理、检查点、Run 节点管理等辅助方法
/// </summary>
public partial class WorkflowEngine
{
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
