namespace Tnzi.AI.Workflow.Engine;

/// <summary>
/// 执行中的心跳：引擎在 <see cref="WorkflowEngine.ExecuteAsync"/> 运行的整个期间按 <see cref="Interval"/>
/// 周期调用 <see cref="BeatAsync"/>，证明这个执行实例还活着。
/// </summary>
/// <remarks>
/// <para>
/// ★ 看门狗按 <c>WorkflowExecution.UpdatedTime</c> 判定 Running 超时（默认 30 分钟）。此前没有任何
/// 运行中的写入：Sequential / Parallel 从插入到终态一行不碰，DAG 只在层边界写检查点 —— 而单个节点合法地
/// 可以跑 <c>TimeoutSeconds</c>（上限 3600）× 重试次数。于是一次跑得久的执行会在**还在执行、还在计费**时
/// 被标成 Failed / timed_out，运维追着幻影故障跑。
/// </para>
/// <para>
/// 心跳是**定时器**而不是节点边界：节点边界对一个跑 40 分钟的节点毫无帮助。实现必须能与请求作用域的
/// DbContext 并发调用（在自己的作用域里工作），且只更新时间戳，**不碰乐观并发标记**（检查点保存靠它）。
/// 未注册实现时引擎不发心跳（行为与 2026-09-12 之前相同）。
/// </para>
/// </remarks>
public interface IWorkflowExecutionHeartbeat
{
    /// <summary>两次心跳的间隔。必须明显短于看门狗的 <c>RunningTimeout</c>。</summary>
    TimeSpan Interval { get; }

    /// <summary>记录执行实例 <paramref name="executionId"/> 此刻仍在运行。</summary>
    Task BeatAsync(string executionId, CancellationToken cancellationToken = default);
}
