namespace Tnzi.AI.Dtos;

/// <summary>
/// AI 运行请求
/// </summary>
public class AgentRunRequest
{
    /// <summary>Agent ID（与 Provider/Model 二选一）</summary>
    public Guid? AgentId { get; init; }

    /// <summary>直接指定 Provider（无需预定义 Agent）</summary>
    public string? Provider { get; init; }

    /// <summary>直接指定 Model</summary>
    public string? Model { get; init; }

    /// <summary>用户消息（文本或多模态）</summary>
    public string? UserMessage { get; init; }

    /// <summary>多模态内容部分</summary>
    public List<ContentPartDto>? ContentParts { get; init; }

    /// <summary>对话线程 ID（为空则新建，中间件可回写）</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>附加工具组</summary>
    public List<string>? ToolGroups { get; init; }

    /// <summary>Workflow 定义 ID（若指定则走 Workflow 模式）</summary>
    public Guid? WorkflowId { get; init; }

    /// <summary>Workflow 输入变量</summary>
    public Dictionary<string, object>? WorkflowInputs { get; init; }

    /// <summary>是否创建 Run 记录（用于追踪复杂运行）</summary>
    public bool EnableRunTracking { get; init; }

    /// <summary>当前用户 ID（用于配额检查等）</summary>
    public Guid? UserId { get; init; }

    /// <summary>Per-request reasoning effort override (None = no reasoning)</summary>
    public ReasoningEffort? ReasoningEffort { get; init; }
}
