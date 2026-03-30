namespace Tnzi.Chat.Services;

/// <summary>
/// 草稿消息服务实现
/// </summary>
public class DraftMessageService : ApplicationService, IDraftMessageService
{
    private readonly IRepository<Message, Guid> _messageRepository;
    private readonly IRepository<MessageReceive, Guid> _receiveRepository;
    private readonly IRepository<MessageRecipient, Guid> _recipientRepository;
    private readonly IRepository<MessageRole, Guid> _roleRepository;
    private readonly IRepository<User, Guid> _userRepository;
    private readonly IUserRoleService? _userRoleService;

    public DraftMessageService(
        IRepository<Message, Guid> messageRepository,
        IRepository<MessageReceive, Guid> receiveRepository,
        IRepository<MessageRecipient, Guid> recipientRepository,
        IRepository<MessageRole, Guid> roleRepository,
        IRepository<User, Guid> userRepository,
        IServiceProvider serviceProvider,
        IUserRoleService? userRoleService = null)
        : base(serviceProvider)
    {
        _messageRepository = Check.NotNull(messageRepository);
        _receiveRepository = Check.NotNull(receiveRepository);
        _recipientRepository = Check.NotNull(recipientRepository);
        _roleRepository = Check.NotNull(roleRepository);
        _userRepository = Check.NotNull(userRepository);
        _userRoleService = userRoleService;
    }

    /// <summary>
    /// 保存草稿（创建或更新）
    /// </summary>
    public async Task<Result<MessageDto>> SaveDraftAsync(SaveDraftDto input)
    {
        Check.NotNull(input);

        var currentUser = GetRequiredCurrentUser();
        var userId = currentUser.Id!.Value;

        // 更新已有草稿
        if (input.Id.HasValue)
        {
            var existing = await _messageRepository.GetAsync(input.Id.Value);
            if (existing == null)
                return Fail<MessageDto>("Draft not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

            if (existing.SenderId != userId)
                return Fail<MessageDto>("You can only update your own drafts", 403, ErrorCodes.FORBIDDEN);

            if (!existing.IsDraft)
                return Fail<MessageDto>("Message is not a draft", 400, ErrorCodes.VALIDATION_ERROR);

            // 更新草稿内容
            if (input.Title != null) existing.Title = input.Title;
            existing.Content = input.Content;
            if (input.MessageType.HasValue) existing.MessageType = input.MessageType.Value;
            existing.CanReply = input.CanReply;
            existing.IsImportant = input.IsImportant;

            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                await _messageRepository.UpdateAsync(existing);

                // 更新收件人关系
                await UpdateDraftRecipientsAsync(existing.Id, input);
            });

            var dto = await MapToDraftDtoAsync(existing);
            LogInformation("Draft updated: {DraftId}", existing.Id);
            return Ok(dto, "Draft saved successfully");
        }

        // 创建新草稿
        var message = new Message
        {
            Title = input.Title ?? string.Empty,
            Content = input.Content,
            MessageType = input.MessageType ?? MessageType.Private,
            SenderId = userId,
            IsSent = false,
            IsDraft = true,
            CanReply = input.CanReply,
            IsImportant = input.IsImportant
        };

        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await _messageRepository.InsertAsync(message);

