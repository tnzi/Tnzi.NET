namespace Tnzi.Notification.Services;

/// <summary>
/// 通知偏好服务接口
/// </summary>
public interface INotificationPreferenceService
{
    /// <summary>
    /// Paged list of notification preferences across all users. Supports
    /// optional filters on user / channel / category / enabled state.
    /// Used by the admin NotificationSubscription page.
    /// </summary>
    Task<Result<IPagedList<NotificationPreferenceDto>>> GetPagedListAsync(NotificationPreferenceQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户的所有通知偏好
    /// </summary>
    Task<Result<List<NotificationPreferenceDto>>> GetUserPreferencesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置用户通知偏好（存在则更新，不存在则创建）
    /// </summary>
    Task<Result<NotificationPreferenceDto>> SetPreferenceAsync(Guid userId, SetNotificationPreferenceDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除通知偏好
    /// </summary>
    Task<Result> DeletePreferenceAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重置为默认偏好（删除用户的所有自定义偏好）
    /// </summary>
    Task<Result> ResetToDefaultAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查指定渠道是否对用户启用（内部方法）
    /// 无偏好记录时默认返回 true（启用）
    /// </summary>
    Task<bool> IsChannelEnabledAsync(Guid userId, string channel, string? category = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查用户当前是否在指定渠道的静默时段内（内部方法）
    /// 无偏好记录或未设置静默时段时返回 false
    /// </summary>
    Task<bool> IsInQuietHoursAsync(Guid userId, string channel, CancellationToken cancellationToken = default);
}
