namespace Tnzi.Chat.Services;

/// <summary>
/// 消息服务实现
/// </summary>
public class MessageService : ApplicationService, IMessageService
{
    private readonly IRepository<Message, Guid> _messageRepository;
    private readonly IRepository<MessageReceive, Guid> _receiveRepository;
    private readonly IRepository<MessageRecipient, Guid> _recipientRepository;
    private readonly IRepository<MessageReply, Guid> _replyRepository;
    private readonly IRepository<MessageRole, Guid> _roleRepository;
    private readonly IRepository<User, Guid> _userRepository;
    private readonly IUserRoleService? _userRoleService;

    public MessageService(
        IRepository<Message, Guid> messageRepository,
        IRepository<MessageReceive, Guid> receiveRepository,
        IRepository<MessageRecipient, Guid> recipientRepository,
        IRepository<MessageReply, Guid> replyRepository,
        IRepository<MessageRole, Guid> roleRepository,
        IRepository<User, Guid> userRepository,
        IServiceProvider serviceProvider,
        IUserRoleService? userRoleService = null)
        : base(serviceProvider)
    {
        _messageRepository = Check.NotNull(messageRepository);
        _receiveRepository = Check.NotNull(receiveRepository);
        _recipientRepository = Check.NotNull(recipientRepository);
        _replyRepository = Check.NotNull(replyRepository);
        _roleRepository = Check.NotNull(roleRepository);
        _userRepository = Check.NotNull(userRepository);
        _userRoleService = userRoleService;
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    public async Task<Result<MessageDto>> SendAsync(Guid senderId, CreateMessageDto input)
    {
        Check.NotNull(input);

        // 验证收件人
        if (input.MessageType == MessageType.Private && (input.RecipientIds == null || input.RecipientIds.Count == 0))
            return Fail<MessageDto>("Private message requires at least one recipient", 400, ErrorCodes.VALIDATION_ERROR);

        if (input.MessageType == MessageType.Public && (input.RoleIds == null || input.RoleIds.Count == 0))
            return Fail<MessageDto>("Public message requires at least one role", 400, ErrorCodes.VALIDATION_ERROR);

        var recipientUserIds = new List<Guid>();

        var message = await ExecuteInUnitOfWorkAsync(async ct =>
        {
            var msg = input.MapTo<Message>();
            msg.SenderId = senderId;
            msg.IsSent = false;

            await _messageRepository.InsertAsync(msg);

            // 创建收件人/角色关系记录
            if (input.MessageType == MessageType.Private && input.RecipientIds != null)
            {
                var recipients = input.RecipientIds.Select(uid => new MessageRecipient
                {
                    MessageId = msg.Id,
                    UserId = uid
                }).ToList();
                await _recipientRepository.InsertManyAsync(recipients);

                var receives = input.RecipientIds.Select(uid => new MessageReceive
                {
                    MessageId = msg.Id,
                    UserId = uid
                }).ToList();
                await _receiveRepository.InsertManyAsync(receives);

                recipientUserIds.AddRange(input.RecipientIds);
            }
            else if (input.MessageType == MessageType.Public && input.RoleIds != null)
            {
                var roles = input.RoleIds.Select(rid => new MessageRole
                {
                    MessageId = msg.Id,
                    RoleId = rid
                }).ToList();
                await _roleRepository.InsertManyAsync(roles);

                // 解析角色对应的用户
                if (_userRoleService != null)
                {
                    var userIdSet = new HashSet<Guid>();
                    foreach (var roleId in input.RoleIds)
                    {
                        var userIds = await _userRoleService.GetRoleUserIdsAsync(roleId);
                        foreach (var uid in userIds)
                            userIdSet.Add(uid);
                    }

                    if (userIdSet.Count > 0)
                    {
                        var receives = userIdSet.Select(uid => new MessageReceive
                        {
                            MessageId = msg.Id,
                            UserId = uid
                        }).ToList();
                        await _receiveRepository.InsertManyAsync(receives);
                        recipientUserIds.AddRange(userIdSet);
                    }
                }
            }

            msg.IsSent = true;
            await _messageRepository.UpdateAsync(msg);
            return msg;
        });

        // 发布消息发送事件（事务外）
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new MessageSentEvent
            {
                MessageId = message.Id,
                SenderId = senderId,
                MessageType = message.MessageType,
                RecipientUserIds = recipientUserIds,
                Title = message.Title
            });
        }

