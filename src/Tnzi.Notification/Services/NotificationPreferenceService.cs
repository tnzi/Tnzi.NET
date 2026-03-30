namespace Tnzi.Notification.Services;

/// <summary>
/// 通知偏好服务实现
/// </summary>
public class NotificationPreferenceService : ApplicationService, INotificationPreferenceService
{
    private readonly IRepository<NotificationPreference, Guid> _repository;

    public NotificationPreferenceService(
        IRepository<NotificationPreference, Guid> repository,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
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
        var preference = new NotificationPreference
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

        LogInformation("Created notification preference {PreferenceId} for user {UserId}, channel: {Channel}",
            preference.Id, userId, input.Channel);

        return Ok(preference.MapTo<NotificationPreferenceDto>());
    }

    public async Task<Result> DeletePreferenceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var preference = await _repository.GetAsync(id, cancellationToken);
        if (preference == null)
            return Fail("Notification preference not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        await _repository.DeleteAsync(preference, cancellationToken);

        LogInformation("Deleted notification preference {PreferenceId} for user {UserId}", id, preference.UserId);
        return Ok("Notification preference deleted");
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
