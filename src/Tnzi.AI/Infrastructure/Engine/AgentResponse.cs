
namespace Tnzi.AI.Infrastructure.Engine;

/// <summary>
/// Agent 非流式执行结果
/// </summary>
public class AgentResponse
{
    /// <summary>
    /// 助手回复的文本内容
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Token 使用信息
    /// </summary>
    public UsageDetails? Usage { get; set; }

    /// <summary>
    /// 结束原因
    /// </summary>
    public string? FinishReason { get; set; }

    /// <summary>
    /// 完整消息列表（含对话上下文，可用于持久化）
    /// </summary>
    public List<ChatMessage> Messages { get; set; } = [];
}
