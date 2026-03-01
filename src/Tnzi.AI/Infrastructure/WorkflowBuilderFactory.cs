

namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 工作流构建器工厂 - 从工作流定义构建 AgentExecutor 列表
/// </summary>
public class WorkflowBuilderFactory : IWorkflowBuilderFactory
{
    private readonly IAgentFactory _agentFactory;
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly ILogger<WorkflowBuilderFactory> _logger;

    public WorkflowBuilderFactory(
        IAgentFactory agentFactory,
        IRepository<Agent, Guid> agentRepository,
        ILogger<WorkflowBuilderFactory> logger)
    {
        _agentFactory = Check.NotNull(agentFactory);
        _agentRepository = Check.NotNull(agentRepository);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 从工作流定义构建 AgentExecutor 列表和执行模式
    /// </summary>
    public async Task<(IReadOnlyList<AgentExecutor> Agents, WorkflowExecutionMode ExecutionMode)> BuildWorkflowAsync(
        WorkflowDefinition workflowDef,
        CancellationToken ct = default)
    {
        Check.NotNull(workflowDef);

        var steps = JsonSerializer.Deserialize<List<WorkflowStepDefinition>>(workflowDef.Steps)
            ?? throw new InvalidOperationException("Invalid workflow steps");

        if (steps.Count == 0)
        {
            throw new InvalidOperationException("Workflow must have at least one step");
        }

        var agentList = new List<AgentExecutor>();
        foreach (var step in steps.OrderBy(s => s.Order))
        {
            var agent = await BuildAgentFromStepAsync(step, ct);
            if (agent != null) agentList.Add(agent);
        }

        if (agentList.Count == 0)
        {
            throw new InvalidOperationException("Workflow must have at least one agent");
        }

        return (agentList.AsReadOnly(), workflowDef.ExecutionMode);
    }

    /// <summary>
    /// 从工作流定义构建 DAG 步骤列表（DAG 模式专用）
    /// </summary>
    public async Task<IReadOnlyList<DagStep>> BuildDagStepsAsync(WorkflowDefinition workflowDef, CancellationToken ct = default)
    {
        Check.NotNull(workflowDef);

        var steps = JsonSerializer.Deserialize<List<WorkflowStepDefinition>>(workflowDef.Steps)
            ?? throw new InvalidOperationException("Invalid workflow steps");

        if (steps.Count == 0)
        {
            throw new InvalidOperationException("Workflow must have at least one step");
        }

        // 为没有 StepId 的步骤自动生成 ID
        for (var i = 0; i < steps.Count; i++)
        {
            steps[i].StepId ??= $"step_{i + 1}";
        }

        var dagSteps = new List<DagStep>();
        foreach (var step in steps)
        {
            var agent = await BuildAgentFromStepAsync(step, ct);
            if (agent == null) continue;

            dagSteps.Add(new DagStep
            {
                StepId = step.StepId!,
                Agent = agent,
                DependsOn = step.DependsOn,
                Condition = step.Condition
            });
        }

        if (dagSteps.Count == 0)
        {
            throw new InvalidOperationException("Workflow must have at least one agent");
        }

        return dagSteps.AsReadOnly();
    }

    /// <summary>
    /// 从步骤定义构建单个 AgentExecutor
    /// </summary>
    private async Task<AgentExecutor?> BuildAgentFromStepAsync(WorkflowStepDefinition step, CancellationToken ct)
    {
        if (!step.AgentId.HasValue) return null;

        var agentDef = await _agentRepository.GetAsync(step.AgentId.Value, ct);
        if (agentDef == null || agentDef.IsDeleted)
        {
            throw new InvalidOperationException($"Agent '{step.AgentId.Value}' not found");
        }

        var toolGroups = string.IsNullOrWhiteSpace(agentDef.ToolGroups)
            ? null
            : JsonSerializer.Deserialize<List<string>>(agentDef.ToolGroups);

        return await _agentFactory.CreateAgentAsync(
            providerName: step.Provider ?? agentDef.Provider,
            model: step.Model ?? agentDef.Model,
            instructions: step.Instructions ?? agentDef.Instructions,
            name: agentDef.Name,
            toolGroups: toolGroups,
            ct: ct).ConfigureAwait(false);
    }
}

/// <summary>
/// 工作流步骤定义（内部使用，避免与 Dtos.WorkflowStepDto 冲突）
/// </summary>
internal class WorkflowStepDefinition
{
    /// <summary>
    /// 步骤唯一 ID（DAG 模式下用于依赖引用；未设置时自动生成）
    /// </summary>
    public string? StepId { get; set; }

    /// <summary>
    /// Agent ID
    /// </summary>
    public Guid? AgentId { get; set; }

    /// <summary>
    /// 顺序（Sequential/Parallel 模式使用）
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 前置步骤 ID 列表（DAG 模式：当前步骤的所有依赖完成后才执行）
    /// </summary>
    public List<string>? DependsOn { get; set; }

    /// <summary>
    /// 执行条件表达式（为空则无条件执行；支持 {{stepId}} 引用步骤输出）
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// 自定义 Provider（覆盖 Agent 默认值）
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 自定义 Model（覆盖 Agent 默认值）
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 自定义 Instructions（覆盖 Agent 默认值，支持 {{stepId}} 模板变量）
    /// </summary>
    public string? Instructions { get; set; }
}
