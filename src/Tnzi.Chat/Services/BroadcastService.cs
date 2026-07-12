namespace Tnzi.Chat.Services;

public class BroadcastService : ApplicationService, IBroadcastService
{
    private readonly IRepository<Conversation, Guid> _conversationRepository;
    private readonly IRepository<ConversationMember, Guid> _memberRepository;
    private readonly IRepository<ChatMessage, Guid> _messageRepository;
    private readonly IRepository<User, Guid> _userRepository;
    private readonly IRepository<BroadcastLog, Guid> _broadcastLogRepository;
    private readonly IOptions<MultiTenancyOptions> _multiTenancyOptions;
    private readonly IUserRoleService? _userRoleService;

    public BroadcastService(
        IServiceProvider serviceProvider,
        IRepository<Conversation, Guid> conversationRepository,
        IRepository<ConversationMember, Guid> memberRepository,
        IRepository<ChatMessage, Guid> messageRepository,
        IRepository<User, Guid> userRepository,
        IRepository<BroadcastLog, Guid> broadcastLogRepository,
        IOptions<MultiTenancyOptions> multiTenancyOptions,
        IUserRoleService? userRoleService = null) : base(serviceProvider)
    {
        _conversationRepository = Check.NotNull(conversationRepository);
        _memberRepository = Check.NotNull(memberRepository);
        _messageRepository = Check.NotNull(messageRepository);
        _userRepository = Check.NotNull(userRepository);
        _broadcastLogRepository = Check.NotNull(broadcastLogRepository);
        _multiTenancyOptions = Check.NotNull(multiTenancyOptions);
        _userRoleService = userRoleService;
    }

    public Task<Result<int>> BroadcastToUsersAsync(IEnumerable<Guid> userIds, string content)
    {
        Check.NotNullOrWhiteSpace(content, nameof(content));
        return SendToUsersAsync(userIds, new ChatNotification { Content = content }, BroadcastTargetType.Users);
    }

    public Task<Result<int>> NotifyUsersAsync(IEnumerable<Guid> userIds, ChatNotification notification)
    {
        Check.NotNull(notification);
        Check.NotNullOrWhiteSpace(notification.Content, nameof(notification.Content));
        return SendToUsersAsync(userIds, notification, BroadcastTargetType.Users);
    }

    public Task<Result<int>> BroadcastToRoleAsync(Guid roleId, string content)
    {
        Check.NotNullOrWhiteSpace(content, nameof(content));
        return NotifyRoleAsync(roleId, new ChatNotification { Content = content });
    }

    public async Task<Result<int>> NotifyRoleAsync(Guid roleId, ChatNotification notification)
    {
        Check.NotNull(notification);
        Check.NotNullOrWhiteSpace(notification.Content, nameof(notification.Content));
        if (_userRoleService == null)
            return Fail<int>("Role broadcast requires Identity module.", 501);
        var userIds = await _userRoleService.GetRoleUserIdsAsync(roleId);
        return await SendToUsersAsync(userIds, notification, BroadcastTargetType.Roles, "1 role(s)");
    }

    public async Task<Result<int>> BroadcastAsync(BroadcastDto input)
    {
        Check.NotNull(input);
        Check.NotNullOrWhiteSpace(input.Content, nameof(input.Content));

        // System-wide notification: enumerate every (non-deleted) user. The repository's
        // global query filter excludes soft-deleted users. This loads user rows to project
        // their ids — acceptable for an admin broadcast (the per-user fan-out below is the
        // dominant cost); very large deployments would override with a queued mechanism.
        BroadcastTargetType targetType;
        string summary;
        List<Guid> targetIds;

        if (input.All)
        {
            // Multi-tenant guard: User is NOT IMultiTenant (no TenantId), so the global query
            // filter cannot scope this enumeration to the current tenant — "every user" would
            // span all tenants and leak one tenant's broadcast to the others. Require explicit
            // role/user targeting in multi-tenant mode.
            if (_multiTenancyOptions.Value.Enabled)
                return Fail<int>("System-wide broadcast is not supported in multi-tenant mode; target by role or user instead.", 400);

            var allUsers = await _userRepository.ToListAsync(u => true);
            targetIds = allUsers.Select(u => u.Id).ToList();
            targetType = BroadcastTargetType.All;
            summary = "All users";
        }
        else
        {
            var targets = new HashSet<Guid>();
            if (input.UserIds != null) foreach (var u in input.UserIds) targets.Add(u);
            if (input.RoleIds != null && _userRoleService != null)
            {
                foreach (var roleId in input.RoleIds)
                {
                    var users = await _userRoleService.GetRoleUserIdsAsync(roleId);
                    foreach (var u in users) targets.Add(u);
                }
            }
            if (targets.Count == 0) return Fail<int>("No broadcast targets resolved.", 400);
            targetIds = targets.ToList();

            var roleCount = input.RoleIds?.Count ?? 0;
            var userCount = input.UserIds?.Count ?? 0;
            if (roleCount > 0)
            {
                targetType = BroadcastTargetType.Roles;
                summary = userCount > 0 ? $"{roleCount} role(s) + {userCount} user(s)" : $"{roleCount} role(s)";
            }
            else
            {
                targetType = BroadcastTargetType.Users;
                summary = $"{userCount} user(s)";
            }
        }

        // Admin-UI broadcast: plain text, provenance carried by the CurrentUser (SenderId).
        // Unlike the direct/rich paths this always records the log — even a resolved-but-empty
        // "All users" audience is a real admin action worth an audit row.
        var notification = new ChatNotification { Content = input.Content };
        var delivered = await DeliverAsync(targetIds, notification);
        await RecordBroadcastAsync(notification, targetType, summary, delivered);
        return Ok(delivered);
    }

