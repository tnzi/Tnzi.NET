
namespace Tnzi.Caching;

/// <summary>
/// 缓存失效服务接口
/// 统一管理缓存失效，支持精确失效和模式匹配失效
/// </summary>
public interface ICacheInvalidationService
{
    /// <summary>
    /// 失效指定的缓存键
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task InvalidateAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量失效缓存键
    /// </summary>
    /// <param name="keys">缓存键集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task InvalidateManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按模式失效缓存（支持通配符）
    /// </summary>
    /// <param name="pattern">缓存键模式（支持 * 通配符）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default);
}

/// <summary>
/// 缓存失效服务实现
/// </summary>
public class CacheInvalidationService : ICacheInvalidationService
{
    private readonly ICache _cache;
    private readonly ILogger<CacheInvalidationService>? _logger;

    /// <summary>
    /// 初始化缓存失效服务
    /// </summary>
    /// <param name="cache">缓存服务</param>
    /// <param name="logger">日志记录器（可选）</param>
    public CacheInvalidationService(ICache cache, ILogger<CacheInvalidationService>? logger = null)
    {
        _cache = Check.NotNull(cache);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        try
        {
            await _cache.RemoveAsync(key, cancellationToken);
            _logger?.LogDebug("Cache invalidated: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to invalidate cache key: {Key}", key);
        }
    }

    /// <inheritdoc />
    public async Task InvalidateManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys == null)
            return;

        var keyList = keys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
        if (keyList.Count == 0)
            return;

        try
        {
            await _cache.RemoveManyAsync(keyList, cancellationToken);
            _logger?.LogDebug("Cache invalidated: {Count} keys", keyList.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to invalidate {Count} cache keys", keyList.Count);
        }
    }

    /// <inheritdoc />
    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return;

        try
        {
            // 如果缓存服务支持模式匹配（如Redis），直接使用
            if (_cache is IPatternCache patternCache)
            {
                await patternCache.RemoveByPatternAsync(pattern, cancellationToken);
                _logger?.LogDebug("Cache invalidated by pattern: {Pattern}", pattern);
                return;
            }

            // 否则，对于内存缓存等不支持模式匹配的实现，记录警告
            // 在实际应用中，可能需要维护一个缓存键索引来实现模式匹配
            _logger?.LogWarning(
                "Pattern-based cache invalidation is not supported by the current cache implementation. Pattern: {Pattern}",
                pattern);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to invalidate cache by pattern: {Pattern}", pattern);
        }
    }
}

/// <summary>
/// 支持模式匹配的缓存接口
/// </summary>
public interface IPatternCache : ICache
{
    /// <summary>
    /// 按模式移除缓存（支持通配符）
    /// </summary>
    /// <param name="pattern">缓存键模式（支持 * 通配符）</param>
    /// <param name="cancellationToken">取消令牌</param>
    new Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);
}