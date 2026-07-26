namespace Tnzi.Identity.Presence.Services;

public class PresenceService : ApplicationService, IPresenceService
{
    private readonly IRepository<UserPresence, Guid> _repository;
    private readonly IOptionsSnapshot<PresenceOptions> _options;
    private readonly IConnectionManager? _connectionManager;

    public PresenceService(
        IServiceProvider serviceProvider,
        IRepository<UserPresence, Guid> repository,
        IOptionsSnapshot<PresenceOptions> options,
        IConnectionManager? connectionManager = null) : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _options = Check.NotNull(options);
        _connectionManager = connectionManager;
    }

    public async Task<Result> SetStatusAsync(UserPresenceStatus status)
    {
        // Offline 不作为手动意图；越界回落 Online。
        if (status == UserPresenceStatus.Offline) status = UserPresenceStatus.Online;

        // 部署禁用隐身时拒绝隐身意图（前端已隐藏该选项，此处为服务端强制）。
        if (status == UserPresenceStatus.Invisible && !_options.Value.AllowInvisible)
            return Fail("Invisible status is disabled by the administrator.", 403);

        var me = GetRequiredCurrentUser().Id!.Value;

        await UpsertAsync(me, p =>
        {
            p.Status = status;
            // 手动切换即视为活动，清除 auto-away 标记。
            p.IsAutoAway = false;
            p.LastActivityAt = DateTime.UtcNow;
            p.LastChangedAt = DateTime.UtcNow;
        });

        await NotifyChangedAsync(me);
        return Ok();
    }

    public async Task<UserPresenceStatus> GetMyStatusAsync()
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        var p = await _repository.FirstOrDefaultAsync(x => x.UserId == me);
        return p?.Status ?? UserPresenceStatus.Online;
    }

    public async Task<IReadOnlyList<UserPresenceDto>> ResolveEffectiveAsync(IReadOnlyCollection<Guid> userIds)
    {
        if (userIds == null || userIds.Count == 0) return Array.Empty<UserPresenceDto>();
        var idSet = userIds.Distinct().ToHashSet();
        var records = (await _repository.ToListAsync(p => idSet.Contains(p.UserId)))
            .ToDictionary(p => p.UserId);
        var opt = _options.Value;
        var tracking = _connectionManager != null;

        var result = new List<UserPresenceDto>(idSet.Count);
        foreach (var id in idSet)
        {
            records.TryGetValue(id, out var rec);
            var intent = rec?.Status ?? UserPresenceStatus.Online;
            var isAutoAway = rec?.IsAutoAway ?? false;
            var hasConn = tracking && await _connectionManager!.IsUserOnlineAsync(id);
            var effective = PresenceResolver.Resolve(
                intent, hasConn, tracking, opt.AllowInvisible, isAutoAway, opt.AutoAwayEnabled);
            result.Add(new UserPresenceDto { UserId = id, Status = effective, LastSeenAt = rec?.LastSeenAt });
        }
        return result;
    }

    public async Task<Result> ReportActivityAsync(bool active)
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        var autoAwayEnabled = _options.Value.AutoAwayEnabled;
        var changed = false;

        // 走 UpsertAsync（其 DbUpdateException race guard 包裹整个 UoW，见下）；ApplyActivity 既改行又
        // 回报有效状态是否翻转。race 重试路径会对真正落库的行再跑一次 mutate，故 changed 反映最终结果。
        await UpsertAsync(me, p => changed = ApplyActivity(p, active, autoAwayEnabled));

        // 仅在有效状态确实发生翻转时才推送（避免活动心跳造成推送风暴）。
        if (changed) await NotifyChangedAsync(me);
        return Ok();
    }

    /// <summary>把一次活动/空闲上报应用到 presence 行；返回有效状态是否发生翻转（翻转时同步 LastChangedAt）。</summary>
    private static bool ApplyActivity(UserPresence row, bool active, bool autoAwayEnabled)
    {
        var now = DateTime.UtcNow;
        if (active)
        {
            row.LastActivityAt = now;
            if (row.IsAutoAway)
            {
                row.IsAutoAway = false;
                row.LastChangedAt = now;
                return true; // 从 auto-away 恢复在线
            }
            return false;
        }

        // 客户端上报空闲：仅当启用 auto-away 且当前意图为 Online 且尚未标记时才翻转。
        if (autoAwayEnabled && !row.IsAutoAway && row.Status == UserPresenceStatus.Online)
        {
            row.IsAutoAway = true;
            row.LastChangedAt = now;
            return true;
        }
        return false;
    }

    public async Task MarkActiveAsync(Guid userId)
    {
        // 上线（offline→online）是有效状态变化 → 同步 LastChangedAt。
        await UpsertAsync(userId, p =>
        {
            p.IsAutoAway = false;
            p.LastActivityAt = DateTime.UtcNow;
            p.LastChangedAt = DateTime.UtcNow;
        });
    }

    public async Task MarkOfflineAsync(Guid userId)
    {
        // 下线（online→offline）是有效状态变化 → 同步 LastSeenAt + LastChangedAt。
        await UpsertAsync(userId, p =>
        {
            p.LastSeenAt = DateTime.UtcNow;
            p.LastChangedAt = DateTime.UtcNow;
        });
    }

    public async Task NotifyChangedAsync(Guid userId)
    {
        var dto = (await ResolveEffectiveAsync(new[] { userId })).FirstOrDefault();
        if (dto == null) return;
        await EventBus.PublishAsync(new UserPresenceChangedEvent
        {
            UserId = userId,
            Status = dto.Status,
            LastSeenAt = dto.LastSeenAt
        });
    }

    /// <summary>
    /// 每用户一行的乐观 upsert。★DbUpdateException race guard 必须**包裹整个** <c>ExecuteInUnitOfWorkAsync</c>
    /// 调用（而非 lambda 内的 <c>InsertAsync</c>）——启用事务时 <c>InsertAsync</c> 只 <c>AddAsync</c> 不发 SQL、
    /// 不抛异常，UserId 唯一索引冲突在 **commit 时**（lambda 之外）才抛，故只能在这一层捕获；捕获后须在一个
    /// **全新 UoW** 里重查并应用变更（commit 已 fault 的 DbContext 不可复用）。与
    /// <c>ConversationService.GetOrCreateDirectAsync</c> 同款。
    /// </summary>
    private async Task UpsertAsync(Guid userId, Action<UserPresence> mutate)
    {
        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                var existing = await _repository.AsQueryable(withTracking: true)
                    .FirstOrDefaultAsync(p => p.UserId == userId, ct);
                if (existing == null)
                {
                    var row = new UserPresence
                    {
                        UserId = userId, Status = UserPresenceStatus.Online, LastChangedAt = DateTime.UtcNow
                    };
                    mutate(row);
                    await _repository.InsertAsync(row, ct);
                }
                else
                {
                    mutate(existing);
                    await _repository.UpdateAsync(existing, ct);
                }
            });
        }
        catch (DbUpdateException)
        {
            // 并发会话抢先插入撞 UserId 唯一索引（commit 时抛）→ 在全新 UoW 里重查后应用变更。
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                var raced = await _repository.AsQueryable(withTracking: true)
                    .FirstOrDefaultAsync(p => p.UserId == userId, ct);
                if (raced == null) return;
                mutate(raced);
                await _repository.UpdateAsync(raced, ct);
            });
        }
    }
}
