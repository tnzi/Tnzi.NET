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

    /// <summary>Token 使用量（仅最后一个事件）</summary>
    public TokenUsageDto? Usage { get; init; }

    /// <summary>是否工具调用中</summary>
    public bool IsToolCall { get; init; }

    /// <summary>错误信息</summary>
    public string? Error { get; init; }

    /// <summary>线程 ID</summary>
    public Guid? ThreadId { get; init; }
}
