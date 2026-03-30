namespace Tnzi.AI.Dtos;

/// <summary>
/// 对话摘要信息
/// </summary>
public class ConversationSummary
{
    /// <summary>
    /// 对话 ID（不透明标识符，格式取决于 IConversationStore 实现）
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 对话标题
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivity { get; set; }

    /// <summary>
    /// 消息数量
    /// </summary>
    public int MessageCount { get; set; }
}
