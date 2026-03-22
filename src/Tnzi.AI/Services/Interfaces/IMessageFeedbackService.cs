namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 消息级反馈服务 — 管理 ThreadMessage 上的 👍/👎 反馈
/// </summary>
public interface IMessageFeedbackService
{
    /// <summary>提交反馈（用户端，限定线程所有者）</summary>
    Task<Result> SubmitAsync(Guid threadId, Guid messageId, Guid userId, MessageFeedbackDto input);

    /// <summary>撤回反馈（用户端）</summary>
    Task<Result> RevokeAsync(Guid threadId, Guid messageId, Guid userId);
}
