namespace Tnzi.AI.Workflow.Services;

/// <summary>
/// 工作流服务 - 管理操作 (Clone/BatchDelete/BatchEnable/BatchDisable/GetStats/Validate)
/// </summary>
public partial class WorkflowService
{
    public async Task<Result<WorkflowDefinitionDto>> CloneAsync(Guid id, string? newName = null)
    {
        var source = await _repository.GetAsync(id);
        if (source == null)
            return Fail<WorkflowDefinitionDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);

        var clone = new WorkflowDefinition
        {
            Name = newName ?? $"{source.Name} (Copy)",
            Description = source.Description,
            Steps = source.Steps,
            ExecutionMode = source.ExecutionMode,
            IsEnabled = source.IsEnabled,
            Configuration = source.Configuration
        };

        await _repository.InsertAsync(clone);
        Logger.LogInformation("Workflow cloned: {SourceId} -> {CloneId}, Name: {Name}", id, clone.Id, clone.Name);
        return Ok(MapToDto(clone));
    }

    public async Task<Result<int>> BatchDeleteAsync(List<Guid> ids)
    {
        Check.NotNullOrEmpty(ids);
        var deleted = await _repository.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync();
        return Ok(deleted);
    }

    public async Task<Result<int>> BatchSetEnabledAsync(List<Guid> ids, bool enabled)
    {
        Check.NotNullOrEmpty(ids);
        var updated = await _repository
            .Where(e => ids.Contains(e.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsEnabled, enabled));
        return Ok(updated);
    }

    public async Task<Result<WorkflowStatsDto>> GetStatsAsync()
    {
        var defStats = await _repository.AsQueryable()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Enabled = g.Count(e => e.IsEnabled),
                Disabled = g.Count(e => !e.IsEnabled)
            })
            .FirstOrDefaultAsync();

        var modeStats = await _repository.AsQueryable()
            .GroupBy(e => e.ExecutionMode)
            .Select(g => new { Mode = g.Key, Count = g.Count() })
            .ToListAsync();

        var execStats = await _executionRepository.AsQueryable()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Running = g.Count(e => e.Status == WorkflowExecutionStatus.Running),
                Completed = g.Count(e => e.Status == WorkflowExecutionStatus.Completed),
                Failed = g.Count(e => e.Status == WorkflowExecutionStatus.Failed)
            })
            .FirstOrDefaultAsync();

        return Ok(new WorkflowStatsDto
        {
            TotalWorkflows = defStats?.Total ?? 0,
            EnabledWorkflows = defStats?.Enabled ?? 0,
            DisabledWorkflows = defStats?.Disabled ?? 0,
            ByExecutionMode = modeStats.ToDictionary(m => m.Mode.ToString(), m => m.Count),
            TotalExecutions = execStats?.Total ?? 0,
            RunningExecutions = execStats?.Running ?? 0,
            CompletedExecutions = execStats?.Completed ?? 0,
            FailedExecutions = execStats?.Failed ?? 0
        });
    }

    /// <summary>
    /// 条件边 / 循环引用的步骤必须存在；路由类节点（router / conditional / review）没有条件边时其 <c>RouteTo</c>
    /// 会被引擎静默丢弃、所有分支照跑 —— 那正是本 DTO 存在之前每一条 API 工作流的形态，所以要报出来。
    /// </summary>
    private static void ValidateGraphConfiguration(
        WorkflowDefinition entity, List<WorkflowStepDto> steps, HashSet<string> stepIds, WorkflowValidationResultDto result)
    {
        var configuration = TryDeserializeConfiguration(entity.Configuration);
        if (!string.IsNullOrWhiteSpace(entity.Configuration) && configuration is null)
        {
            result.IsValid = false;
            result.Errors.Add("Invalid graph configuration JSON.");
            return;
        }

        var edgeSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in configuration?.ConditionalEdges ?? [])
        {
            edgeSources.Add(edge.FromNodeId);
            if (!stepIds.Contains(edge.FromNodeId))
            {
                result.IsValid = false;
                result.Errors.Add($"Conditional edge starts at non-existent step '{edge.FromNodeId}'.");
            }

            foreach (var (routeKey, target) in edge.Routes)
            {
                if (!stepIds.Contains(target))
                {
                    result.IsValid = false;
                    result.Errors.Add($"Conditional edge from '{edge.FromNodeId}' routes '{routeKey}' to non-existent step '{target}'.");
                }
            }

            if (!string.IsNullOrWhiteSpace(edge.DefaultTarget) && !stepIds.Contains(edge.DefaultTarget))
            {
                result.IsValid = false;
                result.Errors.Add($"Conditional edge from '{edge.FromNodeId}' has non-existent default target '{edge.DefaultTarget}'.");
            }
        }

        foreach (var (loopId, loop) in configuration?.Loops ?? [])
        {
            foreach (var nodeId in loop.NodeIds.Where(n => !stepIds.Contains(n)))
            {
                result.IsValid = false;
                result.Errors.Add($"Loop '{loopId}' references non-existent step '{nodeId}'.");
            }

            if (loop.MaxIterations < 1)
            {
                result.IsValid = false;
                result.Errors.Add($"Loop '{loopId}' must allow at least one iteration.");
            }
        }

        foreach (var step in steps)
        {
            var nodeType = WorkflowStepNodeType.Get(step);
            var routes = nodeType is not null
                && (string.Equals(nodeType, WorkflowNodeTypes.Router, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(nodeType, WorkflowNodeTypes.Conditional, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(nodeType, WorkflowNodeTypes.Review, StringComparison.OrdinalIgnoreCase));
            if (routes && !edgeSources.Contains(step.StepId!))
            {
                result.Warnings.Add($"Step '{step.StepId}' ({nodeType}) returns a route but has no conditional edge; its route is ignored and every dependent branch runs.");
            }
        }
    }

    public async Task<Result<WorkflowValidationResultDto>> ValidateAsync(Guid workflowId)
    {
        var entity = await _repository.GetAsync(workflowId);
        if (entity == null)
            return Fail<WorkflowValidationResultDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);

        var result = new WorkflowValidationResultDto { IsValid = true };

        // Parse steps
        List<WorkflowStepDto> steps;
        try
        {
            steps = string.IsNullOrWhiteSpace(entity.Steps)
                ? []
                : JsonSerializer.Deserialize<List<WorkflowStepDto>>(entity.Steps, TnziJsonDefaults.Options) ?? [];
        }
        catch (JsonException ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Invalid steps JSON: {ex.Message}");
            return Ok(result);
        }

        if (steps.Count == 0)
        {
            result.Warnings.Add("Workflow has no steps defined.");
            return Ok(result);
        }

        // Assign default step IDs for validation
        for (var i = 0; i < steps.Count; i++)
            steps[i].StepId ??= $"step-{i + 1}";

        var stepIds = new HashSet<string>(steps.Select(s => s.StepId!), StringComparer.OrdinalIgnoreCase);

        // Check for duplicate step IDs
        var duplicates = steps.GroupBy(s => s.StepId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            result.IsValid = false;
            result.Errors.Add($"Duplicate step IDs: {string.Join(", ", duplicates!)}");
        }

        // Check DependsOn references
        foreach (var step in steps)
        {
            if (step.DependsOn == null) continue;
            foreach (var dep in step.DependsOn)
            {
                if (!stepIds.Contains(dep))
                {
                    result.IsValid = false;
                    result.Errors.Add($"Step '{step.StepId}' depends on non-existent step '{dep}'.");
                }
                if (string.Equals(dep, step.StepId, StringComparison.OrdinalIgnoreCase))
                {
                    result.IsValid = false;
                    result.Errors.Add($"Step '{step.StepId}' has a self-dependency.");
                }
            }
        }

        // Check node types against the registered IWorkflowNode set. A typo (or a kind whose
        // sub-module is not loaded) otherwise surfaces only at run time, as an exception from
        // WorkflowNodeExecutor.ResolveNode; an absent key legitimately means "agent".
        var registeredNodeTypes = new HashSet<string>(_workflowNodes.Select(n => n.NodeType), StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            var nodeType = WorkflowStepNodeType.Get(step);
            if (nodeType != null && !registeredNodeTypes.Contains(nodeType))
            {
                result.IsValid = false;
                result.Errors.Add($"Step '{step.StepId}' uses unknown node type '{nodeType}'. Registered node types: {string.Join(", ", registeredNodeTypes.Order(StringComparer.OrdinalIgnoreCase))}.");
            }
        }

        ValidateGraphConfiguration(entity, steps, stepIds, result);

        // Simple cycle detection (topological sort)
        if (entity.ExecutionMode == WorkflowExecutionMode.Dag)
        {
            var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in steps)
            {
                inDegree[step.StepId!] = 0;
                adjacency[step.StepId!] = [];
            }
            foreach (var step in steps)
            {
                if (step.DependsOn == null) continue;
                foreach (var dep in step.DependsOn)
                {
                    if (adjacency.ContainsKey(dep))
                    {
                        adjacency[dep].Add(step.StepId!);
                        inDegree[step.StepId!]++;
                    }
                }
            }

            var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            var visited = 0;
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                visited++;
                foreach (var neighbor in adjacency[node])
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                        queue.Enqueue(neighbor);
                }
            }

            if (visited < steps.Count)
            {
                result.IsValid = false;
                result.Errors.Add("Workflow contains a dependency cycle.");
            }
        }

        // Check agent references
        var agentIds = steps.Where(s => s.AgentId.HasValue).Select(s => s.AgentId!.Value).Distinct().ToList();
        if (agentIds.Count > 0)
        {
            var existingAgentIds = await _agentRepository.AsQueryable()
                .Where(a => agentIds.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync();

            var missingAgentIds = agentIds.Except(existingAgentIds).ToList();
            if (missingAgentIds.Count > 0)
            {
                result.Warnings.Add($"Workflow references {missingAgentIds.Count} non-existent agent(s): {string.Join(", ", missingAgentIds)}");
            }
        }

        if (!entity.IsEnabled)
            result.Warnings.Add("Workflow is currently disabled.");

        return Ok(result);
    }
}
