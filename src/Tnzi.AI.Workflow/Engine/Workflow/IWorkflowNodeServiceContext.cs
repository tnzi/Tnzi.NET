namespace Tnzi.AI.Workflow.Engine;

/// <summary>
/// 工作流节点服务上下文 — 封装节点运行时的依赖解析，避免直接使用 IServiceProvider。
/// </summary>
public interface IWorkflowNodeServiceContext
{
    /// <summary>Agent 工厂</summary>
    IAgentFactory AgentFactory { get; }

    /// <summary>Agent 仓储（可选，用于解析 Agent 配置）</summary>
    IRepository<Agent, Guid>? AgentRepository { get; }

    /// <summary>创建隔离的服务作用域（用于并行节点的 DbContext 隔离）</summary>
    IServiceScope CreateScope();
}
