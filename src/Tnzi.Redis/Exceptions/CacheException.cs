
namespace Tnzi.Redis.Exceptions;

/// <summary>
/// 缓存异常基类
/// </summary>
public class CacheException : InfrastructureException
{
    /// <summary>
    /// 缓存键
    /// </summary>
    public string? CacheKey { get; }
    
    /// <summary>
    /// 初始化一个<see cref="CacheException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="cacheKey">缓存键</param>
    /// <param name="isRetryable">是否可重试</param>
    /// <param name="innerException">内部异常</param>
    public CacheException(
        string message, 
        string? cacheKey = null,
        bool isRetryable = true,
        Exception? innerException = null)
        : base("Cache", message, isRetryable, innerException)
    {
        CacheKey = cacheKey;
        if (cacheKey != null)
        {
            this.WithData("CacheKey", cacheKey);
        }
    }
}

/// <summary>
/// Redis 连接异常
/// </summary>
public class RedisConnectionException : CacheException
{
    /// <summary>
    /// Redis 端点
    /// </summary>
    public string? Endpoint { get; }
    
    /// <summary>
    /// 初始化一个<see cref="RedisConnectionException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="endpoint">Redis 端点</param>
    /// <param name="innerException">内部异常</param>
    public RedisConnectionException(string message, string? endpoint = null, Exception? innerException = null)
        : base(message, null, true, innerException)
    {
        Endpoint = endpoint;
        if (endpoint != null)
        {
            this.WithData("Endpoint", endpoint);
        }
    }
}

/// <summary>
/// 缓存读取异常
/// </summary>
public class CacheReadException : CacheException
{
    /// <summary>
    /// 初始化一个<see cref="CacheReadException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="cacheKey">缓存键</param>
    /// <param name="innerException">内部异常</param>
    public CacheReadException(string message, string? cacheKey = null, Exception? innerException = null)
        : base(message, cacheKey, true, innerException)
    {
    }
}

/// <summary>
/// 缓存写入异常
/// </summary>
public class CacheWriteException : CacheException
{
    /// <summary>
    /// 初始化一个<see cref="CacheWriteException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="cacheKey">缓存键</param>
    /// <param name="innerException">内部异常</param>
    public CacheWriteException(string message, string? cacheKey = null, Exception? innerException = null)
        : base(message, cacheKey, true, innerException)
    {
    }
}