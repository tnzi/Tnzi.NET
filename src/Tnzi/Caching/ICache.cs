namespace Tnzi.Caching;

/// <summary>
/// 缓存服务接口
/// 提供统一的缓存操作抽象，支持内存缓存和分布式缓存实现
/// </summary>
[StableApi(Since = "0.1.0")]
public interface ICache
{
    /// <summary>
    /// 获取缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>缓存值，不存在时返回 null</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 设置缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="expiration">过期时间，null 使用默认过期时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 移除缓存项
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 按模式移除缓存项
    /// 支持通配符：* 匹配任意字符，? 匹配单个字符
    /// </summary>
    /// <param name="pattern">匹配模式，如 "user:*" 或 "cache:user:?"</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 检查缓存键是否存在
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>存在返回 true，否则返回 false</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 递增缓存值
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="increment">递增量，默认为 1</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>递增后的值</returns>
    Task<long> IncrementAsync(string key, long increment = 1, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 递增缓存值并设置过期时间
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="increment">递增量</param>
    /// <param name="expiration">过期时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>递增后的值</returns>
    Task<long> IncrementAsync(string key, long increment, TimeSpan expiration, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 递减缓存值
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="decrement">递减量，默认为 1</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>递减后的值</returns>
    Task<long> DecrementAsync(string key, long decrement = 1, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 尝试设置缓存值（仅在键不存在时设置）
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="expiration">过期时间，null 使用默认过期时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果设置成功返回 true，如果键已存在返回 false</returns>
    Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 按前缀删除缓存（比 RemoveByPatternAsync 更高效）
    /// 使用前缀索引，时间复杂度接近 O(n)，n 为匹配的键数
    /// </summary>
    /// <param name="prefix">缓存键前缀，如 "user:" 将匹配所有以 "user:" 开头的键</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 设置带标签的缓存
    /// 标签可用于批量失效相关缓存项
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="tags">标签列表</param>
    /// <param name="expiration">过期时间，null 使用默认过期时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SetWithTagsAsync<T>(string key, T value, IEnumerable<string> tags, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 按标签删除缓存
    /// 删除所有带有指定标签的缓存项
    /// </summary>
    /// <param name="tag">标签名</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量获取缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="keys">缓存键集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>键值对字典，不存在的键不包含在结果中</returns>
    Task<Dictionary<string, T?>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量设置缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="items">键值对集合</param>
    /// <param name="expiration">过期时间，null 使用默认过期时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SetManyAsync<T>(
        IEnumerable<KeyValuePair<string, T>> items,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量移除缓存项
    /// </summary>
    /// <param name="keys">缓存键集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RemoveManyAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取缓存值，如果不存在则使用工厂方法创建并缓存。
    /// 默认实现使用 per-key 锁防止缓存击穿（并发请求只执行一次 factory）。
    /// 具体实现可重写以提供更高效的原子操作。
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="factory">值不存在时的创建工厂</param>
    /// <param name="expiration">过期时间，null 使用默认过期时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>缓存值或工厂创建的值</returns>
    async Task<T?> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var value = await GetAsync<T>(key, cancellationToken);
        if (value is not null)
            return value;

        // Per-key 锁防止缓存击穿
        await using (await CacheStampedeGuard.Instance.LockAsync(key, cancellationToken))
        {
            // Double-check: 另一个线程可能已经填充了缓存
            value = await GetAsync<T>(key, cancellationToken);
            if (value is not null)
                return value;

            value = await factory();
            if (value is not null)
                await SetAsync(key, value, expiration, cancellationToken);
        }

        return value;
    }
}

/// <summary>
/// 缓存击穿防护的共享锁实例
/// </summary>
internal static class CacheStampedeGuard
{
    internal static readonly KeyedAsyncLock Instance = new();
}


