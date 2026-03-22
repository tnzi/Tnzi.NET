using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;

namespace Tnzi.AI.Services;

/// <summary>
/// 消息级反馈服务实现
/// </summary>
public class MessageFeedbackService : ApplicationService, IMessageFeedbackService
{
    private readonly IRepository<AgentThreadMessage, Guid> _messageRepository;
    private readonly IRepository<AgentThreadEntity, Guid> _threadRepository;

    public MessageFeedbackService(
        IServiceProvider serviceProvider,
        IRepository<AgentThreadMessage, Guid> messageRepository,
        IRepository<AgentThreadEntity, Guid> threadRepository)
        : base(serviceProvider)
    {
        _messageRepository = Check.NotNull(messageRepository);
        _threadRepository = Check.NotNull(threadRepository);
    }

    public async Task<Result> SubmitAsync(Guid threadId, Guid messageId, Guid userId, MessageFeedbackDto input)
    {
        // 验证线程归属（CreatorId == userId）
        var thread = await _threadRepository.FirstOrDefaultAsync(t => t.Id == threadId && t.CreatorId == userId);
        if (thread == null)
            return Fail("Thread not found", 404, ErrorCodes.ThreadNotFound);

        // 验证消息属于该线程
        var message = await _messageRepository.FirstOrDefaultAsync(m => m.Id == messageId && m.ThreadId == threadId);
        if (message == null)
            return Fail("Message not found", 404, ErrorCodes.MessageNotFound);

        // 只允许对 assistant 消息评价
        if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            return Fail("Feedback can only be submitted on assistant messages", 400, ErrorCodes.FeedbackOnlyAssistant);

        // 设置反馈字段（允许覆盖已有反馈）
        message.FeedbackRating = input.Rating;
        message.FeedbackComment = input.Comment;
        // Tags 仅在差评时保存，👍 时忽略
        message.FeedbackTags = !input.Rating && input.Tags is { Count: > 0 }
            ? JsonSerializer.Serialize(input.Tags)
            : null;
        message.FeedbackTime = DateTime.UtcNow;

        await _messageRepository.UpdateAsync(message);

        return Ok();
    }

    public async Task<Result> RevokeAsync(Guid threadId, Guid messageId, Guid userId)
    {
        // 验证线程归属
        var thread = await _threadRepository.FirstOrDefaultAsync(t => t.Id == threadId && t.CreatorId == userId);
        if (thread == null)
            return Fail("Thread not found", 404, ErrorCodes.ThreadNotFound);

        // 验证消息属于该线程
        var message = await _messageRepository.FirstOrDefaultAsync(m => m.Id == messageId && m.ThreadId == threadId);
        if (message == null)
            return Fail("Message not found", 404, ErrorCodes.MessageNotFound);

        // 验证消息有反馈可撤回
        if (message.FeedbackRating == null)
            return Fail("No feedback to revoke", 404, ErrorCodes.FeedbackNotFound);

        // 清除所有 4 个反馈字段
        message.FeedbackRating = null;
        message.FeedbackTags = null;
        message.FeedbackComment = null;
        message.FeedbackTime = null;

        await _messageRepository.UpdateAsync(message);

        return Ok();
    }

}
