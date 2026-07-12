using Tnzi.Data;

namespace Tnzi.Chat.Services;

/// <summary>
/// 系统级管理员 Chat 维护服务实现。跨用户全局视角，不做会话成员门控。
/// </summary>
public class ChatAdminService : ApplicationService, IChatAdminService
{
    private readonly IRepository<Conversation, Guid> _conversationRepository;
    private readonly IRepository<ConversationMember, Guid> _memberRepository;
    private readonly IRepository<ChatMessage, Guid> _messageRepository;
    private readonly IRepository<UserPresence, Guid> _presenceRepository;
    private readonly IRepository<BroadcastLog, Guid> _broadcastLogRepository;
    private readonly IChatContactService _contactService;
    private readonly IOptionsSnapshot<ChatOptions> _options;
    private readonly IConnectionManager? _connectionManager;

    public ChatAdminService(
        IServiceProvider serviceProvider,
        IRepository<Conversation, Guid> conversationRepository,
        IRepository<ConversationMember, Guid> memberRepository,
        IRepository<ChatMessage, Guid> messageRepository,
        IRepository<UserPresence, Guid> presenceRepository,
        IRepository<BroadcastLog, Guid> broadcastLogRepository,
        IChatContactService contactService,
        IOptionsSnapshot<ChatOptions> options,
        IConnectionManager? connectionManager = null) : base(serviceProvider)
    {
        _conversationRepository = Check.NotNull(conversationRepository);
        _memberRepository = Check.NotNull(memberRepository);
        _messageRepository = Check.NotNull(messageRepository);
        _presenceRepository = Check.NotNull(presenceRepository);
        _broadcastLogRepository = Check.NotNull(broadcastLogRepository);
        _contactService = Check.NotNull(contactService);
        _options = Check.NotNull(options);
        _connectionManager = connectionManager;
    }

    public async Task<Result<ChatStatisticsDto>> GetStatisticsAsync()
    {
        var convs = _conversationRepository.AsQueryable();
        var msgs = _messageRepository.AsQueryable();
        var today = DateTime.UtcNow.Date;

        var dto = new ChatStatisticsDto
        {
            TotalConversations = await convs.CountAsync(),
            DirectConversations = await convs.CountAsync(c => c.Type == ConversationType.Direct),
            GroupConversations = await convs.CountAsync(c => c.Type == ConversationType.Group),
            SystemConversations = await convs.CountAsync(c => c.Type == ConversationType.System),
            TotalMessages = await msgs.CountAsync(),
            MessagesToday = await msgs.CountAsync(m => m.SentAt >= today),
            ActiveMembers = await _memberRepository.AsQueryable()
                .Where(m => m.RemovedAt == null).Select(m => m.UserId).Distinct().CountAsync(),
            OnlineUsers = _connectionManager != null ? await _connectionManager.GetOnlineUserCountAsync() : 0,
        };
        return Ok(dto);
    }

    public async Task<Result<IPagedList<AdminConversationListItemDto>>> GetConversationsAsync(AdminConversationQueryDto query)
    {
        Check.NotNull(query);
        var q = _conversationRepository.AsQueryable();

        if (query.Type.HasValue)
            q = q.Where(c => c.Type == query.Type.Value);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim().ToLower();
            q = q.Where(c => c.Title != null && c.Title.ToLower().Contains(kw));
        }

        if (query.UserId.HasValue)
        {
            var uid = query.UserId.Value;
            var convIds = (await _memberRepository.ToListAsync(m => m.UserId == uid && m.RemovedAt == null))
                .Select(m => m.ConversationId).ToHashSet();
            if (convIds.Count == 0)
                return Ok<IPagedList<AdminConversationListItemDto>>(
                    new PagedList<AdminConversationListItemDto>([], query.PageIndex, query.PageSize, 0));
            q = q.Where(c => convIds.Contains(c.Id));
        }

        q = q.OrderByDescending(c => c.LastMessageAt).ThenByDescending(c => c.CreationTime);

        var total = await q.CountAsync();
        var rows = await q.Skip(query.Skip).Take(query.Take).ToListAsync();

