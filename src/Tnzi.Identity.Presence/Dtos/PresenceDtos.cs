namespace Tnzi.Identity.Presence.Dtos;

/// <summary>某用户的有效在线状态（对外解析后的结果）。</summary>
public class UserPresenceDto
{
    public Guid UserId { get; set; }
    public UserPresenceStatus Status { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

/// <summary>设置本人手动状态意图。</summary>
public class SetPresenceDto
{
    public UserPresenceStatus Status { get; set; }
}

/// <summary>auto-away 活动上报。</summary>
public class PresenceActivityDto
{
    /// <summary>
    /// <c>true</c> = 用户有活动（从空闲恢复/心跳）；<c>false</c> = 客户端越过本地空闲阈值。
    /// 请求体缺省时按 <c>true</c> 处理。
    /// </summary>
    public bool Active { get; set; } = true;
}

/// <summary>供前端消费的 presence 客户端配置。</summary>
public class PresenceClientConfigDto
{
    public bool EnablePresence { get; set; }
    public bool AllowInvisible { get; set; }
    public bool AutoAwayEnabled { get; set; }
    public int AutoAwayMinutes { get; set; }
}
