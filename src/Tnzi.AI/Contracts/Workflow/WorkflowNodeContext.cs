namespace Tnzi.AI.Contracts.Workflow;

/// <summary>
/// 工作流节点执行上下文 — 节点执行时的环境信息
/// </summary>
public class WorkflowNodeContext
{
    /// <summary>当前步骤定义</summary>
    public required WorkflowStepDto Step { get; init; }

    /// <summary>工作流状态（含所有已完成步骤的输出）</summary>
    public required WorkflowState State { get; init; }

    /// <summary>当前步骤的上游依赖输出</summary>
    public IReadOnlyDictionary<string, WorkflowStepOutput> DependencyOutputs { get; init; } = new Dictionary<string, WorkflowStepOutput>();

    /// <summary>服务提供者（用于解析 AgentFactory 等服务）</summary>
    public required IServiceProvider ServiceProvider { get; init; }

    /// <summary>关联的运行实例（可选，用于 Trace 记录）</summary>
    public AgentRun? Run { get; init; }
}
