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
}
