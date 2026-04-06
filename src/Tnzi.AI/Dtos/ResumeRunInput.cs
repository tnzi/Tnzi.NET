namespace Tnzi.AI.Dtos;

/// <summary>
/// 恢复运行的输入参数
/// </summary>
public class ResumeRunInput
{
    /// <summary>审批决定（approve/reject）</summary>
    public string? ApprovalDecision { get; init; }

    /// <summary>审批备注</summary>
    public string? ApprovalComment { get; init; }

    /// <summary>要重试的节点 ID</summary>
    public Guid? RetryNodeId { get; init; }

    /// <summary>额外的用户输入</summary>
    public string? UserMessage { get; init; }

    /// <summary>Workflow 中断步骤 ID（恢复 AwaitingInput 时可选，不传则自动解析当前 pending step）</summary>
    public string? WorkflowStepId { get; init; }

    /// <summary>Workflow 恢复输入（恢复 AwaitingInput 时必填）</summary>
    public Dictionary<string, object>? WorkflowInput { get; init; }
}
