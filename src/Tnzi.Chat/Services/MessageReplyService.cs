namespace Tnzi.Chat.Services;

/// <summary>
/// 消息回复服务实现
/// </summary>
public class MessageReplyService : ApplicationService, IMessageReplyService
{
    private readonly IRepository<Message, Guid> _messageRepository;
    private readonly IRepository<MessageReply, Guid> _replyRepository;
    private readonly IRepository<MessageReceive, Guid> _receiveRepository;
    private readonly IRepository<User, Guid> _userRepository;

    public MessageReplyService(
        IRepository<Message, Guid> messageRepository,
        IRepository<MessageReply, Guid> replyRepository,
        IRepository<MessageReceive, Guid> receiveRepository,
        IRepository<User, Guid> userRepository,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _messageRepository = Check.NotNull(messageRepository);
        _replyRepository = Check.NotNull(replyRepository);
        _receiveRepository = Check.NotNull(receiveRepository);
        _userRepository = Check.NotNull(userRepository);
    }

    /// <summary>
    /// 创建回复
    /// </summary>
    public async Task<Result<MessageReplyDto>> CreateAsync(Guid userId, CreateMessageReplyDto input)
    {
        Check.NotNull(input);

        var message = await _messageRepository.GetAsync(input.MessageId);
        if (message == null)
            return Fail<MessageReplyDto>("Message not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (!message.CanReply)
            return Fail<MessageReplyDto>("This message does not allow replies", 400, ErrorCodes.VALIDATION_ERROR);

        // 验证父回复
        if (input.ParentReplyId.HasValue)
        {
            var parentReply = await _replyRepository.GetAsync(input.ParentReplyId.Value);
            if (parentReply == null || parentReply.BelongMessageId != input.MessageId)
                return Fail<MessageReplyDto>("Parent reply not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var reply = input.MapTo<MessageReply>();
        reply.UserId = userId;
        reply.BelongMessageId = input.MessageId;

        await _replyRepository.InsertAsync(reply);

        var dto = reply.MapTo<MessageReplyDto>();
        var user = await _userRepository.GetAsync(userId);
        dto.UserName = user?.UserName;

        // 发布回复事件
        if (EventBus != null)
        {
            var notifyUserIds = await _receiveRepository
                .Where(mr => mr.MessageId == input.MessageId && mr.UserId != userId)
                .Select(mr => mr.UserId)
                .Distinct()
                .ToListAsync();

            if (message.SenderId != userId && !notifyUserIds.Contains(message.SenderId))
                notifyUserIds.Add(message.SenderId);

            await EventBus.PublishAsync(new MessageRepliedEvent
            {
                ReplyId = reply.Id,
                MessageId = input.MessageId,
                UserId = userId,
                NotifyUserIds = notifyUserIds,
                Content = reply.Content
            });
        }

        LogInformation("Reply created: {ReplyId}, MessageId: {MessageId}", reply.Id, input.MessageId);
        return Ok(dto, "Reply created successfully");
    }

    /// <summary>
    /// 获取消息的回复列表（树形结构）
    /// </summary>
    public async Task<Result<IEnumerable<MessageReplyDto>>> GetByMessageIdAsync(Guid messageId, Guid userId)
    {
        var replies = await _replyRepository
            .Where(r => r.BelongMessageId == messageId)
            .OrderBy(r => r.CreationTime)
            .ToListAsync();

        if (replies.Count == 0)
            return Ok(Enumerable.Empty<MessageReplyDto>());

        // 批量加载用户名
        var userIds = replies.Select(r => r.UserId).Distinct().ToList();
        var users = await _userRepository
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToListAsync();
        var userDict = users.ToDictionary(u => u.Id, u => u.UserName);

        var dtos = replies.Select(reply =>
        {
            var dto = reply.MapTo<MessageReplyDto>();
            if (userDict.TryGetValue(reply.UserId, out var userName))
                dto.UserName = userName;
            return dto;
        }).ToList();

        // O(n) 构建树
        var tree = BuildReplyTree(dtos);
        return Ok((IEnumerable<MessageReplyDto>)tree);
    }

    /// <summary>
    /// 删除回复
    /// </summary>
    public async Task<Result> DeleteAsync(Guid replyId, Guid userId)
    {
        var reply = await _replyRepository.GetAsync(replyId);
        if (reply == null)
            return Fail("Reply not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (reply.UserId != userId)
            return Fail("You can only delete your own replies", 403, ErrorCodes.FORBIDDEN);

        await _replyRepository.DeleteAsync(replyId);
        LogInformation("Reply deleted: {ReplyId}, UserId: {UserId}", replyId, userId);
        return Ok("Reply deleted successfully");
    }

    /// <summary>
    /// 构建回复树 — O(n) 算法
    /// </summary>
    private static List<MessageReplyDto> BuildReplyTree(List<MessageReplyDto> allReplies)
    {
        var lookup = allReplies.ToLookup(r => r.ParentReplyId);
        var roots = new List<MessageReplyDto>();

        foreach (var reply in allReplies)
        {
            reply.Replies = lookup[reply.Id].ToList();
            if (!reply.ParentReplyId.HasValue)
                roots.Add(reply);
        }

        return roots;
    }
}
