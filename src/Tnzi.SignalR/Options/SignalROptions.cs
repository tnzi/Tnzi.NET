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
    /// Redis 连接字符串（当 Type = Redis 时必填）。只读这一项，<b>不</b>回退到 Redis 缓存模块的连接配置：
    /// 未配置时启动即抛 <c>ConfigurationException</c>。支持 <c>${VAR}</c> 环境变量占位符。
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// 频道前缀（默认 <c>Tnzi.SignalR</c>）。
    /// </summary>
    /// <remarks>
    /// ★ 共用一个 Redis 服务器的每个应用、每个环境都要配一个自己的前缀。backplane 的频道以 Hub 的完整类型名命名，
    /// 框架自带的 Hub（聊天、设置推送、在线状态）在每个消费应用里类型名都相同；Redis 的 Pub/Sub 又不分库，
    /// 「一个环境一个库号」隔离不了它。前缀相同的两个部署共享频道：一方的 <c>Clients.All</c> 推送到达另一方的客户端，
    /// 用户 id 相同时（例如预发库由生产库克隆）按用户、按租户的推送也会跨环境送达。
    /// </remarks>
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
    /// 每用户最大连接数（默认 5）。
    /// </summary>
    /// <remarks>
    /// 按用户跨<b>所有</b> Hub 计数、只对本实例成立（连接登记在进程内）。框架管理端每个浏览器标签页通常占两条
    /// （设置推送 <c>/hubs/settings</c>，启用内置聊天时再加 <c>/hubs/chat</c>），默认值下第三个标签页的第二条会被拒绝；
    /// 允许多标签页使用的部署按「标签页数 × 每页连接数」设置。检查在连接登记之前读取当前计数，同一用户并发握手时可能略超上限。
    /// </remarks>
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
    /// TTL 让它自愈。它是<b>固定窗口</b>：从该分区计数键创建（第一条连接）起算，之后的连接不续期，
    /// 到期时计数整体归零，窗口内建立、此刻仍在线的连接一并不再计入，此后它们断开时的 -1 还可能把新窗口减成负数而被清零。
    /// 失效方向是<b>放宽</b>（一个分区在一个窗口之后可以再开满一轮），比永久误封可接受；需要更紧的约束时调短这个值。
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
