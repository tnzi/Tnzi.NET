
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
            // RemoveByPatternAsync 是 ICache 的必选成员：内存实现与 Redis 实现都有完整的通配符支持。
            // 此前这里只对一个 Redis 独有的标记接口委托，默认的内存缓存于是「记一条 Warning 然后什么都不删」，
            // 与线上 Redis 部署行为分叉且零异常。
            await _cache.RemoveByPatternAsync(pattern, cancellationToken);
            _logger?.LogDebug("Cache invalidated by pattern: {Pattern}", pattern);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to invalidate cache by pattern: {Pattern}", pattern);
        }
    }
}
