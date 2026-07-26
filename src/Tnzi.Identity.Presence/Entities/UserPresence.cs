namespace Tnzi.Identity.Presence.Entities;

/// <summary>每用户在线状态（手动选择意图 + 客户端空闲标记 + 最近在线时间）。每用户一行，UserId 唯一。</summary>
public class UserPresence : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>用户手动选择的状态意图（Online/Away/Busy/Invisible）；Offline 不作为手动值持久化。</summary>
    public UserPresenceStatus Status { get; set; } = UserPresenceStatus.Online;

    /// <summary>
    /// 客户端上报的空闲标记：当意图为 Online 且此标记为 true 时，有效状态解析为 Away
    /// （受 <c>PresenceOptions.AutoAwayEnabled</c> 门控）。客户端越过本地空闲阈值时置 true，
    /// 用户恢复活动时置 false。手动切换状态也会清除此标记。
    /// </summary>
    public bool IsAutoAway { get; set; }

    /// <summary>最近一次客户端上报活动的时间（用户交互心跳/从空闲恢复）。</summary>
    public DateTime? LastActivityAt { get; set; }

    /// <summary>最近一次全部连接断开的时间。</summary>
    public DateTime? LastSeenAt { get; set; }

    public DateTime LastChangedAt { get; set; }
}
