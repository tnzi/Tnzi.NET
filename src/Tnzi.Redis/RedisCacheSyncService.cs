namespace Tnzi.Redis;

/// <summary>
/// Redis缓存同步服务实现（基于Redis Pub/Sub）
/// </summary>
/// <remarks>
/// 发布与订阅的失败都抛给调用方。订阅失败（Redis 抖动、ACL 禁止 SUBSCRIBE）若只记一行日志就正常返回，
/// 调用方以为订阅成功，本地 L1 从此收不到任何失效通知，各实例长期答旧值；
/// 发布失败由 <see cref="RedisCacheService"/> 的后台发布统一记 Warning。
/// </remarks>
public class RedisCacheSyncService : ICacheSyncService
{
    /// <summary>
    /// 未配置实例名时的频道名。
    /// </summary>
    public const string DefaultChannelName = "Tnzi:Cache:Invalidation";

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
        _channelName = channelName ?? DefaultChannelName;
    }

    /// <summary>
    /// 某个实例名对应的频道：<c>{instanceName}:Tnzi:Cache:Invalidation</c>，实例名为空时为 <see cref="DefaultChannelName"/>。
    /// Redis 的 Pub/Sub 不分库，共用一个 Redis 的应用必须各用各的频道。
    /// </summary>
    public static string ChannelNameFor(string? instanceName)
        => string.IsNullOrEmpty(instanceName) ? DefaultChannelName : $"{instanceName}:{DefaultChannelName}";

    /// <summary>
    /// 发布缓存失效通知。失败时原样抛出（见类型 remarks）。
    /// </summary>
    public async Task PublishCacheInvalidationAsync(string key, CacheOperation operation, CancellationToken cancellationToken = default)
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

    /// <summary>
    /// 订阅缓存失效通知。订阅失败时原样抛出（见类型 remarks），由调用方决定启动失败还是重试。
    /// </summary>
    public async Task SubscribeCacheInvalidationAsync(Func<string, CacheOperation, Task> handler, CancellationToken cancellationToken = default)
    {
        Check.NotNull(handler);

        _subscriber = _connectionMultiplexer.GetSubscriber();
        var channel = RedisChannel.Literal(_channelName);

        await _subscriber.SubscribeAsync(channel, async (ch, message) =>
        {
            // 单条消息处理失败不能让订阅终止：记下来，继续收后面的
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