        var items = await MapListAsync(rows);
        return Ok<IPagedList<AdminConversationListItemDto>>(
            new PagedList<AdminConversationListItemDto>(items, query.PageIndex, query.PageSize, total));
    }

    public async Task<Result<AdminConversationDetailDto>> GetConversationDetailAsync(Guid conversationId)
    {
        var conv = await _conversationRepository.FindAsync(conversationId);
        if (conv == null) return Fail<AdminConversationDetailDto>("Conversation not found.", 404);

        var members = await _memberRepository.ToListAsync(m => m.ConversationId == conversationId && m.RemovedAt == null);
        var messageCount = await _messageRepository.AsQueryable().CountAsync(m => m.ConversationId == conversationId);

        var userIds = members.Select(m => m.UserId).ToList();
        if (conv.OwnerId.HasValue) userIds.Add(conv.OwnerId.Value);
        var profiles = await _contactService.ResolveProfilesAsync(userIds.Distinct().ToList());

        var dto = new AdminConversationDetailDto
        {
            Id = conv.Id,
            Type = conv.Type,
            Title = conv.Title,
            Notice = conv.Notice,
            OwnerId = conv.OwnerId,
            OwnerName = conv.OwnerId.HasValue && profiles.TryGetValue(conv.OwnerId.Value, out var op) ? op.Name : null,
            DirectKey = conv.DirectKey,
            MemberCount = conv.MemberCount,
            MessageCount = messageCount,
            LastMessageAt = conv.LastMessageAt,
            CreationTime = conv.CreationTime,
            Members = members.Select(m =>
            {
                profiles.TryGetValue(m.UserId, out var p);
                return new AdminConversationMemberDto
                {
                    UserId = m.UserId,
                    Name = p?.Name ?? string.Empty,
                    AvatarFileId = p?.AvatarFileId,
                    Role = m.Role,
                    Alias = m.Alias,
                    UnreadCount = m.UnreadCount,
                    LastReadAt = m.LastReadAt,
                    JoinedAt = m.CreationTime
                };
            }).ToList()
        };
        return Ok(dto);
    }

    public async Task<Result<MessageThreadDto>> GetConversationMessagesAsync(Guid conversationId, MessageThreadQueryDto query)
    {
        Check.NotNull(query);
        var conv = await _conversationRepository.FindAsync(conversationId);
        if (conv == null) return Fail<MessageThreadDto>("Conversation not found.", 404);

        var limit = query.Limit <= 0 || query.Limit > 100 ? 30 : query.Limit;

        DateTime? beforeAt = null;
        if (query.Before.HasValue)
            beforeAt = (await _messageRepository.FindAsync(query.Before.Value))?.SentAt;

        var mq = _messageRepository.AsQueryable().Where(m => m.ConversationId == conversationId);
        if (beforeAt.HasValue) mq = mq.Where(m => m.SentAt < beforeAt.Value);

        var page = await mq.OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).Take(limit + 1).ToListAsync();
        var hasMore = page.Count > limit;
        var slice = page.Take(limit).OrderBy(m => m.SentAt).ThenBy(m => m.Id).ToList();

        var senderIds = slice.Where(m => m.SenderId.HasValue).Select(m => m.SenderId!.Value).Distinct().ToList();
        var profiles = await _contactService.ResolveProfilesAsync(senderIds);
        var dtos = slice.Select(m =>
        {
            var d = m.MapTo<ChatMessageDto>();
            if (m.SenderId.HasValue && profiles.TryGetValue(m.SenderId.Value, out var p))
            {
                d.SenderName = p.Name;
                d.SenderAvatarFileId = p.AvatarFileId;
            }
            return d;
        }).ToList();

        return Ok(new MessageThreadDto { Messages = dtos, HasMore = hasMore });
    }

    public async Task<Result> DeleteConversationAsync(Guid conversationId)
    {
        var conv = await _conversationRepository.FindAsync(conversationId);
        if (conv == null) return Fail("Conversation not found.", 404);

        var memberIds = (await _memberRepository.ToListAsync(m => m.ConversationId == conversationId && m.RemovedAt == null))
            .Select(m => m.UserId).ToList();

        await _conversationRepository.DeleteAsync(conv); // soft-delete (FullAudited)

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new ConversationChangedEvent
            {
                ConversationId = conversationId,
                ChangeType = ConversationChangeType.Dissolved,
                AffectedUserIds = memberIds
            });
        }
        return Ok();
    }

    public async Task<Result> DeleteMessageAsync(Guid messageId)
    {
        var msg = await _messageRepository.FindAsync(messageId);
        if (msg == null) return Fail("Message not found.", 404);

        await _messageRepository.DeleteAsync(msg); // soft-delete = recall, admin override (any sender)
        return Ok();
    }

    public async Task<Result<PresenceOverviewDto>> GetPresenceOverviewAsync(PresenceOverviewQueryDto query)
    {
        Check.NotNull(query);

        var records = await _presenceRepository.ToListAsync();
        if (query.Status.HasValue)
            records = records.Where(r => r.Status == query.Status.Value).ToList();

        var tracking = _connectionManager != null;
        var allowInvisible = _options.Value.AllowInvisible;
        var onlineIds = tracking
            ? (await _connectionManager!.GetAllOnlineUserIdsAsync()).ToHashSet()
            : new HashSet<Guid>();

        var userIds = records.Select(r => r.UserId).Distinct().ToList();
        var profiles = await _contactService.ResolveProfilesAsync(userIds);

        var users = new List<PresenceUserDto>();
        foreach (var r in records)
        {
            var hasConn = onlineIds.Contains(r.UserId);
            var effective = ComputeEffective(r.Status, hasConn, tracking, allowInvisible);
            if (query.OnlineOnly && effective == UserPresenceStatus.Offline) continue;

            profiles.TryGetValue(r.UserId, out var p);
            users.Add(new PresenceUserDto
            {
                UserId = r.UserId,
                Name = p?.Name ?? string.Empty,
                AvatarFileId = p?.AvatarFileId,
                IntentStatus = r.Status,
                EffectiveStatus = effective,
                HasConnection = hasConn,
                LastSeenAt = r.LastSeenAt,
                LastChangedAt = r.LastChangedAt
            });
        }

        var dto = new PresenceOverviewDto
        {
            Total = users.Count,
            Online = users.Count(u => u.EffectiveStatus == UserPresenceStatus.Online),
            Away = users.Count(u => u.EffectiveStatus == UserPresenceStatus.Away),
            Busy = users.Count(u => u.EffectiveStatus == UserPresenceStatus.Busy),
            Offline = users.Count(u => u.EffectiveStatus == UserPresenceStatus.Offline),
            Users = users
                .OrderByDescending(u => u.EffectiveStatus != UserPresenceStatus.Offline)
                .ThenByDescending(u => u.LastSeenAt ?? DateTime.MinValue)
                .ToList()
        };
        return Ok(dto);
    }

    public async Task<Result<IPagedList<BroadcastLogDto>>> GetBroadcastsAsync(PagedQueryDto query)
    {
        Check.NotNull(query);
        var q = _broadcastLogRepository.AsQueryable().OrderByDescending(b => b.CreationTime);

        var total = await q.CountAsync();
        var rows = await q.Skip(query.Skip).Take(query.Take).ToListAsync();

        var senderIds = rows.Where(b => b.SenderId.HasValue).Select(b => b.SenderId!.Value).Distinct().ToList();
        var profiles = await _contactService.ResolveProfilesAsync(senderIds);

        var items = rows.Select(b => new BroadcastLogDto
        {
            Id = b.Id,
            Content = b.Content,
            TargetType = b.TargetType,
            TargetSummary = b.TargetSummary,
            RecipientCount = b.RecipientCount,
            SenderId = b.SenderId,
            SenderName = b.SenderId.HasValue && profiles.TryGetValue(b.SenderId.Value, out var p) ? p.Name : null,
            Source = b.Source,
            CreationTime = b.CreationTime
        }).ToList();

        return Ok<IPagedList<BroadcastLogDto>>(
            new PagedList<BroadcastLogDto>(items, query.PageIndex, query.PageSize, total));
    }

    /// <summary>有效状态解析，与 <see cref="PresenceService.ResolveEffectiveAsync"/> 同语义。</summary>
    private static UserPresenceStatus ComputeEffective(UserPresenceStatus intent, bool hasConnection, bool connectionTracking, bool allowInvisible)
    {
        // 部署禁用隐身时，历史隐身意图不再隐藏——按在线意图解析（仍受连接约束）。
        if (!allowInvisible && intent == UserPresenceStatus.Invisible)
            intent = UserPresenceStatus.Online;
        if (intent == UserPresenceStatus.Invisible || intent == UserPresenceStatus.Offline)
            return UserPresenceStatus.Offline;
        if (!connectionTracking)
            return intent; // manual-only (no SignalR) — show chosen status as-is
        return hasConnection ? intent : UserPresenceStatus.Offline;
    }

    private async Task<List<AdminConversationListItemDto>> MapListAsync(List<Conversation> rows)
    {
        if (rows.Count == 0) return [];

        var ownerIds = rows.Where(c => c.OwnerId.HasValue).Select(c => c.OwnerId!.Value).ToList();

        // Direct/System conversations derive a friendly title from their members.
        var peerConvIds = rows
            .Where(c => c.Type == ConversationType.Direct || c.Type == ConversationType.System)
            .Select(c => c.Id).ToHashSet();
        var peerMembers = peerConvIds.Count > 0
            ? await _memberRepository.ToListAsync(m => peerConvIds.Contains(m.ConversationId) && m.RemovedAt == null)
            : new List<ConversationMember>();
        var memberIdsByConv = peerMembers
            .GroupBy(m => m.ConversationId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.UserId).ToList());

        var allUserIds = ownerIds.Concat(peerMembers.Select(m => m.UserId)).Distinct().ToList();
        var profiles = await _contactService.ResolveProfilesAsync(allUserIds);

        return rows.Select(c => new AdminConversationListItemDto
        {
            Id = c.Id,
            Type = c.Type,
            Title = ResolveTitle(c, memberIdsByConv, profiles),
            OwnerId = c.OwnerId,
            OwnerName = c.OwnerId.HasValue && profiles.TryGetValue(c.OwnerId.Value, out var op) ? op.Name : null,
            MemberCount = c.MemberCount,
            LastMessagePreview = c.LastMessagePreview,
            LastMessageAt = c.LastMessageAt,
            CreationTime = c.CreationTime
        }).ToList();
    }

    private static string? ResolveTitle(
        Conversation c,
        Dictionary<Guid, List<Guid>> memberIdsByConv,
        IReadOnlyDictionary<Guid, ChatContactDto> profiles)
    {
        if (c.Type == ConversationType.Group)
            return c.Title;

        memberIdsByConv.TryGetValue(c.Id, out var ids);
        var names = (ids ?? [])
            .Select(id => profiles.TryGetValue(id, out var p) ? p.Name : null)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        if (c.Type == ConversationType.System)
            return names.Count > 0 ? $"System: {names[0]}" : "System Notifications";

        // Direct
        return names.Count > 0 ? string.Join(", ", names) : null;
    }
}
