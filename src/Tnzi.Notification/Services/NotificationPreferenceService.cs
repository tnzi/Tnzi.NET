namespace Tnzi.Notification.Services;

/// <summary>
/// 通知偏好服务实现
/// </summary>
public class NotificationPreferenceService : ApplicationService, INotificationPreferenceService
{
    private readonly IRepository<Preference, Guid> _repository;

    public NotificationPreferenceService(
        IRepository<Preference, Guid> repository,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task<Result<IPagedList<NotificationPreferenceDto>>> GetPagedListAsync(NotificationPreferenceQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var filtered = _repository
            .AsQueryable()
            .AsNoTracking()
            .Where(p =>
                (!query.UserId.HasValue || p.UserId == query.UserId.Value) &&
                (string.IsNullOrEmpty(query.Channel) || p.Channel == query.Channel) &&
                (string.IsNullOrEmpty(query.Category) || p.Category == query.Category) &&
                (!query.IsEnabled.HasValue || p.IsEnabled == query.IsEnabled.Value));

        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .OrderByDescending(p => p.CreationTime)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

        var paged = new PagedList<NotificationPreferenceDto>(
            items.MapToList<NotificationPreferenceDto>(),
            query.PageIndex,
            query.PageSize,
            totalCount);

        return Ok<IPagedList<NotificationPreferenceDto>>(paged);
    }

    public async Task<Result<List<NotificationPreferenceDto>>> GetUserPreferencesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var preferences = await _repository
            .AsQueryable()
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Channel)
            .ThenBy(p => p.Category)
            .ToListAsync(cancellationToken);

