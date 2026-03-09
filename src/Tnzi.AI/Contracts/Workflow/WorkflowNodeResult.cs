namespace Tnzi.AI.Contracts.Workflow;

/// <summary>
/// 工作流节点执行结果
/// </summary>
public class WorkflowNodeResult
{
    /// <summary>节点输出</summary>
    public WorkflowStepOutput Output { get; init; } = new();

    /// <summary>Token 使用量</summary>
    public TokenUsageDto? Usage { get; init; }

    /// <summary>执行耗时（毫秒）</summary>
    public long DurationMs { get; init; }

    /// <summary>是否成功</summary>
    public bool IsSuccess { get; init; } = true;

    /// <summary>错误信息（失败时）</summary>
    public string? Error { get; init; }

    /// <summary>路由目标节点 ID（条件边使用）</summary>
    public string? RouteTo { get; init; }

    /// <summary>是否等待人工审批</summary>
    public bool AwaitingApproval { get; init; }

    /// <summary>动态生成的子步骤（可选，用于动态工作流）</summary>
    public List<WorkflowStepDto>? DynamicSteps { get; init; }
}
