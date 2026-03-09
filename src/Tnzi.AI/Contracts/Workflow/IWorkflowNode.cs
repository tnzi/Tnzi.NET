namespace Tnzi.AI.Contracts.Workflow;

/// <summary>
/// 工作流节点接口 — 定义可扩展的节点执行逻辑
/// </summary>
/// <remarks>
/// 通过实现此接口，可添加自定义节点类型（review、router、synthesize 等）。
/// 每个节点通过 <see cref="NodeType"/> 标识，由 WorkflowNodeExecutor 根据类型分发。
/// </remarks>
public interface IWorkflowNode
{
    /// <summary>节点类型标识（如 "agent", "review", "router", "transform" 等）</summary>
    string NodeType { get; }

    /// <summary>
    /// 执行节点逻辑
    /// </summary>
    /// <param name="context">节点执行上下文（含步骤定义、状态、依赖输出等）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>节点执行结果</returns>
    Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default);
}