        return Ok(preferences.MapToList<NotificationPreferenceDto>());
    }

    public async Task<Result<NotificationPreferenceDto>> SetPreferenceAsync(Guid userId, SetNotificationPreferenceDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        Check.NotNullOrWhiteSpace(input.Channel);

        // 验证静默时段：必须同时设置或同时为空
        if (input.QuietHoursStart.HasValue != input.QuietHoursEnd.HasValue)
            return Fail<NotificationPreferenceDto>("Quiet hours start and end must both be set or both be empty", 400);

        // 开始等于结束是空区间，永远不会生效：存下来只会让人以为自己设了免打扰
        if (input.QuietHoursStart.HasValue && input.QuietHoursStart == input.QuietHoursEnd)
            return Fail<NotificationPreferenceDto>("Quiet hours start and end must differ; a window that starts and ends at the same time never applies", 400);

        // 验证频率限制
        if (input.MaxFrequencyPerHour.HasValue && input.MaxFrequencyPerHour.Value <= 0)
            return Fail<NotificationPreferenceDto>("Max frequency per hour must be a positive number", 400);

        // 验证时区：只收规范大小写的 IANA 标识，原样存（理由见 IanaTimeZone）
        if (!IanaTimeZone.TryNormalize(input.QuietHoursTimeZone, out var timeZone))
            return Fail<NotificationPreferenceDto>(
                $"Unknown time zone '{input.QuietHoursTimeZone}'. Use an IANA time zone id such as 'America/Toronto'.",
                400, ErrorCodes.NOTIFICATION_INVALID_TIME_ZONE);

        // ★ 分类按读取路径同一个口径归一化：空串与空白就是「整渠道」（null）。原样存下的 "" 既不是渠道级行
        //   （读取路径按 Category == null 找它），也匹配不上任何消息分类（消息分类同样把空白归成 null），
        //   于是一行「关掉这个渠道」在管理端显示为已关闭、发送时却被忽略。清空过的表单输入框发来的就是 ""。
        var category = NormalizeCategory(input.Category);

        // 查找已有偏好（按 UserId + Channel + Category 唯一）
        var existing = await _repository
            .AsQueryable()
            .Where(p => p.UserId == userId
                && p.Channel == input.Channel
                && p.Category == category)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
        {
            // 更新已有记录
            existing.IsEnabled = input.IsEnabled;
            existing.QuietHoursStart = input.QuietHoursStart;
            existing.QuietHoursEnd = input.QuietHoursEnd;
            existing.QuietHoursTimeZone = timeZone;
            existing.MaxFrequencyPerHour = input.MaxFrequencyPerHour;
            await _repository.UpdateAsync(existing, cancellationToken);

            LogInformation("Updated notification preference {PreferenceId} for user {UserId}, channel: {Channel}",
                existing.Id, userId, input.Channel);

            return Ok(existing.MapTo<NotificationPreferenceDto>());
        }

        // 创建新记录
        var preference = new Preference
        {
            UserId = userId,
            Channel = input.Channel,
            Category = category,
            IsEnabled = input.IsEnabled,
            QuietHoursStart = input.QuietHoursStart,
            QuietHoursEnd = input.QuietHoursEnd,
            QuietHoursTimeZone = timeZone,
            MaxFrequencyPerHour = input.MaxFrequencyPerHour
        };

        await _repository.InsertAsync(preference, cancellationToken);
        // 环境事务下仓储会推迟 SaveChanges，而实体 Id 是框架在 SaveChanges 里生成的 ——
        // 不 flush 则返回的 DTO（与下面的日志）带 Guid.Empty，调用方拿不到可用于删除的 id。
        await _repository.SaveChangesAsync(cancellationToken);

        LogInformation("Created notification preference {PreferenceId} for user {UserId}, channel: {Channel}",
            preference.Id, userId, input.Channel);

        return Ok(preference.MapTo<NotificationPreferenceDto>());
    }

    /// <summary>管理端删除偏好所需的权限码（与 admin 控制器上的方法级码同源）。</summary>
    private const string SubscriptionDeletePermission = "notification.subscription.delete";

    /// <summary>
    /// 删除一条通知偏好。
    /// </summary>
    /// <remarks>
    /// ★ <b>必须带归属判定</b>：本方法同时被用户面
    /// （<c>DELETE /notification/preferences/{id}</c>，只有 <c>[ApiAuthorize]</c>）与管理端
    /// （带 <c>notification.subscription.delete</c>）调用 —— 它<b>无从知道谁在问</b>，
    /// 所以判定必须在这里做，而不是靠调用方。
    /// <para>
    /// 少了它的后果不是「看到别人的数据」而是**替别人改回默认**：任何已登录用户删掉他人的
    /// 偏好行，对方刚关掉的那个渠道就悄悄恢复成默认（发送）。这直接抵消了同一个模块里
    /// 刚接上的退订链路 —— 一个可以被别人撤销的「我不想收」等于没有。
    /// </para>
    /// <para>
    /// ★ 原实现连 <c>preference.UserId</c> 都写进了日志，也就是说它**知道这行有主人**，
    /// 只是从来没比对过。
    /// </para>
    /// <para>
    /// 拒绝按 <b>404</b> 出（框架铁律）：把「不存在」与「不是你的」分开回答，
    /// 等于告诉试探者哪些 id 是真的。
    /// </para>
    /// </remarks>
    public async Task<Result> DeletePreferenceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var preference = await _repository.GetAsync(id, cancellationToken);
        if (preference == null || !await CanManageAsync(preference.UserId))
            return Fail("Notification preference not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        await _repository.DeleteAsync(preference, cancellationToken);

        LogInformation("Deleted notification preference {PreferenceId} for user {UserId}", id, preference.UserId);
        return Ok("Notification preference deleted");
    }

    /// <summary>当前调用者能不能动 <paramref name="ownerUserId"/> 名下的偏好。</summary>
    private async Task<bool> CanManageAsync(Guid ownerUserId)
    {
        if (CurrentUser?.Id is { } currentUserId && currentUserId == ownerUserId)
            return true;

        var checker = PermissionChecker;
        return checker is not null && await checker.IsGrantedAsync(SubscriptionDeletePermission);
    }

    public async Task<Result> ResetToDefaultAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var preferences = await _repository
            .AsQueryable()
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        if (preferences.Count == 0)
            return Ok("No custom preferences to reset");

        await _repository.DeleteManyAsync(preferences, cancellationToken);

        LogInformation("Reset notification preferences for user {UserId}, removed {Count} preferences", userId, preferences.Count);
        return Ok($"Reset {preferences.Count} notification preferences to default");
    }

    public async Task<bool> IsChannelEnabledAsync(Guid userId, string channel, string? category = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(channel);

        // ★ 与 FilterEnabledUsersAsync 同一个口径：渠道不分大小写、分类把空白归成 null。
        //   此前这里按原样比较渠道：管理端存的 "Email" 对 IsChannelEnabledAsync(user, "email") 一行都匹配不上，
        //   答「启用」，而真正的发送路径会把这个人排除掉。
        var channelName = channel.ToLower();
        var normalizedCategory = NormalizeCategory(category);

        var rows = await _repository
            .AsQueryable()
            .AsNoTracking()
            .Where(p => p.UserId == userId
                        && p.Channel.ToLower() == channelName
                        && (p.Category == null || p.Category == normalizedCategory))
            .Select(p => new { p.Category, p.IsEnabled })
            .ToListAsync(cancellationToken);

        // 分类级优先于渠道级，两者都没有则默认启用。
        var categoryRow = normalizedCategory == null
            ? null
            : rows.Find(r => r.Category == normalizedCategory);
        var channelRow = rows.Find(r => r.Category == null);

        return categoryRow?.IsEnabled ?? channelRow?.IsEnabled ?? true;
    }

    /// <summary>分类归一化：空串与空白就是「整渠道」（null），与各读取路径对消息分类的处理一致。</summary>
    private static string? NormalizeCategory(string? category)
        => string.IsNullOrWhiteSpace(category) ? null : category;

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Guid>> FilterEnabledUsersAsync(
        IEnumerable<Guid> userIds, NotificationType channel, string? category = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(userIds);

        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var channelName = channel.ToString().ToLower();
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category;

        // 一次查完这一批人在该渠道上的全部相关偏好行（分类级 + 渠道级），在内存里定夺。
        // 逐个 IsChannelEnabledAsync 在一次千人群发上就是两千次往返。
        var rows = await _repository
            .AsQueryable()
            .AsNoTracking()
            .Where(p => ids.Contains(p.UserId)
                        && p.Channel.ToLower() == channelName
                        && (p.Category == null || p.Category == normalizedCategory))
            .Select(p => new { p.UserId, p.Category, p.IsEnabled })
            .ToListAsync(cancellationToken);

        var byUser = rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.ToList());

        var enabled = new List<Guid>(ids.Count);
        foreach (var id in ids)
        {
            if (!byUser.TryGetValue(id, out var userRows))
            {
                // 无偏好记录 = 默认启用（与 IsChannelEnabledAsync 一致）。
                // ★ 反过来（查不到当成已关闭）会让整份导入名单一条都发不出去。
                enabled.Add(id);
                continue;
            }

            // 分类级优先于渠道级，两者都没有则默认启用 —— 与 IsChannelEnabledAsync 同序同默认值。
            var categoryRow = normalizedCategory == null
                ? null
                : userRows.Find(r => r.Category == normalizedCategory);
            var channelRow = userRows.Find(r => r.Category == null);

            if (categoryRow?.IsEnabled ?? channelRow?.IsEnabled ?? true)
                enabled.Add(id);
        }

        return enabled;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> GetFrequencyCapsAsync(
        IEnumerable<Guid> userIds, NotificationType channel, string? category = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(userIds);

        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, int>();

        var channelName = channel.ToString().ToLower();
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category;

        // 一次查完（分类级 + 渠道级），在内存里定夺 —— 与 FilterEnabledUsersAsync 同款。
        var rows = await _repository
            .AsQueryable()
            .AsNoTracking()
            .Where(p => ids.Contains(p.UserId)
                        && p.Channel.ToLower() == channelName
                        && p.MaxFrequencyPerHour != null
                        && (p.Category == null || p.Category == normalizedCategory))
            .Select(p => new { p.UserId, p.Category, p.MaxFrequencyPerHour })
            .ToListAsync(cancellationToken);

        var caps = new Dictionary<Guid, int>();
        foreach (var group in rows.GroupBy(r => r.UserId))
        {
            var userRows = group.ToList();
            // 分类级优先于渠道级 —— 与 IsChannelEnabledAsync / FilterEnabledUsersAsync 同序。
            var categoryRow = normalizedCategory == null
                ? null
                : userRows.Find(r => r.Category == normalizedCategory);
            var channelRow = userRows.Find(r => r.Category == null);

            var cap = categoryRow?.MaxFrequencyPerHour ?? channelRow?.MaxFrequencyPerHour;
            // 写入路径已校验为正数；这里再挡一次，因为 0 或负数会把上限读成「一条都不许发」。
            if (cap is > 0)
                caps[group.Key] = cap.Value;
        }

        return caps;
    }

    /// <inheritdoc />
    /// <remarks>
    /// ★ 判定本身在 <see cref="QuietHoursWindow"/> 里，与批量那条<b>共用同一份</b>：
    /// 这个模块的规则曾经就是靠「同一条判定抄两遍」漂开的（界面显示的与实际发送的不一致）。
    /// </remarks>
    public async Task<bool> IsInQuietHoursAsync(Guid userId, string channel, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(channel);

        var windows = await LoadQuietHoursAsync([userId], channel, cancellationToken);

        return windows.TryGetValue(userId, out var window) && window.Contains(DateTime.UtcNow);
    }

    /// <inheritdoc />
    /// <remarks>
    /// ★ 带时区的行按「包含现在的那一次、或紧随其后的下一次」换算成 UTC 时刻，
    /// 所以只认 UTC 的老调用方在当天拿到的仍是正确答案；跨夏令时切换的精确判定走
    /// <see cref="GetQuietHoursWindowsAsync"/>。没有时区的行原样返回。
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, (TimeOnly Start, TimeOnly End)>> GetQuietHoursAsync(
        IEnumerable<Guid> userIds, NotificationType channel, CancellationToken cancellationToken = default)
    {
        Check.NotNull(userIds);

        var windows = await LoadQuietHoursAsync(userIds, channel.ToString(), cancellationToken);
        var utcNow = DateTime.UtcNow;

        return windows.ToDictionary(kv => kv.Key, kv => kv.Value.ToUtcTimesOfDay(utcNow));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, QuietHoursWindow>> GetQuietHoursWindowsAsync(
        IEnumerable<Guid> userIds, NotificationType channel, CancellationToken cancellationToken = default)
    {
        Check.NotNull(userIds);

        return await LoadQuietHoursAsync(userIds, channel.ToString(), cancellationToken);
    }

    /// <summary>
    /// 取这一批人在该渠道上的静默时段。<b>两个公开方法唯一的取数处。</b>
    /// </summary>
    /// <remarks>
    /// ★ 只看 <c>Category == null</c> 的渠道级行：「现在别吵我」是一句关于时间的话，
    /// 按消息分类分别设一个免打扰时段表达不了任何用户真正想要的东西。
    /// ★ 渠道按名字大小写不敏感匹配，与 <see cref="FilterEnabledUsersAsync"/> /
    /// <see cref="GetFrequencyCapsAsync"/> 同款（偏好的渠道词汇比 <c>NotificationType</c> 宽）。
    /// ★ 两个时刻缺任一即视为没设：只有开始没有结束的窗口算不出结束时刻，而延后必须
    /// 写得出「什么时候再发」。
    /// ★ 写入时校验过的时区在这台主机上查不到（时区数据与写入那台不同），按 UTC 解释并告警：
    /// 那是部署问题，跳过这个人的静默时段等于在他设的免打扰里照发，比偏几个小时更糟。
    /// </remarks>
    private async Task<Dictionary<Guid, QuietHoursWindow>> LoadQuietHoursAsync(
        IEnumerable<Guid> userIds, string channel, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var channelName = channel.ToLower();

        var rows = await _repository
            .AsQueryable()
            .AsNoTracking()
            .Where(p => ids.Contains(p.UserId)
                        && p.Channel.ToLower() == channelName
                        && p.Category == null
                        && p.QuietHoursStart != null
                        && p.QuietHoursEnd != null)
            .Select(p => new { p.UserId, p.QuietHoursStart, p.QuietHoursEnd, p.QuietHoursTimeZone })
            .ToListAsync(cancellationToken);

        var windows = new Dictionary<Guid, QuietHoursWindow>();
        // 按时区聚合后各记一条：主机缺时区数据时一批里每个人都查不到，逐人记会把日志淹掉。
        var unknownZones = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var zone = IanaTimeZone.Resolve(row.QuietHoursTimeZone);
            if (zone == null)
            {
                unknownZones[row.QuietHoursTimeZone!] = unknownZones.GetValueOrDefault(row.QuietHoursTimeZone!) + 1;
                zone = TimeZoneInfo.Utc;
            }

            windows[row.UserId] = new QuietHoursWindow(row.QuietHoursStart!.Value, row.QuietHoursEnd!.Value, zone);
        }

        foreach (var (timeZone, userCount) in unknownZones)
        {
            LogWarning(
                "Time zone {TimeZone} on the {Channel} quiet hours of {UserCount} user(s) is not known on this host; evaluating them as UTC. Install the host's time zone data (tzdata / ICU) to fix this",
                timeZone, channel, userCount);
        }

        return windows;
    }
}
