namespace Tnzi.AI.Events;

/// <summary>
/// 新线程的第一条用户消息被接受、回退标题已写下，请后台生成 AI 标题。
/// </summary>
/// <remarks>
/// 只在开了 <c>AI:Thread:AutoGenerateTitle</c> 时发布，发布时不等回复。处理器是后台处理器
/// （<see cref="Handlers.ThreadTitleGenerationHandler"/>）：对话本身从不等标题。
/// 租户在 <see cref="EventBase.TenantId"/> 上，由总线恢复。
/// </remarks>
public class ThreadTitleRequestedEvent : EventBase
{
    /// <summary>线程 ID</summary>
    public Guid ThreadId { get; init; }

    /// <summary>
    /// 第一条用户消息：模型实际收到的文本（输入守卫改写之后），已截到 <c>AI:Thread:TitleMaxInputBytes</c>。
    /// </summary>
    public string UserMessage { get; init; } = string.Empty;

    /// <summary>发起这一轮的用户；标题调用的费用记到他名下（为空时只记用量日志）。</summary>
    public Guid? UserId { get; init; }

    /// <summary>线程所属的 Agent（用量日志按它归集）。</summary>
    public Guid? AgentId { get; init; }
}
