namespace Tnzi.Chat.Services;

/// <summary>
/// 消息服务接口
/// </summary>
public interface IMessageService
{
    /// <summary>
    /// 发送消息
    /// </summary>
    Task<Result<MessageDto>> SendAsync(Guid senderId, CreateMessageDto input);

    /// <summary>
    /// 获取用户消息列表（收件箱）
    /// </summary>
    Task<Result<IPagedList<MessageListItemDto>>> GetUserInboxAsync(Guid userId, MessageQueryDto query);

    /// <summary>
    /// 获取消息详情
    /// </summary>
    Task<Result<MessageDto>> GetByIdAsync(Guid messageId, Guid userId);

    /// <summary>
    /// 更新消息（仅发送者可操作）
    /// </summary>
    Task<Result<MessageDto>> UpdateAsync(Guid messageId, Guid userId, UpdateMessageDto input);

    /// <summary>
    /// 删除消息（仅发送者可操作，软删除）
    /// </summary>
    Task<Result> DeleteAsync(Guid messageId, Guid userId);

    /// <summary>
    /// 标记消息为已读
    /// </summary>
    Task<Result> MarkAsReadAsync(Guid messageId, Guid userId);

    /// <summary>
    /// 批量标记已读
    /// </summary>
    Task<Result<int>> MarkAllAsReadAsync(Guid userId);

    /// <summary>
    /// 获取未读消息数量
    /// </summary>
    Task<Result<int>> GetUnreadCountAsync(Guid userId);

    /// <summary>
    /// 管理端：获取所有消息列表
    /// </summary>
    Task<Result<IPagedList<MessageListItemDto>>> GetAdminListAsync(AdminMessageQueryDto query);

    /// <summary>
    /// 管理端：删除消息
    /// </summary>
    Task<Result> AdminDeleteAsync(Guid messageId);

    /// <summary>
    /// 管理端：批量删除消息
    /// </summary>
    Task<Result<int>> AdminBatchDeleteAsync(List<Guid> ids, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<int>("Admin batch delete not implemented", 501));
    }

    /// <summary>
    /// 管理端：获取消息统计
    /// </summary>
    Task<Result<ChatStatisticsDto>> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<ChatStatisticsDto>("Statistics not implemented", 501));
    }

    /// <summary>
    /// 用户：批量删除消息（从收件箱移除）
    /// </summary>
    Task<Result<int>> BatchDeleteReceivesAsync(Guid userId, List<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<int>("Batch delete receives not implemented", 501));
    }

    /// <summary>
    /// 用户：批量标记已读
    /// </summary>
    Task<Result<int>> BatchMarkAsReadAsync(Guid userId, List<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<int>("Batch mark as read not implemented", 501));
    }
}
