namespace Tnzi.AI.Workflow;

/// <summary>
/// 工作流中断处理器 - 处理需要人工审批的步骤中断
/// </summary>
/// <remarks>
/// <para>
/// 只对设置了 <c>WorkflowStepDto.RequiresApproval</c> = true 的步骤生效：步骤执行完后引擎就地调用此处理器，
/// 批准则继续，拒绝则结束本次执行。拒绝不暂停，也不写等待审批的检查点；配了
/// <see cref="WorkflowExecutionOptions.CheckpointStore"/> 时，本层收尾仍以 <c>Failed</c> 状态落一次检查点，
/// 被拒的步骤不计入已完成，恢复这次执行时它重新生成并再次送审。节点经 <c>CheckInterruptAsync</c> 发起的中断
/// （审批节点、人工输入、外部事件）不经过它，一律保存检查点后暂停。
/// </para>
/// <para>
/// 只经 <see cref="WorkflowExecutionOptions.InterruptHandler"/> 传给引擎时才被调用；在依赖注入容器里注册它没有效果，
/// 工作流服务的运行与恢复不使用处理器，审批经检查点暂停、由审批端点放行。
/// </para>
/// <para>
/// 实现者可通过 WebSocket、SignalR、轮询或外部审批系统来完成审批流程。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Workflow HITL is in preview")]
public interface IWorkflowInterruptHandler
{
    /// <summary>
    /// 处理工作流中断，等待人工审批
    /// </summary>
    /// <param name="context">中断上下文（含步骤信息和输出）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>审批结果（批准/拒绝，可附带反馈和修改后的输入）</returns>
    Task<WorkflowInterruptResult> HandleInterruptAsync(WorkflowInterruptContext context, CancellationToken ct = default);
}

/// <summary>
/// 工作流中断上下文 - 提供中断发生时的步骤信息
/// </summary>
[ExperimentalApi(Reason = "Workflow HITL is in preview")]
public class WorkflowInterruptContext
{
    /// <summary>
    /// 工作流执行实例 ID
    /// </summary>
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>
    /// 中断的步骤 ID
    /// </summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>
    /// 步骤对应的 Agent 名称
    /// </summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>
    /// 步骤的输出内容（供人工审阅）
    /// </summary>
    public string StepOutput { get; set; } = string.Empty;
}

/// <summary>
/// 工作流中断审批结果
/// </summary>
[ExperimentalApi(Reason = "Workflow HITL is in preview")]
public class WorkflowInterruptResult
{
    /// <summary>
    /// 是否批准继续执行
    /// </summary>
    public bool Approved { get; set; }

    /// <summary>
    /// 审批反馈（可选，供后续步骤参考）
    /// </summary>
    public string? Feedback { get; set; }

    /// <summary>
    /// 修改后的输入（可选，不为 null 时将替代原步骤输出作为后续步骤的输入）
    /// </summary>
    public string? ModifiedInput { get; set; }
}
