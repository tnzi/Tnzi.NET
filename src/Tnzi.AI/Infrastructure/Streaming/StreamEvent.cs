namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// 流式事件（delta 模型 — 每个事件只包含增量内容，非累积）
/// </summary>
public class StreamEvent
{
    /// <summary>增量文本（非累积内容）</summary>
    public string? Delta { get; set; }

    /// <summary>完成原因 (stop, length, tool_calls, etc.)</summary>
    public string? FinishReason { get; set; }

    /// <summary>使用的模型</summary>
    public string? Model { get; set; }

    /// <summary>线程 ID</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>Token 使用量（仅在最终事件中包含）</summary>
    public TokenUsageDto? Usage { get; set; }

    /// <summary>是否为终止事件</summary>
    public bool IsDone { get; set; }
}
