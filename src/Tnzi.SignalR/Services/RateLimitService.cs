// 所有必要的 using 语句已在 GlobalUsings.cs 中定义

namespace Tnzi.SignalR.Services;

/// <summary>
/// 速率限制服务实现
/// </summary>
public class RateLimitService : IRateLimitService
{
    private readonly ICache _cache;
    private readonly IConnectionManager _connectionManager;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RateLimitService> _logger;

    /// <summary>
    /// 初始化一个<see cref="RateLimitService"/>类型的新实例
    /// </summary>
    public RateLimitService(
        ICache cache,
        IConnectionManager connectionManager,
        IOptions<SignalROptions> options,
        ILogger<RateLimitService> logger)
    {
        _cache = Check.NotNull(cache);
        _connectionManager = Check.NotNull(connectionManager);
        _logger = Check.NotNull(logger);
        var rateLimit = Check.NotNull(options).Value.RateLimit;
        _options = rateLimit ?? throw new InvalidOperationException("SignalR.RateLimit cannot be null. Ensure SignalROptions is configured and RateLimit is set.");
    }

    /// <summary>
    /// 检查用户是否可以建立新连接
    /// </summary>
    public async Task<bool> CheckConnectionLimitAsync(Guid userId)
    {
        var connections = await _connectionManager.GetUserConnectionsAsync(userId);
        var connectionCount = connections.Count();

        if (connectionCount >= _options.MaxConnectionsPerUser)
        {
            _logger.LogWarning(
                "User {UserId} exceeded max connections ({Count}/{Max})",
                userId, connectionCount, _options.MaxConnectionsPerUser);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 检查用户是否可以发送消息
    /// </summary>
    public async Task<bool> CheckMessageRateLimitAsync(Guid userId)
    {
        var cacheKey = CacheKeys.SignalR.MessageRateCount(userId);
        var count = (await _cache.GetAsync<int?>(cacheKey)) ?? 0;

        if (count >= _options.MaxMessagesPerMinute)
        {
            _logger.LogWarning(
                "User {UserId} exceeded message rate limit ({Count}/{Max})",
                userId, count, _options.MaxMessagesPerMinute);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 记录消息发送（使用原子递增操作）
    /// </summary>
    public async Task RecordMessageAsync(Guid userId)
    {
        var cacheKey = CacheKeys.SignalR.MessageRateCount(userId);
        await _cache.IncrementAsync(cacheKey, 1, TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// 检查用户是否被封禁
    /// </summary>
    public async Task<bool> IsUserBannedAsync(Guid userId)
    {
        var cacheKey = CacheKeys.SignalR.UserBan(userId);
        return await _cache.ExistsAsync(cacheKey);
    }

    /// <summary>
    /// 封禁用户
    /// </summary>
    public async Task BanUserAsync(Guid userId, TimeSpan duration)
    {
        var cacheKey = CacheKeys.SignalR.UserBan(userId);
        await _cache.SetAsync(cacheKey, true, duration);

        _logger.LogWarning(
            "User {UserId} banned for {Duration} due to rate limit violation",
            userId, duration);
    }
}
