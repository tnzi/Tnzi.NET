namespace Tnzi.Identity.Events;

/// <summary>
/// 检测到刷新令牌重放。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>发出这条事件本身就是这个特性一半的价值。</b>撤销会话解决的是「这次别让他进来」，
/// 而事件解决的是「让人知道发生过这件事」—— 令牌被盗的典型形态是攻击者安静地用着，
/// 没有任何一次失败登录、没有任何一次异地登录，风控看不到、用户也看不到。
/// 一次重放检测是这条链路上<b>唯一</b>会自己冒出来的信号，不发出去就等于没检测。
/// </para>
/// <para>
/// 框架自身不订阅它（不替部署方决定要发邮件还是要拉黑）。消费应用应当至少做一件事：
/// 通知本人。
/// </para>
/// </remarks>
public class RefreshTokenReuseDetectedEvent : EventBase
{
    /// <summary>令牌所属用户</summary>
    public Guid UserId { get; set; }

    /// <summary>被判定为重放的令牌所绑定的会话；<see cref="Guid.Empty"/> 表示遗留的未绑定令牌</summary>
    public Guid SessionId { get; set; }

    /// <summary>发起本次重放请求的来源地址（可能为 null）</summary>
    public string? IpAddress { get; set; }

    /// <summary>发起本次重放请求的 User-Agent（可能为 null）</summary>
    public string? UserAgent { get; set; }

    /// <summary>上一次轮换发生的时刻 —— 与本次请求的间隔就是「晚了多久」</summary>
    public DateTime? RotatedAt { get; set; }

    /// <summary>本次检测的时刻</summary>
    public DateTime DetectedTime { get; set; }

    /// <summary>因本次检测被撤销的会话数</summary>
    public int RevokedSessionCount { get; set; }
}

/// <summary>
/// 一条会话的来源地址在存续期间发生了变化（<see cref="Options.SessionIpChangeBehavior.Track"/> 时发布）。
/// </summary>
/// <remarks>
/// 单纯的地址变化<b>不是</b>攻击信号：换 Wi-Fi、切蜂窝、VPN、企业出口轮换都会触发。
/// 发布它是为了让有能力做判断的一侧（有地理库、有设备台账、有用户画像的消费应用）
/// 拿去做判断，而不是让框架在信息最少的位置替所有人下结论。
/// 每次地址变化只发一条：发布后会话上记录的地址随即被更新。
/// </remarks>
public class SessionIpChangedEvent : EventBase
{
    /// <summary>会话所属用户</summary>
    public Guid UserId { get; set; }

    /// <summary>会话ID</summary>
    public Guid SessionId { get; set; }

    /// <summary>此前记录的地址</summary>
    public string? PreviousIpAddress { get; set; }

    /// <summary>本次请求的地址</summary>
    public string? CurrentIpAddress { get; set; }

    /// <summary>变化被观察到的时刻</summary>
    public DateTime ChangedTime { get; set; }
}

/// <summary>
/// 一次会话撤销（含连带删除的刷新令牌）。由 <see cref="Services.ISessionRevocationService"/> 发布。
/// </summary>
/// <remarks>
/// 与 <c>UserLoggedOutEvent</c> 的区别是<b>视角</b>：那条是「用户登出了」，
/// 这条是「有人被踢下线了」，包括用户自己并不知情的情况（管理员停用、密码被重置、
/// 检测到令牌重放）。审计与安全通知要看的是这一条。
/// </remarks>
public class SessionsRevokedEvent : EventBase
{
    /// <summary>被撤销会话所属用户</summary>
    public Guid UserId { get; set; }

    /// <summary>被撤销的会话数</summary>
    public int SessionCount { get; set; }

    /// <summary>被连带删除的刷新令牌数</summary>
    public int TokenCount { get; set; }

    /// <summary>撤销原因</summary>
    public SessionRevocationReason Reason { get; set; }

    /// <summary>被保留的会话（一般是发起本次操作的当前会话）</summary>
    public Guid? ExcludedSessionId { get; set; }

    /// <summary>撤销时刻</summary>
    public DateTime RevokedTime { get; set; }
}
