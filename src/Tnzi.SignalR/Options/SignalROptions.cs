namespace Tnzi.SignalR.Options;

/// <summary>
/// SignalR 模块配置选项
/// 配置路径：SignalR
/// </summary>
public class SignalROptions
{
    /// <summary>
    /// Hub 配置选项
    /// </summary>
    public HubOptions Hub { get; set; } = new();

    /// <summary>
    /// Backplane 配置选项
    /// </summary>
    public BackplaneOptions? Backplane { get; set; }

    /// <summary>
    /// MessagePack 协议配置
    /// </summary>
    public MessagePackOptions MessagePack { get; set; } = new();

    /// <summary>
    /// 速率限制配置
    /// </summary>
    public RateLimitOptions RateLimit { get; set; } = new();

    /// <summary>
    /// 是否启用详细日志
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;
}

/// <summary>
/// Hub 配置选项
/// </summary>
public class HubOptions
{
    /// <summary>
    /// 是否启用详细错误信息
    /// </summary>
    public bool EnableDetailedErrors { get; set; } = false;

    /// <summary>
    /// 心跳间隔
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 客户端超时时间
    /// </summary>
    public TimeSpan ClientTimeoutInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 握手超时时间
    /// </summary>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 最大接收消息大小 (字节)
    /// </summary>
    public int? MaximumReceiveMessageSize { get; set; } = 32 * 1024;
}

/// <summary>
/// Backplane 配置选项
/// </summary>
public class BackplaneOptions
{
    /// <summary>
    /// Backplane 类型
    /// </summary>
    public BackplaneType Type { get; set; } = BackplaneType.None;

    /// <summary>
    /// Redis 连接字符串 (当 Type = Redis 时必填)
    /// 如不指定，将尝试复用 RedisCachingModule 的连接
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// 频道前缀
    /// </summary>
    public string ChannelPrefix { get; set; } = "Tnzi.SignalR";
}

/// <summary>
/// Backplane 类型
/// </summary>
public enum BackplaneType
{
    /// <summary>
    /// 不使用 Backplane (单服务器模式)
    /// </summary>
    None,

    /// <summary>
    /// Redis Backplane
    /// </summary>
    Redis
}

/// <summary>
/// MessagePack 协议配置
/// </summary>
public class MessagePackOptions
{
    /// <summary>
    /// 是否启用 MessagePack 协议
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 是否启用压缩
    /// </summary>
    public bool EnableCompression { get; set; } = true;
}

/// <summary>
/// 速率限制配置
/// </summary>
public class RateLimitOptions
{
    /// <summary>
    /// 是否启用速率限制
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 每用户最大连接数
    /// </summary>
    public int MaxConnectionsPerUser { get; set; } = 5;

    /// <summary>
    /// 每分钟最大消息数
    /// </summary>
    public int MaxMessagesPerMinute { get; set; } = 60;

    /// <summary>
    /// 违规封禁时长
    /// </summary>
    public TimeSpan BanDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 匿名（拿不到用户 id 的）连接如何处理，默认 <see cref="AnonymousHubRateLimitPolicy.Limit"/>
    /// </summary>
    public AnonymousHubRateLimitPolicy AnonymousPolicy { get; set; } = AnonymousHubRateLimitPolicy.Limit;

    /// <summary>
    /// 每个匿名分区（默认按客户端 IP）的最大连接数。
    ///
    /// 默认值比 <see cref="MaxConnectionsPerUser"/> 宽得多：一个 IP 背后可能是整个
    /// NAT 网关或校园出口，按单用户的额度卡会误伤一整批正常客户端。
    /// </summary>
    public int MaxConnectionsPerAnonymousPartition { get; set; } = 20;

    /// <summary>
    /// 匿名连接计数的存活时间，默认 2 小时。
    ///
    /// 计数靠连接建立时 +1、断开时 -1 维护。进程非正常退出时那些 -1 永远不会发生，
    /// 没有 TTL 的话该分区的计数会永久偏高，最终把一个正常的 IP 永久锁在门外。
    /// TTL 让它自愈，代价是超过这个时长的长连接不再被计入 —— 那是**放宽**方向的
    /// 失效，比永久误封可接受。
    /// </summary>
    public TimeSpan AnonymousConnectionCountTtl { get; set; } = TimeSpan.FromHours(2);
}

/// <summary>
/// 匿名 Hub 连接的限流处置方式。
///
/// ★ 存在的理由：限流的分区键此前只有用户 id，取不到就整条放行 —— 匿名 Hub
/// （<c>[AllowAnonymous]</c>、或认证失败但 Hub 本身不要求认证）既不计连接数、
/// 不计消息速率，也不查封禁，而且没有任何告警或配置项能看出这一点。
/// </summary>
public enum AnonymousHubRateLimitPolicy
{
    /// <summary>
    /// 按分区键（客户端 IP，取不到则退回连接 id）参与限流。默认值。
    /// </summary>
    Limit = 0,

    /// <summary>
    /// 直接拒绝匿名连接。适合所有 Hub 都要求认证的部署。
    /// </summary>
    Reject = 1,

    /// <summary>
    /// 匿名连接完全不受限流约束（本修复之前的行为）。
    /// 只有在确知匿名 Hub 由别的机制兜住时才用。
    /// </summary>
    Allow = 2,
}
