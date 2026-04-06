namespace Tnzi.AI.Workflow;

/// <summary>
/// 工作流执行信号类型常量
/// </summary>
[ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
public static class WorkflowExecutionSignalTypes
{
    public const string Cancel = "cancel";
    public const string ResumeInput = "resume_input";
}

/// <summary>
/// 工作流恢复输入载荷
/// </summary>
[ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
public class WorkflowExecutionInput
{
    /// <summary>附加说明文本</summary>
    public string? Message { get; set; }

    /// <summary>结构化输入数据</summary>
    public Dictionary<string, object>? Data { get; set; }

    /// <summary>提交时间</summary>
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Run 节点级输入描述
/// </summary>
[ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
public class AgentRunNodeInput
{
    /// <summary>稳定节点键</summary>
    public string? NodeKey { get; set; }

    /// <summary>节点名称</summary>
    public string? NodeName { get; set; }

    /// <summary>输入类型（approval / human_input / external_signal）</summary>
    public string? InputKind { get; set; }

    /// <summary>工作流输入载荷</summary>
    public WorkflowExecutionInput Input { get; set; } = new();
}

/// <summary>
/// 工作流执行信号
/// </summary>
[ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
public class WorkflowExecutionSignal
{
    /// <summary>信号 ID</summary>
    public string SignalId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>信号类型</summary>
    public string Type { get; set; } = WorkflowExecutionSignalTypes.Cancel;

    /// <summary>目标步骤 ID</summary>
    public string? StepId { get; set; }

    /// <summary>目标节点输入</summary>
    public AgentRunNodeInput? NodeInput { get; set; }

    /// <summary>原因描述</summary>
    public string? Reason { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
