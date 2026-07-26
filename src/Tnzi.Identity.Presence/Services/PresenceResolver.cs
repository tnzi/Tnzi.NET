namespace Tnzi.Identity.Presence.Services;

/// <summary>
/// 有效在线状态解析纯函数。<see cref="PresenceService"/> 与消费方（如 Chat 管理端在线总览）
/// 共用此单一实现，避免解析逻辑漂移。
/// </summary>
public static class PresenceResolver
{
    /// <param name="intent">用户手动意图状态。</param>
    /// <param name="hasConnection">是否有活跃实时连接（仅在 <paramref name="connectionTracking"/> 为真时有意义）。</param>
    /// <param name="connectionTracking">是否加载了连接追踪（SignalR）。</param>
    /// <param name="allowInvisible">部署是否允许隐身。</param>
    /// <param name="isAutoAway">客户端是否上报了空闲。</param>
    /// <param name="autoAwayEnabled">部署是否启用 auto-away。</param>
    public static UserPresenceStatus Resolve(
        UserPresenceStatus intent, bool hasConnection, bool connectionTracking,
        bool allowInvisible, bool isAutoAway, bool autoAwayEnabled)
    {
        // 部署禁用隐身时，历史隐身意图不再对外隐藏——按在线意图解析（仍受连接状态约束）。
        if (!allowInvisible && intent == UserPresenceStatus.Invisible)
            intent = UserPresenceStatus.Online;

        if (intent == UserPresenceStatus.Invisible || intent == UserPresenceStatus.Offline)
            return UserPresenceStatus.Offline;

        // 无连接追踪（未加载 SignalR）：按选择的状态原样返回（仍应用 auto-away 标记）。
        if (!connectionTracking)
            return ApplyAutoAway(intent, isAutoAway, autoAwayEnabled);

        if (!hasConnection)
            return UserPresenceStatus.Offline;

        return ApplyAutoAway(intent, isAutoAway, autoAwayEnabled);
    }

    private static UserPresenceStatus ApplyAutoAway(UserPresenceStatus intent, bool isAutoAway, bool autoAwayEnabled)
        => (autoAwayEnabled && isAutoAway && intent == UserPresenceStatus.Online)
            ? UserPresenceStatus.Away
            : intent;
}
