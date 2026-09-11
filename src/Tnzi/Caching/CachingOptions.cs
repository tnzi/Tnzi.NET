namespace Tnzi.Caching;

/// <summary>
/// 缓存模块配置选项
/// 配置路径：Caching
/// </summary>
public class CachingOptions
{
    /// <summary>
    /// 获取或设置 缓存类型（Memory, Redis）
    /// </summary>
    public string Type { get; set; } = "Memory";

    /// <summary>
    /// 获取或设置 默认过期时间（分钟）
    /// </summary>
    public int DefaultExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// 获取或设置 Redis连接字符串（当Type为Redis时使用）
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// 获取或设置 Redis实例名称（用于键前缀）
    /// </summary>
    public string? RedisInstanceName { get; set; }

    /// <summary>
    /// 获取或设置 是否启用缓存键生成器
    /// </summary>
    public bool EnableKeyGenerator { get; set; } = true;


    /// <summary>
    /// 获取或设置 内存缓存最大条目数（仅 Type=Memory 时生效）
    /// 为 0 或 null 表示不限制。超出限制后按 LRU 策略驱逐条目。
    /// </summary>
    public long? MemorySizeLimit { get; set; }
}

