namespace Tnzi.Chat.Services;

public class PresenceService : ApplicationService, IPresenceService
{
    public const string PresenceChangedMethod = "Chat.PresenceChanged";

    private readonly IRepository<UserPresence, Guid> _presenceRepository;
    private readonly IRepository<ConversationMember, Guid> _memberRepository;
    private readonly IRepository<Conversation, Guid> _conversationRepository;
    private readonly IOptionsSnapshot<ChatOptions> _options;
    private readonly IConnectionManager? _connectionManager;
    private readonly IMessagePushService? _push;

    public PresenceService(
        IServiceProvider serviceProvider,
        IRepository<UserPresence, Guid> presenceRepository,
        IRepository<ConversationMember, Guid> memberRepository,
        IRepository<Conversation, Guid> conversationRepository,
        IOptionsSnapshot<ChatOptions> options,
        IConnectionManager? connectionManager = null,
        IMessagePushService? push = null) : base(serviceProvider)
    {
        _presenceRepository = Check.NotNull(presenceRepository);
        _memberRepository = Check.NotNull(memberRepository);
        _conversationRepository = Check.NotNull(conversationRepository);
        _options = Check.NotNull(options);
        _connectionManager = connectionManager;
        _push = push;
    }

    public async Task<Result> SetStatusAsync(UserPresenceStatus status)
    {
        // Offline 不作为手动意图；越界回落 Online
        if (status == UserPresenceStatus.Offline) status = UserPresenceStatus.Online;

        // 部署禁用隐身时拒绝隐身意图（前端已隐藏该选项，此处为服务端强制）。
        if (status == UserPresenceStatus.Invisible && !_options.Value.AllowInvisible)
            return Fail("Invisible status is disabled by the administrator.", 403);

        var me = GetRequiredCurrentUser().Id!.Value;

        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            var existing = await _presenceRepository.AsQueryable(withTracking: true)
                .FirstOrDefaultAsync(p => p.UserId == me, ct);
            if (existing == null)
            {
                try
                {
                    // First manual status = "seen now"; stamp LastSeenAt on the initial insert.
                    await _presenceRepository.InsertAsync(new UserPresence
                    {
                        UserId = me, Status = status,
                        LastSeenAt = DateTime.UtcNow, LastChangedAt = DateTime.UtcNow
                    }, ct);
                }
                catch (DbUpdateException)
                {
                    // Concurrent session inserted first → unique UserId index conflict.
                    // Re-query the now-existing row and apply the update instead (mirrors
                    // ConversationService.GetOrCreateDirectAsync's DbUpdateException guard).
                    var raced = await _presenceRepository.AsQueryable(withTracking: true)
                        .FirstOrDefaultAsync(p => p.UserId == me, ct);
                    if (raced == null) throw;
                    raced.Status = status;
                    raced.LastChangedAt = DateTime.UtcNow;
                    await _presenceRepository.UpdateAsync(raced, ct);
                }
            }
            else
            {
                existing.Status = status;
                existing.LastChangedAt = DateTime.UtcNow;
                await _presenceRepository.UpdateAsync(existing, ct);
            }
        });

        await BroadcastAsync(me);
        return Ok();
    }

    public async Task<UserPresenceStatus> GetMyStatusAsync()
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        var p = await _presenceRepository.FirstOrDefaultAsync(x => x.UserId == me);
        return p?.Status ?? UserPresenceStatus.Online;
    }

    public async Task<IReadOnlyList<UserPresenceDto>> ResolveEffectiveAsync(IReadOnlyCollection<Guid> userIds)
    {
        if (userIds == null || userIds.Count == 0) return Array.Empty<UserPresenceDto>();
        var idSet = userIds.Distinct().ToHashSet();
        var records = (await _presenceRepository.ToListAsync(p => idSet.Contains(p.UserId)))
            .ToDictionary(p => p.UserId);
        var allowInvisible = _options.Value.AllowInvisible;

        var result = new List<UserPresenceDto>(idSet.Count);
        foreach (var id in idSet)
        {
            records.TryGetValue(id, out var rec);
            var intent = rec?.Status ?? UserPresenceStatus.Online;
            // 部署禁用隐身时，历史隐身意图不再对外隐藏——按在线意图解析（仍受连接状态约束）。
            if (!allowInvisible && intent == UserPresenceStatus.Invisible)
                intent = UserPresenceStatus.Online;
            var lastSeen = rec?.LastSeenAt;

            UserPresenceStatus effective;
            if (intent == UserPresenceStatus.Invisible || intent == UserPresenceStatus.Offline)
            {
                effective = UserPresenceStatus.Offline;
            }
            else if (_connectionManager == null)
            {
                // Manual-only mode (no SignalR): show the chosen status as-is.
                effective = intent;
            }
            else
            {
                var online = await _connectionManager.IsUserOnlineAsync(id);
                effective = online ? intent : UserPresenceStatus.Offline;
            }

            result.Add(new UserPresenceDto { UserId = id, Status = effective, LastSeenAt = lastSeen });
        }
        return result;
    }

    public async Task BroadcastAsync(Guid userId)
    {
        if (_push == null) return; // SignalR 未加载 → 无实时
        try
        {
            var dto = (await ResolveEffectiveAsync(new[] { userId })).FirstOrDefault();
            if (dto == null) return;

            // 联系人 = 与该用户共享非 System 会话的其他成员（distinct）。
            var myConvIds = (await _memberRepository.ToListAsync(m => m.UserId == userId && m.RemovedAt == null))
                .Select(m => m.ConversationId).ToHashSet();
            if (myConvIds.Count == 0) return;

            // 仅向「共享非 System 会话」的联系人推送（System 会话是每用户独立通知会话，不应外溢 presence）。
            var nonSystemConvIds = (await _conversationRepository.ToListAsync(
                    c => myConvIds.Contains(c.Id) && c.Type != ConversationType.System))
                .Select(c => c.Id).ToHashSet();
            if (nonSystemConvIds.Count == 0) return;

            var contacts = (await _memberRepository.ToListAsync(
                    m => nonSystemConvIds.Contains(m.ConversationId) && m.UserId != userId && m.RemovedAt == null))
                .Select(m => m.UserId).Distinct().ToList();
            if (contacts.Count == 0) return;

            await _push.PushToUsersAsync(contacts, PresenceChangedMethod, dto);
        }
        catch
        {
            // 推送为辅助操作，失败静默（与 ChatSignalREventHandler 一致）。
        }
    }

    public async Task MarkOfflineAsync(Guid userId)
    {
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            var existing = await _presenceRepository.AsQueryable(withTracking: true)
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (existing == null)
            {
                try
                {
                    await _presenceRepository.InsertAsync(new UserPresence
                    {
                        UserId = userId, Status = UserPresenceStatus.Online,
                        LastSeenAt = DateTime.UtcNow, LastChangedAt = DateTime.UtcNow
                    }, ct);
                }
                catch (DbUpdateException)
                {
                    // Concurrent insert raced us to the unique UserId index → re-query and update.
                    var raced = await _presenceRepository.AsQueryable(withTracking: true)
                        .FirstOrDefaultAsync(p => p.UserId == userId, ct);
                    if (raced == null) throw;
                    raced.LastSeenAt = DateTime.UtcNow;
                    await _presenceRepository.UpdateAsync(raced, ct);
                }
            }
            else
            {
                existing.LastSeenAt = DateTime.UtcNow;
                await _presenceRepository.UpdateAsync(existing, ct);
            }
        });
    }
}
