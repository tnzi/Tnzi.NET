namespace Tnzi.Redis.Options;

/// <summary>
/// Redis 模块配置选项
/// 配置路径：Redis
/// </summary>
public class RedisOptions
{
    /// <summary>
    /// 连接字符串（可选，优先使用 Caching.RedisConnectionString）
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// 连接选项
    /// </summary>
    public ConnectionOptions Connection { get; set; } = new();
}

/// <summary>
/// Redis 连接选项
/// </summary>
public class ConnectionOptions
{
    /// <summary>
    /// 连接失败时是否中止（默认: false）
    /// </summary>
    public bool AbortOnConnectFail { get; set; } = false;

    /// <summary>
    /// 连接重试次数（默认: 3）
    /// </summary>
    public int ConnectRetry { get; set; } = 3;

    /// <summary>
    /// 连接超时（毫秒，默认: 5000）
    /// </summary>
    public int ConnectTimeout { get; set; } = 5000;

    /// <summary>
    /// 同步操作超时（毫秒，默认: 5000）
    /// </summary>
    public int SyncTimeout { get; set; } = 5000;
}
