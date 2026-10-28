namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 当前正在执行的工作流节点：工作流引擎在每个节点执行期间设置，工具权限评估据此认出「这次调用发生在哪个节点」。
/// </summary>
/// <remarks>
/// <para>
/// 节点身份是权限规则的匹配维度（<c>AI:Permissions</c> 的 <c>WorkflowNodeName</c> / <c>IsWorkflowOnly</c>），
/// 所以它只能来自进程内的引擎，不能来自请求体或请求元数据：任何能往元数据里写 <c>node_name</c> 的调用方
/// 都能借此让一条按节点放行的规则对自己生效。
/// </para>
/// <para>
/// 基于 <see cref="AsyncLocal{T}"/>：同一层里并发执行的节点各在自己的执行流里，各自看到自己的节点名，
/// 互不覆盖（共享属性包是同一个字典引用，并发节点往里写名字会互相串）。
/// 节点里另起的运行（agent-as-tool、后台子 Agent）沿执行流继承节点身份：按节点限定的拒绝规则
/// 不能靠把工作委派给子 Agent 绕过去；放行规则随之继承，给出的也只是节点本身已经有的权限。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "AI workflow permission context is evolving")]
public static class WorkflowNodeScope
{
    private static readonly AsyncLocal<WorkflowNodeExecution?> Holder = new();

    /// <summary>当前执行流里正在执行的工作流节点；不在任何节点里时为 null。</summary>
    public static WorkflowNodeExecution? Current => Holder.Value;

    /// <summary>
    /// 标记接下来（直到返回值被释放）的执行属于某个工作流节点。
    /// </summary>
    /// <param name="workflowId">工作流定义 ID</param>
    /// <param name="executionId">工作流执行实例 ID</param>
    /// <param name="nodeName">节点（步骤）名</param>
    /// <returns>释放时恢复进入之前的节点</returns>
    public static IDisposable Enter(Guid? workflowId, string? executionId, string nodeName)
    {
        Check.NotNull(nodeName);

        var previous = Holder.Value;
        Holder.Value = new WorkflowNodeExecution(workflowId, executionId, nodeName);
        return new Restore(previous);
    }

    private sealed class Restore(WorkflowNodeExecution? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Holder.Value = previous;
            }
        }
    }
}

/// <summary>
/// 一次工作流节点执行的身份。
/// </summary>
/// <param name="WorkflowId">工作流定义 ID</param>
/// <param name="ExecutionId">工作流执行实例 ID</param>
/// <param name="NodeName">节点（步骤）名</param>
[ExperimentalApi(Reason = "AI workflow permission context is evolving")]
public sealed record WorkflowNodeExecution(Guid? WorkflowId, string? ExecutionId, string NodeName);
