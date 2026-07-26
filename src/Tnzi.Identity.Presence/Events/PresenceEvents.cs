namespace Tnzi.Identity.Presence.Events;

/// <summary>
/// 某用户有效在线状态发生变化。由 <c>PresenceService</c> 在状态/活动/连接变化时发布（本地事件），
/// 订阅者各自扇出实时推送：通用 <c>PresenceRealtimePushHandler</c>（/hubs/presence，全量广播），
/// 以及加载了 Chat 时的 <c>ChatPresenceRelayHandler</c>（按会话联系人推 Chat.PresenceChanged）。
/// </summary>
public class UserPresenceChangedEvent : EventBase
{
    public Guid UserId { get; set; }
    public UserPresenceStatus Status { get; set; }
    public DateTime? LastSeenAt { get; set; }
}