            // 创建收件人/角色关系记录
            await CreateDraftRecipientsAsync(message.Id, input);
        });

        var resultDto = await MapToDraftDtoAsync(message);
        LogInformation("Draft created: {DraftId}, SenderId: {SenderId}", message.Id, userId);
        return Ok(resultDto, "Draft created successfully");
    }

    /// <summary>
    /// 获取当前用户的草稿列表
    /// </summary>
    public async Task<Result<List<MessageDto>>> GetMyDraftsAsync()
    {
        var currentUser = GetRequiredCurrentUser();
        var userId = currentUser.Id!.Value;

        var drafts = await _messageRepository.AsQueryable()
            .Where(m => m.SenderId == userId && m.IsDraft)
            .OrderByDescending(m => m.LastModificationTime ?? m.CreationTime)
            .ToListAsync();

        var dtos = new List<MessageDto>();
        foreach (var draft in drafts)
        {
            dtos.Add(await MapToDraftDtoAsync(draft));
        }

        return Ok(dtos);
    }

    /// <summary>
    /// 发送草稿（将草稿转为已发送消息）
    /// </summary>
    public async Task<Result<MessageDto>> SendDraftAsync(Guid draftId)
    {
        var currentUser = GetRequiredCurrentUser();
        var userId = currentUser.Id!.Value;

        var message = await _messageRepository.GetAsync(draftId);
        if (message == null)
            return Fail<MessageDto>("Draft not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (message.SenderId != userId)
            return Fail<MessageDto>("You can only send your own drafts", 403, ErrorCodes.FORBIDDEN);

        if (!message.IsDraft)
            return Fail<MessageDto>("Message is not a draft", 400, ErrorCodes.VALIDATION_ERROR);

        // 验证收件人
        if (message.MessageType == MessageType.Private)
        {
            var hasRecipients = await _recipientRepository
                .AnyAsync(r => r.MessageId == draftId);
            if (!hasRecipients)
                return Fail<MessageDto>("Private message requires at least one recipient", 400, ErrorCodes.VALIDATION_ERROR);
        }
        else if (message.MessageType == MessageType.Public)
        {
            var hasRoles = await _roleRepository
                .AnyAsync(r => r.MessageId == draftId);
            if (!hasRoles)
                return Fail<MessageDto>("Public message requires at least one role", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var recipientUserIds = new List<Guid>();

        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            // 创建 MessageReceive 记录
            if (message.MessageType == MessageType.Private)
            {
                var recipientIds = await _recipientRepository
                    .Where(r => r.MessageId == draftId)
                    .Select(r => r.UserId)
                    .ToListAsync(ct);

                var receives = recipientIds.Select(uid => new MessageReceive
                {
                    MessageId = draftId,
                    UserId = uid
                }).ToList();
                await _receiveRepository.InsertManyAsync(receives, ct);

                recipientUserIds.AddRange(recipientIds);
            }
            else if (message.MessageType == MessageType.Public && _userRoleService != null)
            {
                var roleIds = await _roleRepository
                    .Where(r => r.MessageId == draftId)
                    .Select(r => r.RoleId)
                    .ToListAsync(ct);

                var userIdSet = new HashSet<Guid>();
                foreach (var roleId in roleIds)
                {
                    var userIds = await _userRoleService.GetRoleUserIdsAsync(roleId);
                    foreach (var uid in userIds)
                        userIdSet.Add(uid);
                }

                if (userIdSet.Count > 0)
                {
                    var receives = userIdSet.Select(uid => new MessageReceive
                    {
                        MessageId = draftId,
                        UserId = uid
                    }).ToList();
                    await _receiveRepository.InsertManyAsync(receives, ct);
                    recipientUserIds.AddRange(userIdSet);
                }
            }

            // 标记为已发送
            message.IsDraft = false;
            message.IsSent = true;
            await _messageRepository.UpdateAsync(message);
        });

        // 发布消息发送事件（事务外）
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new MessageSentEvent
            {
                MessageId = message.Id,
                SenderId = userId,
                MessageType = message.MessageType,
                RecipientUserIds = recipientUserIds,
                Title = message.Title
            });
        }

        var dto = await MapToDraftDtoAsync(message);
        LogInformation("Draft sent: {DraftId}, SenderId: {SenderId}, Type: {MessageType}",
            draftId, userId, message.MessageType);
        return Ok(dto, "Draft sent successfully");
    }

    /// <summary>
    /// 删除草稿
    /// </summary>
    public async Task<Result> DeleteDraftAsync(Guid draftId)
    {
        var currentUser = GetRequiredCurrentUser();
        var userId = currentUser.Id!.Value;

        var message = await _messageRepository.GetAsync(draftId);
        if (message == null)
            return Fail("Draft not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (message.SenderId != userId)
            return Fail("You can only delete your own drafts", 403, ErrorCodes.FORBIDDEN);

        if (!message.IsDraft)
            return Fail("Message is not a draft", 400, ErrorCodes.VALIDATION_ERROR);

        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            // 删除关联的收件人和角色记录
            var recipients = await _recipientRepository
                .Where(r => r.MessageId == draftId)
                .ToListAsync(ct);
            if (recipients.Count > 0)
                await _recipientRepository.DeleteManyAsync(recipients, ct);

            var roles = await _roleRepository
                .Where(r => r.MessageId == draftId)
                .ToListAsync(ct);
            if (roles.Count > 0)
                await _roleRepository.DeleteManyAsync(roles, ct);

            await _messageRepository.DeleteAsync(draftId);
        });

        LogInformation("Draft deleted: {DraftId}, UserId: {UserId}", draftId, userId);
        return Ok("Draft deleted successfully");
    }

    /// <summary>
    /// 获取当前用户草稿数量
    /// </summary>
    public async Task<Result<int>> GetDraftCountAsync()
    {
        var currentUser = GetRequiredCurrentUser();
        var userId = currentUser.Id!.Value;

        var count = await _messageRepository.AsQueryable()
            .Where(m => m.SenderId == userId && m.IsDraft)
            .CountAsync();

        return Ok(count);
    }

    // === 私有辅助方法 ===

    /// <summary>
    /// 创建草稿的收件人/角色关系记录
    /// </summary>
    private async Task CreateDraftRecipientsAsync(Guid messageId, SaveDraftDto input)
    {
        if (input.RecipientIds is { Count: > 0 })
        {
            var recipients = input.RecipientIds.Select(uid => new MessageRecipient
            {
                MessageId = messageId,
                UserId = uid
            }).ToList();
            await _recipientRepository.InsertManyAsync(recipients);
        }

        if (input.RoleIds is { Count: > 0 })
        {
            var roles = input.RoleIds.Select(rid => new MessageRole
            {
                MessageId = messageId,
                RoleId = rid
            }).ToList();
            await _roleRepository.InsertManyAsync(roles);
        }
    }

    /// <summary>
    /// 更新草稿的收件人/角色关系记录
    /// </summary>
    private async Task UpdateDraftRecipientsAsync(Guid messageId, SaveDraftDto input)
    {
        // 清除旧记录
        var oldRecipients = await _recipientRepository
            .Where(r => r.MessageId == messageId)
            .ToListAsync();
        if (oldRecipients.Count > 0)
            await _recipientRepository.DeleteManyAsync(oldRecipients);

        var oldRoles = await _roleRepository
            .Where(r => r.MessageId == messageId)
            .ToListAsync();
        if (oldRoles.Count > 0)
            await _roleRepository.DeleteManyAsync(oldRoles);

        // 创建新记录
        await CreateDraftRecipientsAsync(messageId, input);
    }

    /// <summary>
    /// 映射草稿消息到 DTO
    /// </summary>
    private async Task<MessageDto> MapToDraftDtoAsync(Message message)
    {
        var dto = message.MapTo<MessageDto>();

        var sender = await _userRepository.GetAsync(message.SenderId);
        dto.SenderName = sender?.UserName;

        return dto;
    }
}
