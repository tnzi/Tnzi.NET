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
    /// <remarks>
    /// ⚠️ <b>群发路径不要用它</b>：逐个问在一次千人群发上就是两千次往返。
    /// 用 <see cref="FilterEnabledUsersAsync"/>。
    /// </remarks>
    Task<bool> IsChannelEnabledAsync(Guid userId, string channel, string? category = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 一次筛出「该渠道仍启用」的用户（发送路径用）。
    /// </summary>
    /// <param name="userIds">本批收件人里带 <c>UserId</c> 的那些（重复无妨，内部去重）。</param>
    /// <param name="channel">正在发送的渠道。</param>
    /// <param name="category">消息分类；空表示只看渠道级偏好。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 语义与 <see cref="IsChannelEnabledAsync"/> <b>逐字一致</b>（分类级偏好优先于渠道级，
    /// 两者都没有则默认启用），只是一次查完一批 —— 两处若各写一遍判定，
    /// 「界面上显示的开关状态」与「实际发不发」迟早对不上。
    /// <para>
    /// 渠道按名字大小写不敏感匹配：偏好的渠道词汇刻意比 <see cref="NotificationType"/> 宽
    /// （<c>InApp</c> / <c>Webhook</c> 对应尚未实现的渠道），所以这里匹配的是
    /// 「当前正在发的这个渠道」，而不是校验偏好行的合法性。
    /// </para>
    /// </remarks>
    Task<IReadOnlyCollection<Guid>> FilterEnabledUsersAsync(
        IEnumerable<Guid> userIds, NotificationType channel, string? category = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查用户当前是否在指定渠道的静默时段内（内部方法）
    /// 无偏好记录或未设置静默时段时返回 false
    /// </summary>
    Task<bool> IsInQuietHoursAsync(Guid userId, string channel, CancellationToken cancellationToken = default);
}
