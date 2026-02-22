
namespace Tnzi.Redis;

/// <summary>
/// Redis缓存同步服务实现（基于Redis Pub/Sub）
/// </summary>
public class RedisCacheSyncService : ICacheSyncService
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILogger<RedisCacheSyncService> _logger;
    private readonly string _channelName;
    private ISubscriber? _subscriber;

    /// <summary>
    /// 初始化一个<see cref="RedisCacheSyncService"/>类型的新实例
    /// </summary>
    public RedisCacheSyncService(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisCacheSyncService> logger,
        string? channelName = null)
    {
        _connectionMultiplexer = Check.NotNull(connectionMultiplexer);
        _logger = Check.NotNull(logger);
        _channelName = channelName ?? "Tnzi:Cache:Invalidation";
    }

    /// <summary>
    /// 发布缓存失效通知
    /// </summary>
    public async Task PublishCacheInvalidationAsync(string key, CacheOperation operation, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriber = _connectionMultiplexer.GetSubscriber();
            var message = new CacheInvalidationMessage
            {
                Key = key,
                Operation = operation,
                Timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(message);
            var channel = RedisChannel.Literal(_channelName);
            await subscriber.PublishAsync(channel, json);
            
            _logger.LogDebug("Published cache invalidation: {Operation} for key: {Key}", operation, key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish cache invalidation for key: {Key}", key);
        }
    }

    /// <summary>
    /// 订阅缓存失效通知
    /// </summary>
    public async Task SubscribeCacheInvalidationAsync(Func<string, CacheOperation, Task> handler, CancellationToken cancellationToken = default)
    {
        try
        {
            _subscriber = _connectionMultiplexer.GetSubscriber();
            var channel = RedisChannel.Literal(_channelName);
            
            await _subscriber.SubscribeAsync(channel, async (ch, message) =>
            {
                try
                {
                    if (message.IsNullOrEmpty)
                        return;

                    var json = message.ToString();
                    var invalidationMessage = JsonSerializer.Deserialize<CacheInvalidationMessage>(json);
                    
                    if (invalidationMessage != null)
                    {
                        await handler(invalidationMessage.Key, invalidationMessage.Operation);
                        _logger.LogDebug("Received cache invalidation: {Operation} for key: {Key}", 
                            invalidationMessage.Operation, invalidationMessage.Key);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing cache invalidation message");
                }
            });

            _logger.LogInformation("Subscribed to cache invalidation channel: {Channel}", _channelName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to cache invalidation channel");
        }
    }

    /// <summary>
    /// 缓存失效消息
    /// </summary>
    private class CacheInvalidationMessage
    {
        public string Key { get; set; } = string.Empty;
        public CacheOperation Operation { get; set; }
        public DateTime Timestamp { get; set; }
    }
}