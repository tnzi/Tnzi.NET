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
    /// 获取或设置 缓存过期策略类型（Fixed, Pattern, Sliding, Composite）
    /// </summary>
    public string ExpirationStrategy { get; set; } = "Fixed";

    /// <summary>
    /// 获取或设置 模式过期时间映射（当ExpirationStrategy为Pattern时使用）
    /// </summary>
    public Dictionary<string, int>? PatternExpirations { get; set; }
}

