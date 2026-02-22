
namespace Tnzi.AI.Infrastructure.Engine;

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
    public UsageDetails? Usage { get; set; }

    /// <summary>
    /// 结束原因（通常只在最后一个 chunk 中出现）
    /// </summary>
    public string? FinishReason { get; set; }

    /// <summary>
    /// 是否为工具调用中间状态（此时 Text 可能为 null）
    /// </summary>
    public bool IsToolCall { get; set; }
}
