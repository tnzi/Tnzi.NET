
namespace Tnzi.AspNetCore.Security;

/// <summary>
/// 限流服务实现
/// 支持多种限流算法：固定窗口、滑动窗口
/// 使用原子操作确保并发安全
/// </summary>
public class RateLimitService : IRateLimitService
{
    private readonly ICache _cache;
    private readonly ILogger<RateLimitService>? _logger;

    /// <summary>
    /// 初始化一个<see cref="RateLimitService"/>类型的新实例
    /// </summary>
    public RateLimitService(ICache cache, ILogger<RateLimitService>? logger = null)
    {
        _cache = Check.NotNull(cache);
        _logger = logger;
    }

    /// <summary>
    /// 递增并获取当前计数（原子操作）
    /// 用于限流检查，先递增后检查，避免竞态条件
    /// </summary>
    public async Task<long> IncrementAndGetAsync(string key, int windowSeconds, RateLimitAlgorithm algorithm = RateLimitAlgorithm.FixedWindow)
    {
        return algorithm switch
        {
            RateLimitAlgorithm.FixedWindow => await IncrementFixedWindowAsync(key, windowSeconds),
            RateLimitAlgorithm.SlidingWindow => await IncrementSlidingWindowAsync(key, windowSeconds),
            RateLimitAlgorithm.TokenBucket => await FallbackToFixedWindowAsync(key, windowSeconds, algorithm),
            RateLimitAlgorithm.LeakyBucket => await FallbackToFixedWindowAsync(key, windowSeconds, algorithm),
            _ => await IncrementFixedWindowAsync(key, windowSeconds)
        };
    }

    private async Task<long> FallbackToFixedWindowAsync(string key, int windowSeconds, RateLimitAlgorithm algorithm)
    {
        _logger?.LogWarning(
            "Rate limit algorithm {Algorithm} is not implemented. Falling back to FixedWindow for key {Key}.",
            algorithm,
            key);

        return await IncrementFixedWindowAsync(key, windowSeconds);
    }

    /// <summary>
    /// 固定窗口算法实现
    /// 直接使用 IncrementAsync 并始终传递过期时间，确保原子性
    /// IncrementAsync 在键不存在时会创建并设置过期时间，已存在时仅递增（不更新过期时间）
    /// </summary>
    private async Task<long> IncrementFixedWindowAsync(string key, int windowSeconds)
    {
        var cacheKey = GetCacheKey(key, "fixed");
        var expiration = TimeSpan.FromSeconds(windowSeconds);

        // 原子操作：递增并在首次创建时设置过期时间
        return await _cache.IncrementAsync(cacheKey, 1, expiration);
    }

    /// <summary>
    /// 滑动窗口算法实现（简化版本，使用多个固定窗口的近似）
    /// 注意：这是一个简化的实现，真正的滑动窗口需要更复杂的数据结构（如Redis ZSet）
    /// </summary>
    private async Task<long> IncrementSlidingWindowAsync(string key, int windowSeconds)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowStart = now / windowSeconds;
        
        // 使用当前窗口作为主键
        var cacheKey = GetCacheKey(key, $"sliding:{windowStart}");
        var expiration = TimeSpan.FromSeconds(windowSeconds * 2); // 过期时间设置为窗口的2倍，确保数据保留

        // 递增当前窗口计数
        var currentCount = await _cache.IncrementAsync(cacheKey, 1, expiration);

        // 计算滑动窗口的总计数（当前窗口 + 前一个窗口的部分计数）
        // 这是简化实现，真正的滑动窗口需要更精确的时间戳管理
        var previousWindowStart = windowStart - 1;
        var previousCacheKey = GetCacheKey(key, $"sliding:{previousWindowStart}");
        long? previousCountValue = await _cache.GetAsync<long>(previousCacheKey);
        var previousCount = previousCountValue ?? 0L;

        // 计算前一个窗口的权重（基于时间）
        var weight = (double)(now % windowSeconds) / windowSeconds;
        var weightedPreviousCount = (long)(previousCount * (1 - weight));

        return currentCount + weightedPreviousCount;
    }

    /// <summary>
    /// 获取缓存键
    /// </summary>
    private static string GetCacheKey(string key, string algorithmType)
    {
        return $"RateLimit:{algorithmType}:{key}";
    }
}
