
namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 流式执行的增量块
/// </summary>
public class AgentStreamChunk
{
    /// <summary>
    /// 增量文本内容
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Token 使用信息（通常只在最后一个 chunk 中出现）
    /// </summary>
    public TokenUsageDto? Usage { get; set; }

    /// <summary>
    /// 结束原因（通常只在最后一个 chunk 中出现）
    /// </summary>
    public string? FinishReason { get; set; }

    /// <summary>
    /// 当前 chunk 对应的实际执行模型
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 是否为工具调用中间状态（此时 Text 可能为 null）
    /// </summary>
    public bool IsToolCall { get; set; }

    /// <summary>
    /// Tool names being called (sent alongside IsToolCall = true for early UI feedback)
    /// </summary>
    public List<string>? ToolCallNames { get; set; }

    /// <summary>
    /// 错误信息（工具调用超时、Guardrail 拒绝等）
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// 推理/思考增量文本（来自 DeepSeek-R1 等模型的 reasoning_content）
    /// </summary>
    public string? ReasoningText { get; set; }

    /// <summary>
    /// Agent 名称变更（Handoff 场景：当前对话切换到新 Agent 时设置）
    /// </summary>
    public string? AgentName { get; set; }

    /// <summary>
    /// RAG 引用来源（仅在最终 chunk 中包含）
    /// </summary>
    public List<CitationDto>? Citations { get; set; }

    /// <summary>
    /// Tool call details (name + duration, sent after each tool execution batch)
    /// </summary>
    public List<ToolCallDetail>? ToolCalls { get; set; }

    /// <summary>
    /// 流式输出粒度模式（标记此 chunk 属于哪种模式）
    /// </summary>
    public StreamMode Mode { get; set; } = StreamMode.Messages;

    /// <summary>
    /// 事件类型（Steps/Debug 模式下的具体事件标识）
    /// </summary>
    public string? EventType { get; set; }

    /// <summary>
    /// 事件附加数据
    /// </summary>
    public Dictionary<string, object>? EventData { get; set; }

    /// <summary>后续建议问题（仅在最终 chunk 中包含，Mode=Suggestion 时）</summary>
    public List<string>? Suggestions { get; set; }

    /// <summary>Todo 列表快照（Plan Mode 中间状态更新）</summary>
    public List<TodoItemDto>? Todos { get; set; }

    /// <summary>产出的文件产物（最终 chunk 中包含）</summary>
    public List<AgentArtifactDto>? Artifacts { get; set; }
}
