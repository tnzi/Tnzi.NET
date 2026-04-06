namespace Tnzi.AI.Models;

/// <summary>
/// ITnziAiClient 流式事件
/// </summary>
public class AiClientStreamEvent
{
    /// <summary>增量文本</summary>
    public string? Text { get; init; }

    /// <summary>完成原因（仅最后一个事件）</summary>
    public string? FinishReason { get; init; }

    /// <summary>实际执行模型</summary>
    public string? Model { get; init; }

    /// <summary>Token 使用量（仅最后一个事件）</summary>
    public TokenUsageDto? Usage { get; init; }

    /// <summary>是否工具调用中</summary>
    public bool IsToolCall { get; init; }

    /// <summary>工具名称列表</summary>
    public List<string>? ToolCallNames { get; init; }

    /// <summary>工具调用详情</summary>
    public List<ToolCallDetail>? ToolCalls { get; init; }

    /// <summary>错误信息</summary>
    public string? Error { get; init; }

    /// <summary>推理增量文本</summary>
    public string? ReasoningText { get; init; }

    /// <summary>Agent 名称变更</summary>
    public string? AgentName { get; init; }

    /// <summary>结构化事件类型</summary>
    public string? EventType { get; init; }

    /// <summary>结构化事件附加数据</summary>
    public Dictionary<string, object>? EventData { get; init; }

    /// <summary>后续建议问题</summary>
    public List<string>? Suggestions { get; init; }

    /// <summary>Todo 列表快照</summary>
    public List<TodoItemDto>? Todos { get; init; }

    /// <summary>运行产物</summary>
    public List<AgentArtifactDto>? Artifacts { get; init; }

    /// <summary>线程 ID</summary>
    public Guid? ThreadId { get; init; }
}
