namespace Tnzi.Chat.Entities;

/// <summary>每用户在线状态（手动选择意图 + 最近在线时间）。每用户一行，UserId 唯一。</summary>
public class UserPresence : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>用户手动选择的状态意图（Online/Away/Busy/Invisible）；Offline 不作为手动值持久化。</summary>
    public UserPresenceStatus Status { get; set; } = UserPresenceStatus.Online;

    /// <summary>最近一次全部连接断开的时间。</summary>
    public DateTime? LastSeenAt { get; set; }

    public DateTime LastChangedAt { get; set; }
}
