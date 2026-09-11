namespace Tnzi.Identity.Options;

/// <summary>
/// 会话存储类型
/// </summary>
public enum SessionStorageType
{
    /// <summary>
    /// 数据库存储（默认，适合简单项目）
    /// </summary>
    Database,

    /// <summary>
    /// Redis分布式存储（适合高并发/分布式场景）
    /// </summary>
    Redis
}

/// <summary>
/// 来源地址在会话存续期间发生变化时的处理方式。
/// </summary>
/// <remarks>
/// 默认<b>不是</b>撤销。OWASP 的会话管理指引在推荐比对来源地址的同时明确警告了它的误报率：
/// 移动网络切换基站、Wi-Fi 与蜂窝之间来回、企业出口轮换、VPN，都会让一条完全正常的会话
/// 换地址。按地址硬拦会把这些用户反复踢下线，而用户学会的第一件事就是绕过它。
/// </remarks>
public enum SessionIpChangeBehavior
{
    /// <summary>完全忽略地址变化。</summary>
    Ignore = 0,

    /// <summary>
    /// 默认：记录下来 —— 更新会话上的地址并发布 <see cref="Events.SessionIpChangedEvent"/>，
    /// 但放行本次请求。消费应用可以订阅它去做通知、风控或人工复核。
    /// </summary>
    Track = 1,

    /// <summary>
    /// 撤销会话并要求重新登录。只适合来源地址确定稳定的部署（内网、固定出口）。
    /// </summary>
    Revoke = 2,
}

/// <summary>
/// 会话配置选项
/// 配置路径：Identity:Session
/// </summary>
public class SessionOptions
{
    /// <summary>
    /// 会话存储类型
    /// 默认：Database（数据库存储）
    /// 可选：Redis（分布式存储，需要配置 Redis 模块）
    /// </summary>
    public SessionStorageType StorageType { get; set; } = SessionStorageType.Database;

    /// <summary>
    /// 会话过期时间（分钟）
    /// 默认：60分钟
    /// 设置为0表示不过期（由 AccountSecurity.SessionTimeoutMinutes 控制）
    /// </summary>
    public int ExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// 是否启用滑动过期
    /// 默认：true（每次活动更新过期时间）
    /// </summary>
    public bool SlidingExpiration { get; set; } = true;

    /// <summary>
    /// Redis会话键前缀
    /// 默认：Tnzi:Session
    /// 仅当 StorageType 为 Redis 时生效
    /// </summary>
    public string RedisKeyPrefix { get; set; } = "Tnzi:Session";

    /// <summary>
    /// 是否在数据库中保留会话记录（用于审计）
    /// 默认：false
    /// 仅当 StorageType 为 Redis 时生效
    /// 启用后，会话信息同时写入 Redis 和数据库
    /// </summary>
    public bool KeepDatabaseAuditLog { get; set; } = false;

    /// <summary>
    /// 是否强制会话校验（多设备登录/单设备/限并发的真正生效开关）。
    /// 默认：true。启用后：登录签发的 access token 携带 session_id，JWT Bearer 的
    /// OnTokenValidated 每请求校验会话有效性（撤销/过期即 401），刷新令牌也校验会话。
    /// 设为 false 则退回旧行为（令牌仍带 session_id 但不校验），仅作应急逃生开关。
    /// </summary>
    public bool EnforceSessionValidation { get; set; } = true;

    /// <summary>
    /// 会话有效性校验的缓存秒数（仅数据库存储模式）。默认：30。
    /// OnTokenValidated 每请求校验会话，缓存避免每请求命中数据库；撤销时主动失效缓存，
    /// 故本地实例即时生效，跨实例（多节点共享数据库）最多滞后本值秒数。0 表示不缓存（每请求查库）。
    /// Redis 存储模式直接读缓存，不受此值影响。
    /// </summary>
    public int ValidationCacheSeconds { get; set; } = 30;

    /// <summary>
    /// 会话维护后台任务运行间隔（分钟）。默认：60。清理过期令牌 + 撤销长期失活会话，
    /// 避免幽灵会话累积影响并发计数。小于 5 时按 5 处理；0 表示禁用后台维护。
    /// </summary>
    public int MaintenanceIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// 会话绝对生命周期上限（小时）。默认 720（30 天）；0 表示不设上限（旧行为）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 会话的 <see cref="Entities.UserSession.ExpiresAt"/> 是滑动的：每次刷新令牌都会
    /// 把它往后推一个刷新令牌周期。只有滑动窗口时，<b>一条会话可以被无限续下去</b> ——
    /// 攻击者拿到一次令牌，只要按期刷新就永久有效。本项是那条滑动窗口的天花板
    /// （OWASP ASVS 7.3.2），到点必须重新认证，与用户活不活跃无关。
    /// </para>
    /// <para>
    /// 默认取 30 天而不是更短，是为了让升级本身不至于把现有用户成批登出；
    /// 对安全要求更高的部署，调到 8-24 小时是常见取值。
    /// </para>
    /// </remarks>
    public int AbsoluteLifetimeHours { get; set; } = 720;

    /// <summary>
    /// 是否把会话绑定到建立时的 User-Agent。默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 令牌是无记名凭证：谁拿着谁就是你。信息窃取类恶意软件把令牌从浏览器里取走之后，
    /// 攻击者在自己的机器上直接复用，服务端此前<b>没有任何一处</b>会察觉换了台机器 ——
    /// 会话行里的 <c>UserAgent</c> 从建立那天起就没有被比对过。
    /// 开启后，UA 对不上的请求当场 401 并撤销会话（OWASP <i>Cookie Theft Mitigation</i>
    /// 的建议动作：尽快发现被盗用，作废会话并要求重新认证）。
    /// </para>
    /// <para>
    /// ★ 比对的是<b>抹掉版本号之后</b>的 UA（见 <c>SessionBinding.Fingerprint</c>）。
    /// 直接比原串会被浏览器自动更新打败：Chrome 每四周升一个大版本，UA 跟着变，
    /// 于是一条 7 天的会话有可观的概率在用户什么都没做的情况下被判成「被盗」。
    /// 抹掉数字之后，版本升级不再触发，而换浏览器、换操作系统、换设备仍然触发。
    /// </para>
    /// <para>
    /// 挡不住刻意复刻 UA 的攻击者 —— 那需要 DBSC 那种设备绑定，而设备绑定只对
    /// cookie 会话生效（见 <c>Identity:TokenDelivery</c>）。这一条的定位是低成本、
    /// 高命中的第一道；它不是终点。
    /// </para>
    /// </remarks>
    public bool BindToUserAgent { get; set; } = true;

    /// <summary>
    /// 会话存续期间来源地址变化时的处理方式。默认 <see cref="SessionIpChangeBehavior.Track"/>（记录不拦）。
    /// </summary>
    public SessionIpChangeBehavior IpChangeBehavior { get; set; } = SessionIpChangeBehavior.Track;
}

