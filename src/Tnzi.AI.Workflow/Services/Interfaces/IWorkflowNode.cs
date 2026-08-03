namespace Tnzi.AI.Workflow.Services;

/// <summary>
/// 工作流节点接口 - 定义可扩展的节点执行逻辑
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

    /// <summary>
    /// 检查节点是否需要中断（在 ExecuteAsync 之前调用）
    /// </summary>
    /// <remarks>
    /// 默认返回 null（不中断）。节点可重写此方法以请求人工输入、审批或外部事件回调。
    /// 当工作流处于恢复状态（context.IsResuming == true）时，引擎跳过此检查直接执行。
    /// </remarks>
    /// <param name="context">节点执行上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>中断描述（null 表示不中断）</returns>
    Task<WorkflowInterrupt?> CheckInterruptAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        => Task.FromResult<WorkflowInterrupt?>(null);
}
