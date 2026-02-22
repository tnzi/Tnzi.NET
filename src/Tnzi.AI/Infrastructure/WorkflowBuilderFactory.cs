

namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 工作流构建器工厂 - 从工作流定义构建 AgentExecutor 列表
/// </summary>
public class WorkflowBuilderFactory
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
    /// <returns>Agent 列表和执行模式</returns>
    public async Task<(IReadOnlyList<AgentExecutor> Agents, WorkflowExecutionMode ExecutionMode)> BuildWorkflowAsync(
        WorkflowDefinition workflowDef,
        CancellationToken ct = default)
    {
        Check.NotNull(workflowDef);

        // 解析工作流步骤
        var steps = JsonSerializer.Deserialize<List<WorkflowStepDefinition>>(workflowDef.Steps)
            ?? throw new InvalidOperationException("Invalid workflow steps");

        if (steps.Count == 0)
        {
            throw new InvalidOperationException("Workflow must have at least one step");
        }

        // 加载所有 Agent
        var agentList = new List<AgentExecutor>();
        foreach (var step in steps.OrderBy(s => s.Order))
        {
            if (step.AgentId.HasValue)
            {
                var agentDef = await _agentRepository.GetAsync(step.AgentId.Value, ct);
                if (agentDef == null || agentDef.IsDeleted)
                {
                    throw new InvalidOperationException($"Agent '{step.AgentId.Value}' not found");
                }

                // 解析工具组
                var toolGroups = string.IsNullOrWhiteSpace(agentDef.ToolGroups)
                    ? null
                    : JsonSerializer.Deserialize<List<string>>(agentDef.ToolGroups);

                var agent = await _agentFactory.CreateAgentAsync(
                    providerName: agentDef.Provider,
                    model: agentDef.Model,
                    instructions: agentDef.Instructions,
                    name: agentDef.Name,
                    toolGroups: toolGroups,
                    ct: ct).ConfigureAwait(false);

                agentList.Add(agent);
            }
        }

        if (agentList.Count == 0)
        {
            throw new InvalidOperationException("Workflow must have at least one agent");
        }

        return (agentList.AsReadOnly(), workflowDef.ExecutionMode);
    }
}

/// <summary>
/// 工作流步骤定义（内部使用，避免与 Dtos.WorkflowStepDto 冲突）
/// </summary>
internal class WorkflowStepDefinition
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public Guid? AgentId { get; set; }

    /// <summary>
    /// 顺序
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 条件表达式（可选）
    /// </summary>
    public string? Condition { get; set; }
}
