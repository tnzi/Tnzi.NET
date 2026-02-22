namespace Tnzi.Chat.Services;

/// <summary>
/// 消息回复服务接口
/// </summary>
public interface IMessageReplyService
{
    /// <summary>
    /// 创建回复
    /// </summary>
    Task<Result<MessageReplyDto>> CreateAsync(Guid userId, CreateMessageReplyDto input);

    /// <summary>
    /// 获取消息的回复列表（树形结构）
    /// </summary>
    Task<Result<IEnumerable<MessageReplyDto>>> GetByMessageIdAsync(Guid messageId, Guid userId);

    /// <summary>
    /// 删除回复（仅回复者可操作，软删除）
    /// </summary>
    Task<Result> DeleteAsync(Guid replyId, Guid userId);
}
