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

        // 验证频率限制
        if (input.MaxFrequencyPerHour.HasValue && input.MaxFrequencyPerHour.Value <= 0)
            return Fail<NotificationPreferenceDto>("Max frequency per hour must be a positive number", 400);

        // 查找已有偏好（按 UserId + Channel + Category 唯一）
        var existing = await _repository
            .AsQueryable()
            .Where(p => p.UserId == userId
                && p.Channel == input.Channel
                && p.Category == input.Category)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
        {
            // 更新已有记录
            existing.IsEnabled = input.IsEnabled;
            existing.QuietHoursStart = input.QuietHoursStart;
            existing.QuietHoursEnd = input.QuietHoursEnd;
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
            Category = input.Category,
            IsEnabled = input.IsEnabled,
            QuietHoursStart = input.QuietHoursStart,
            QuietHoursEnd = input.QuietHoursEnd,
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
    /// （<c>DELETE /notification-preferences/{id}</c>，只有 <c>[ApiAuthorize]</c>）与管理端
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

        // 先查分类级偏好
        if (category != null)
        {
            var categoryPreference = await _repository
                .AsQueryable()
                .AsNoTracking()
                .Where(p => p.UserId == userId && p.Channel == channel && p.Category == category)
                .FirstOrDefaultAsync(cancellationToken);

            if (categoryPreference != null)
                return categoryPreference.IsEnabled;
        }

        // 再查渠道级全局偏好
        var channelPreference = await _repository
            .AsQueryable()
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Channel == channel && p.Category == null)
            .FirstOrDefaultAsync(cancellationToken);

        // 无偏好记录时默认启用
        return channelPreference?.IsEnabled ?? true;
    }

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

    public async Task<bool> IsInQuietHoursAsync(Guid userId, string channel, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(channel);

        // 查渠道级全局偏好（静默时段不区分分类）
        var preference = await _repository
            .AsQueryable()
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Channel == channel && p.Category == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (preference?.QuietHoursStart == null || preference.QuietHoursEnd == null)
            return false;

        var now = TimeOnly.FromDateTime(DateTime.UtcNow);
        var start = preference.QuietHoursStart.Value;
        var end = preference.QuietHoursEnd.Value;

        // 处理跨午夜的情况 (e.g., 22:00 - 06:00)
        if (start <= end)
        {
            return now >= start && now <= end;
        }

        // 跨午夜：当前时间在 start 之后 或 在 end 之前
        return now >= start || now <= end;
    }
}