        var dto = await MapToMessageDtoAsync(message, senderId);
        LogInformation("Message sent: {MessageId}, SenderId: {SenderId}, Type: {MessageType}",
            message.Id, senderId, message.MessageType);
        return Ok(dto, "Message sent successfully");
    }

    /// <summary>
    /// 获取用户消息列表（收件箱）
    /// </summary>
    public async Task<Result<IPagedList<MessageListItemDto>>> GetUserInboxAsync(Guid userId, MessageQueryDto query)
    {
        var queryable = _messageRepository.AsQueryable()
            .Where(m => m.IsSent);

        if (query.MessageType.HasValue)
            queryable = queryable.Where(m => m.MessageType == query.MessageType.Value);

        if (query.StartDate.HasValue)
            queryable = queryable.Where(m => m.CreationTime >= query.StartDate.Value);
        if (query.EndDate.HasValue)
            queryable = queryable.Where(m => m.CreationTime <= query.EndDate.Value);

        // BeginDate/EndDate 有效期过滤
        var now = DateTime.UtcNow;
        queryable = queryable.Where(m =>
            (!m.BeginDate.HasValue || m.BeginDate.Value <= now) &&
            (!m.EndDate.HasValue || m.EndDate.Value >= now));

        if (query.IsImportant.HasValue)
            queryable = queryable.Where(m => m.IsImportant == query.IsImportant.Value);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(m =>
                m.Title.ToLower().Contains(keyword) || m.Content.ToLower().Contains(keyword));
        }

        // 可见性过滤：发送者 OR 有接收记录
        var receiveQuery = _receiveRepository.AsQueryable().Where(mr => mr.UserId == userId);
        queryable = queryable.Where(m =>
            m.SenderId == userId ||
            receiveQuery.Any(mr => mr.MessageId == m.Id));

        // 已读状态过滤
        if (query.IsRead.HasValue)
        {
            if (query.IsRead.Value)
                queryable = queryable.Where(m =>
                    receiveQuery.Any(mr => mr.MessageId == m.Id && mr.ReadTime != null));
            else
                queryable = queryable.Where(m =>
                    !receiveQuery.Any(mr => mr.MessageId == m.Id && mr.ReadTime != null));
        }

        queryable = queryable.OrderByDescending(m => m.CreationTime);

        var paged = await queryable
            .ProjectTo<Message, MessageListItemDto>()
            .CreateAsync(query);

        // 批量加载发送者名称和已读状态
        await EnrichMessageListItemsAsync(paged.Items, userId);

        return Ok(paged);
    }

    /// <summary>
    /// 获取消息详情
    /// </summary>
    public async Task<Result<MessageDto>> GetByIdAsync(Guid messageId, Guid userId)
    {
        var message = await _messageRepository.GetAsync(messageId);
        if (message == null)
            return Fail<MessageDto>("Message not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        // 权限检查
        var hasPermission = await CheckMessagePermissionAsync(message, userId);
        if (!hasPermission)
            return Fail<MessageDto>("Permission denied", 403, ErrorCodes.FORBIDDEN);

        // 确保有接收记录（公共消息首次查看时创建）
        var receive = await _receiveRepository
            .FirstOrDefaultAsync(mr => mr.MessageId == messageId && mr.UserId == userId);
        if (receive == null && message.SenderId != userId)
        {
            receive = new MessageReceive { MessageId = messageId, UserId = userId };
            await _receiveRepository.InsertAsync(receive);
        }

        var dto = await MapToMessageDtoAsync(message, userId);
        return Ok(dto);
    }

    /// <summary>
    /// 更新消息
    /// </summary>
    public async Task<Result<MessageDto>> UpdateAsync(Guid messageId, Guid userId, UpdateMessageDto input)
    {
        Check.NotNull(input);

        var message = await _messageRepository.GetAsync(messageId);
        if (message == null)
            return Fail<MessageDto>("Message not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (message.SenderId != userId)
            return Fail<MessageDto>("You can only update your own messages", 403, ErrorCodes.FORBIDDEN);

        if (input.Title != null) message.Title = input.Title;
        if (input.Content != null) message.Content = input.Content;
        if (input.CanReply.HasValue) message.CanReply = input.CanReply.Value;
        if (input.IsImportant.HasValue) message.IsImportant = input.IsImportant.Value;
        if (input.BeginDate.HasValue) message.BeginDate = input.BeginDate.Value;
        if (input.EndDate.HasValue) message.EndDate = input.EndDate.Value;

        await _messageRepository.UpdateAsync(message);

        var dto = await MapToMessageDtoAsync(message, userId);
        LogInformation("Message updated: {MessageId}", messageId);
        return Ok(dto, "Message updated successfully");
    }

    /// <summary>
    /// 删除消息
    /// </summary>
    public async Task<Result> DeleteAsync(Guid messageId, Guid userId)
    {
        var message = await _messageRepository.GetAsync(messageId);
        if (message == null)
            return Fail("Message not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (message.SenderId != userId)
            return Fail("You can only delete your own messages", 403, ErrorCodes.FORBIDDEN);

        await _messageRepository.DeleteAsync(messageId);
        LogInformation("Message deleted: {MessageId}, UserId: {UserId}", messageId, userId);
        return Ok("Message deleted successfully");
    }

    /// <summary>
    /// 标记消息为已读
    /// </summary>
    public async Task<Result> MarkAsReadAsync(Guid messageId, Guid userId)
    {
        // 验证消息存在
        var message = await _messageRepository.GetAsync(messageId);
        if (message == null)
            return Fail("Message not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var receive = await _receiveRepository
            .FirstOrDefaultAsync(mr => mr.MessageId == messageId && mr.UserId == userId);

        if (receive == null)
        {
            receive = new MessageReceive
            {
                MessageId = messageId,
                UserId = userId,
                ReadTime = DateTime.UtcNow
            };
            await _receiveRepository.InsertAsync(receive);
        }
        else if (receive.ReadTime == null)
        {
            receive.ReadTime = DateTime.UtcNow;
            await _receiveRepository.UpdateAsync(receive);
        }
        else
        {
            return Ok("Message already read");
        }

        // 发布已读事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new MessageReadEvent
            {
                MessageId = messageId,
                UserId = userId,
                SenderId = message.SenderId
            });
        }

        return Ok("Message marked as read");
    }

    /// <summary>
    /// 批量标记已读
    /// </summary>
    public async Task<Result<int>> MarkAllAsReadAsync(Guid userId)
    {
        var unreadReceives = await _receiveRepository
            .Where(mr => mr.UserId == userId && mr.ReadTime == null)
            .ToListAsync();

        if (unreadReceives.Count == 0)
            return Ok(0, "No unread messages");

        var now = DateTime.UtcNow;
        foreach (var receive in unreadReceives)
            receive.ReadTime = now;

        await _receiveRepository.UpdateManyAsync(unreadReceives);
        return Ok(unreadReceives.Count, $"{unreadReceives.Count} messages marked as read");
    }

    /// <summary>
    /// 获取未读消息数量
    /// </summary>
    public async Task<Result<int>> GetUnreadCountAsync(Guid userId)
    {
        var count = await _receiveRepository
            .Where(mr => mr.UserId == userId && mr.ReadTime == null)
            .CountAsync();
        return Ok(count);
    }

    /// <summary>
    /// 管理端：获取所有消息列表
    /// </summary>
    public async Task<Result<IPagedList<MessageListItemDto>>> GetAdminListAsync(AdminMessageQueryDto query)
    {
        var queryable = _messageRepository.AsQueryable();

        if (query.MessageType.HasValue)
            queryable = queryable.Where(m => m.MessageType == query.MessageType.Value);
        if (query.IsSent.HasValue)
            queryable = queryable.Where(m => m.IsSent == query.IsSent.Value);
        if (query.SenderId.HasValue)
            queryable = queryable.Where(m => m.SenderId == query.SenderId.Value);
        if (query.StartDate.HasValue)
            queryable = queryable.Where(m => m.CreationTime >= query.StartDate.Value);
        if (query.EndDate.HasValue)
            queryable = queryable.Where(m => m.CreationTime <= query.EndDate.Value);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(m =>
                m.Title.ToLower().Contains(keyword) || m.Content.ToLower().Contains(keyword));
        }

        queryable = queryable.OrderByDescending(m => m.CreationTime);

        var paged = await queryable
            .ProjectTo<Message, MessageListItemDto>()
            .CreateAsync(query);

        await EnrichSenderNamesAsync(paged.Items.ToList());
        return Ok(paged);
    }

    /// <summary>
    /// 管理端：删除消息
    /// </summary>
    public async Task<Result> AdminDeleteAsync(Guid messageId)
    {
        var message = await _messageRepository.GetAsync(messageId);
        if (message == null)
            return Fail("Message not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        await _messageRepository.DeleteAsync(messageId);
        LogInformation("Message admin deleted: {MessageId}", messageId);
        return Ok("Message deleted successfully");
    }

    /// <summary>
    /// 管理端：批量删除消息
    /// </summary>
    public async Task<Result<int>> AdminBatchDeleteAsync(List<Guid> ids, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(ids);

        var messages = await _messageRepository
            .Where(m => ids.Contains(m.Id))
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
            return Ok(0, "No messages found");

        await _messageRepository.DeleteManyAsync(messages, cancellationToken);
        LogInformation("Admin batch deleted {Count} messages", messages.Count);
        return Ok(messages.Count, $"{messages.Count} messages deleted");
    }

    /// <summary>
    /// 管理端：获取消息统计
    /// </summary>
    public async Task<Result<ChatStatisticsDto>> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        var queryable = _messageRepository.AsQueryable();

        if (startDate.HasValue)
            queryable = queryable.Where(m => m.CreationTime >= startDate.Value);
        if (endDate.HasValue)
            queryable = queryable.Where(m => m.CreationTime <= endDate.Value);

        var messages = await queryable.ToListAsync(cancellationToken);

        var dto = new ChatStatisticsDto
        {
            TotalMessages = messages.Count,
            SentMessages = messages.Count(m => m.IsSent),
            DraftMessages = messages.Count(m => !m.IsSent),
            PublicMessages = messages.Count(m => m.MessageType == MessageType.Public),
            PrivateMessages = messages.Count(m => m.MessageType == MessageType.Private),
            ActiveSenders = messages.Select(m => m.SenderId).Distinct().Count(),
            ImportantMessages = messages.Count(m => m.IsImportant)
        };

        // 回复数量需要单独查询
        var replyQueryable = _replyRepository.AsQueryable();
        if (startDate.HasValue)
            replyQueryable = replyQueryable.Where(r => r.CreationTime >= startDate.Value);
        if (endDate.HasValue)
            replyQueryable = replyQueryable.Where(r => r.CreationTime <= endDate.Value);

        dto.TotalReplies = await replyQueryable.CountAsync(cancellationToken);

        return Ok(dto);
    }

    /// <summary>
    /// 用户：批量删除消息（从收件箱移除）
    /// </summary>
    public async Task<Result<int>> BatchDeleteReceivesAsync(Guid userId, List<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(messageIds);

        var receives = await _receiveRepository
            .Where(r => r.UserId == userId && messageIds.Contains(r.MessageId))
            .ToListAsync(cancellationToken);

        if (receives.Count == 0)
            return Ok(0, "No messages found in inbox");

        await _receiveRepository.DeleteManyAsync(receives, cancellationToken);
        LogInformation("User {UserId} batch deleted {Count} inbox messages", userId, receives.Count);
        return Ok(receives.Count, $"{receives.Count} messages removed from inbox");
    }

    /// <summary>
    /// 用户：批量标记已读
    /// </summary>
    public async Task<Result<int>> BatchMarkAsReadAsync(Guid userId, List<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(messageIds);

        var unreadReceives = await _receiveRepository
            .Where(r => r.UserId == userId && messageIds.Contains(r.MessageId) && r.ReadTime == null)
            .ToListAsync(cancellationToken);

        if (unreadReceives.Count == 0)
            return Ok(0, "No unread messages found");

        var now = DateTime.UtcNow;
        foreach (var receive in unreadReceives)
            receive.ReadTime = now;

        await _receiveRepository.UpdateManyAsync(unreadReceives, cancellationToken);
        return Ok(unreadReceives.Count, $"{unreadReceives.Count} messages marked as read");
    }

    // === 私有辅助方法 ===

    /// <summary>
    /// 检查用户是否有权限查看消息
    /// </summary>
    private async Task<bool> CheckMessagePermissionAsync(Message message, Guid userId)
    {
        if (message.SenderId == userId)
            return true;

        // 检查接收记录
        var hasReceive = await _receiveRepository
            .AnyAsync(mr => mr.MessageId == message.Id && mr.UserId == userId);
        if (hasReceive)
            return true;

        // 公共消息：检查用户角色
        if (message.MessageType == MessageType.Public && _userRoleService != null)
        {
            var userRoleIds = await _userRoleService.GetUserRoleIdsAsync(userId);
            var messageRoleIds = await _roleRepository
                .Where(r => r.MessageId == message.Id)
                .Select(r => r.RoleId)
                .ToListAsync();
            if (messageRoleIds.Any(rid => userRoleIds.Contains(rid)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 映射消息到详情 DTO
    /// </summary>
    private async Task<MessageDto> MapToMessageDtoAsync(Message message, Guid userId)
    {
        var dto = message.MapTo<MessageDto>();

        var sender = await _userRepository.GetAsync(message.SenderId);
        dto.SenderName = sender?.UserName;

        var receive = await _receiveRepository
            .FirstOrDefaultAsync(mr => mr.MessageId == message.Id && mr.UserId == userId);
        dto.IsRead = receive?.ReadTime != null;
        dto.ReadTime = receive?.ReadTime;

        dto.ReplyCount = await _replyRepository
            .Where(r => r.BelongMessageId == message.Id)
            .CountAsync();

        return dto;
    }

    /// <summary>
    /// 批量填充消息列表项数据
    /// </summary>
    private async Task EnrichMessageListItemsAsync(IEnumerable<MessageListItemDto> items, Guid userId)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0) return;

        // 批量加载发送者名称
        await EnrichSenderNamesAsync(itemList);

        // 批量加载已读状态
        var messageIds = itemList.Select(m => m.Id).ToList();
        var receives = await _receiveRepository
            .Where(mr => messageIds.Contains(mr.MessageId) && mr.UserId == userId)
            .ToListAsync();
        var receiveDict = receives.ToDictionary(r => r.MessageId);

        foreach (var item in itemList)
        {
            if (receiveDict.TryGetValue(item.Id, out var receive))
                item.IsRead = receive.ReadTime != null;
        }

        // 批量加载回复数量
        var replyCounts = await _replyRepository.AsQueryable()
            .Where(r => messageIds.Contains(r.BelongMessageId))
            .GroupBy(r => r.BelongMessageId)
            .Select(g => new { MessageId = g.Key, Count = g.Count() })
            .ToListAsync();
        var replyCountDict = replyCounts.ToDictionary(x => x.MessageId, x => x.Count);

        foreach (var item in itemList)
        {
            if (replyCountDict.TryGetValue(item.Id, out var count))
                item.ReplyCount = count;
        }
    }

    /// <summary>
    /// 批量填充发送者名称
    /// </summary>
    private async Task EnrichSenderNamesAsync(List<MessageListItemDto> items)
    {
        if (items.Count == 0) return;

        var senderIds = items.Select(i => i.SenderId).Distinct().ToList();
        var senders = await _userRepository
            .Where(u => senderIds.Contains(u.Id))
            .ToListAsync();
        var senderDict = senders.ToDictionary(s => s.Id, s => s.UserName);

        foreach (var item in items)
        {
            if (senderDict.TryGetValue(item.SenderId, out var name))
                item.SenderName = name;
        }
    }
}