    /// <summary>
    /// 直接/富通知发送路径：规范化目标、无目标短路（不记日志），否则投递并记一次审计。
    /// </summary>
    private async Task<Result<int>> SendToUsersAsync(
        IEnumerable<Guid> userIds, ChatNotification notification, BroadcastTargetType targetType, string? summary = null)
    {
        var ids = (userIds ?? Enumerable.Empty<Guid>()).Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return Ok(0);

        var delivered = await DeliverAsync(ids, notification);
        await RecordBroadcastAsync(notification, targetType, summary ?? $"{ids.Count} user(s)", delivered);
        return Ok(delivered);
    }

    /// <summary>
    /// 逐用户投递到其「系统通知」会话（幂等 DirectKey `system:{userId:N}`）。纯 fan-out，不记审计。
    /// 返回实际投递人数。
    /// </summary>
    private async Task<int> DeliverAsync(List<Guid> ids, ChatNotification notification)
    {
        var now = DateTime.UtcNow;
        var preview = notification.Content.Length <= 100 ? notification.Content : notification.Content[..100];
        var delivered = 0;

        foreach (var uid in ids)
        {
            // Return entity references (not Guid copies) so we read real IDs after commit,
            // when SaveChangesAsync has written the generated GUIDs back onto the objects.
            var (conv, msg) = await ExecuteInUnitOfWorkAsync(async ct =>
            {
                var key = $"system:{uid:N}";

                // Load conversation with tracking so mutation (LastMessageAt/preview) + UpdateAsync
                // on the reuse path doesn't conflict with EF's change tracker.
                var conv = await _conversationRepository.AsQueryable(withTracking: true)
                    .FirstOrDefaultAsync(c => c.DirectKey == key, ct);

                if (conv == null)
                {
                    conv = new Conversation
                    {
                        Type = ConversationType.System,
                        DirectKey = key,
                        Title = "System Notifications",
                        MemberCount = 1
                    };
                    await _conversationRepository.InsertAsync(conv, ct);

                    // Create member with UnreadCount=1 directly — avoids querying the DB for
                    // the just-inserted member whose Id hasn't been generated yet (only happens
                    // in SaveChangesAsync).
                    var newMember = new ConversationMember
                    {
                        ConversationId = conv.Id,
                        UserId = uid,
                        Role = MemberRole.Member,
                        UnreadCount = 1
                    };
                    await _memberRepository.InsertAsync(newMember, ct);
                }
                else
                {
                    // Reuse path: load member with tracking; if missing (data inconsistency),
                    // insert rather than UpdateAsync on an untracked object (silent 0-row UPDATE).
                    var existingMember = await _memberRepository.AsQueryable(withTracking: true)
                        .FirstOrDefaultAsync(m => m.ConversationId == conv.Id && m.UserId == uid, ct);
                    if (existingMember != null)
                    {
                        existingMember.UnreadCount += 1;
                        // A new broadcast re-surfaces a hidden System conversation.
                        existingMember.IsHidden = false;
                        await _memberRepository.UpdateAsync(existingMember, ct);
                    }
                    else
                    {
                        var missingMember = new ConversationMember
                        {
                            ConversationId = conv.Id,
                            UserId = uid,
                            Role = MemberRole.Member,
                            UnreadCount = 1
                        };
                        await _memberRepository.InsertAsync(missingMember, ct);
                    }
                }

                var msg = new ChatMessage
                {
                    ConversationId = conv.Id,
                    SenderId = null,
                    SentAt = now,
                    ContentType = MessageContentType.System,
                    Content = notification.Content,
                    Title = notification.Title,
                    LinkUrl = notification.LinkUrl,
                    Category = notification.Category
                };
                await _messageRepository.InsertAsync(msg, ct);

                conv.LastMessageAt = now;
                conv.LastMessagePreview = preview;
                await _conversationRepository.UpdateAsync(conv, ct);

                return (conv, msg);
            });

            // conv.Id / msg.Id now hold the real persisted GUIDs (written by SaveChangesAsync).
            if (EventBus != null)
            {
                await EventBus.PublishAsync(new ConversationMessageSentEvent
                {
                    ConversationId = conv.Id,
                    MessageId = msg.Id,
                    SenderId = null,
                    ContentType = MessageContentType.System,
                    Preview = preview,
                    RecipientUserIds = new List<Guid> { uid }
                });
            }

            delivered++;
        }

        return delivered;
    }

    /// <summary>记录一次广播（辅助操作，失败静默——不影响广播主流程）。</summary>
    private async Task RecordBroadcastAsync(ChatNotification notification, BroadcastTargetType targetType, string summary, int recipientCount)
    {
        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                await _broadcastLogRepository.InsertAsync(new BroadcastLog
                {
                    SenderId = CurrentUser?.Id,
                    Content = notification.Content,
                    TargetType = targetType,
                    TargetSummary = summary,
                    RecipientCount = recipientCount,
                    Source = notification.Source
                }, ct);
            });
        }
        catch
        {
            // Logging the broadcast is auxiliary; never fail the broadcast over it.
        }
    }
}
