namespace Tnzi.AI.Workflow;

/// <summary>
/// 工作流中断处理器 - 处理需要人工审批的步骤中断
/// </summary>
/// <remarks>
/// <para>
/// 当工作流步骤设置了 <see cref="Tnzi.AI.Dtos.WorkflowStepDto.RequiresApproval"/> = true 时，
/// 步骤完成后工作流将暂停并调用此处理器，等待人工审批结果。
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
