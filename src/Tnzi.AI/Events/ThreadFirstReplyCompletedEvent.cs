namespace Tnzi.AI.Events;

/// <summary>
/// 线程首轮对话完成事件 - 用于触发标题生成等后续操作
/// </summary>
public class ThreadFirstReplyCompletedEvent : EventBase
{
    /// <summary>
    /// 线程 ID
    /// </summary>
    public Guid ThreadId { get; init; }

    /// <summary>
    /// 用户的第一条消息
    /// </summary>
    public string UserMessage { get; init; } = string.Empty;

    /// <summary>
    /// AI 的第一条回复
    /// </summary>
    public string AssistantReply { get; init; } = string.Empty;
}
