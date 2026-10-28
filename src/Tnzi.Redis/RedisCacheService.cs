// 与 StackExchange.Redis.RedisConnectionException 同名：这里要的是本模块的类型（CacheException 族）
using RedisConnectionException = Tnzi.Redis.Exceptions.RedisConnectionException;

namespace Tnzi.Redis;

/// <summary>
/// Redis缓存服务实现
/// </summary>
/// <remarks>
/// <para>
/// 所有读写方法统一使用裸 <see cref="IDatabase"/> 的 String 表示（值序列化为 JSON），单键/批量/标签方法互通。
/// </para>
/// <para>
/// 错误语义分三类，判据是「失败能不能自愈」：
/// <list type="bullet">
/// <item>读路径（Get/Exists/GetMany 等）与写入路径（Set/SetMany/TrySet）<b>fail-open</b>：失败记 LogError
/// 并返回默认值/空集合/false。写不进去等于一次未命中，下一次读会回填。</item>
/// <item>计数器（Increment/Decrement）<b>fail-closed</b>：失败记 LogError 后抛 <see cref="CacheWriteException"/>，
/// 避免配额/限流等消费者把故障误读为"计数清零"。</item>
/// <item>失效路径（Remove/RemoveMany/RemoveByPattern/RemoveByPrefix/RemoveByTag/Clear，含同步重载）与它的前半段
/// SetWithTags（值写进去而标签索引没建上时，RemoveByTag 永远找不到这一条）<b>fail-closed</b>：
/// 失败记 LogError 后抛 <see cref="CacheWriteException"/>。删除失败不会自愈 —— 键活到 TTL、没有 TTL 的活到永远，
/// 而吞掉异常会让调用方看到「删除成功」：权限撤销、会话吊销、一次性令牌消费这类失效的调用方要的正是失败可见
/// （事件处理器据此让总线重试）。请求路径上若「500 比陈旧更糟」，由调用方就地捕获并降级。</item>
/// </list>
/// </para>
/// </remarks>
public class RedisCacheService : ICache
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly string? _instanceName;
    private readonly ICacheSyncService? _cacheSyncService;
    private readonly TimeSpan _defaultExpiration;
    private readonly IPerformanceMonitorService? _performanceMonitor;

    /// <summary>
    /// 初始化一个<see cref="RedisCacheService"/>类型的新实例
    /// </summary>
    /// <param name="connectionMultiplexer">Redis连接</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="instanceName">实例名称（用于键前缀，隔离多应用共享的同一 Redis 实例）</param>
    /// <param name="cacheSyncService">缓存同步服务（可选）</param>
    /// <param name="defaultExpiration">
    /// 写入未指定过期时间时所用的默认过期时间（<c>Caching:DefaultExpirationMinutes</c>）；null 取 <see cref="CachingOptions"/> 的默认值
    /// </param>
    /// <param name="performanceMonitor">
    /// 运行期计数（可选）：读路径按命中 / 未命中记录，与核心内存实现同一口径。
    /// 不传的话 Redis 部署的缓存命中率恒无样本。
    /// </param>
    public RedisCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisCacheService> logger,
        string? instanceName = null,
        ICacheSyncService? cacheSyncService = null,
        TimeSpan? defaultExpiration = null,
        IPerformanceMonitorService? performanceMonitor = null)
    {
        _connectionMultiplexer = Check.NotNull(connectionMultiplexer);
        _logger = Check.NotNull(logger);
        _instanceName = instanceName;
        _cacheSyncService = cacheSyncService;
        _defaultExpiration = defaultExpiration ?? TimeSpan.FromMinutes(new CachingOptions().DefaultExpirationMinutes);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_defaultExpiration, TimeSpan.Zero, nameof(defaultExpiration));
        _performanceMonitor = performanceMonitor;
    }

    /// <summary>
    /// 获取当前 Redis 数据库句柄
    /// </summary>
    private IDatabase GetDatabase() => _connectionMultiplexer.GetDatabase();

    /// <summary>
    /// 获取缓存键（添加实例名称前缀）
    /// </summary>
    private string GetCacheKey(string key)
    {
        if (string.IsNullOrEmpty(_instanceName))
            return key;
        return $"{_instanceName}:{key}";
    }

    /// <summary>
    /// 获取标签索引键（同样带实例名称前缀，避免多应用共享 Redis 时标签互相污染）
    /// </summary>
    private string GetTagKey(string tag) => GetCacheKey($"tag:{tag}");

    /// <summary>
    /// 把 <see cref="ICache"/> 写入方法收到的过期时间换成实际的 TTL：null 取默认过期时间（与契约、
    /// <c>MemoryCacheService</c> 一致），显式的非正值表示不过期。
    /// </summary>
    /// <remarks>
    /// ★ 此前 null 一律不设 TTL：同一句 <c>TrySetAsync(lockKey, "1")</c> 在内存缓存上到默认过期时间自动释放，
    /// 上了 Redis 后持锁进程崩溃留下的键永不过期，<c>Caching:DefaultExpirationMinutes</c> 对 Redis 也从未生效。
    /// </remarks>
    private TimeSpan? ResolveExpiration(TimeSpan? expiration)
    {
        if (!expiration.HasValue)
            return _defaultExpiration;

        return expiration.Value > TimeSpan.Zero ? expiration : null;
    }

    /// <summary>
    /// 统一的字符串写入入口（裸 IDatabase，String 表示）。<paramref name="ttl"/> 是已经
    /// <see cref="ResolveExpiration"/> 换算过的值（null = 不过期）。
    /// 显式指定 <see cref="When"/> 重载，确保所有写路径绑定同一方法签名。
    /// </summary>
    private static Task<bool> WriteStringAsync(IDatabase database, string cacheKey, string json, TimeSpan? ttl, When when = When.Always)
        => database.StringSetAsync(cacheKey, json, ttl, when);

    /// <summary>
    /// 把完整 Redis 键还原成调用方看到的逻辑键（去掉实例前缀）。
    /// </summary>
    private string ToLogicalKey(string fullKey)
        => !string.IsNullOrEmpty(_instanceName) && fullKey.StartsWith(_instanceName + ":", StringComparison.Ordinal)
            ? fullKey[(_instanceName.Length + 1)..]
            : fullKey;

    /// <summary>
    /// fire-and-forget 发布一条失效通知（启用了 <see cref="ICacheSyncService"/> 时）。
    /// 使用 <see cref="CancellationToken.None"/>：后台任务不应受调用方取消令牌影响。
    /// </summary>
    /// <remarks>
    /// ★ <b>每一条写入 / 删除路径都要经过这里或 <see cref="PublishRemovedKeys"/></b>，同步重载也不例外。
    /// 这条通道只对「在 Redis 之上自建了 L1 的消费方」有意义，而一条覆盖不全的失效通道比没有更危险：
    /// Redis 里删干净了、日志干净、没有异常，只有部分实例答旧值。
    /// </remarks>
    private void PublishInvalidation(string key, CacheOperation operation)
    {
        if (_cacheSyncService == null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _cacheSyncService.PublishCacheInvalidationAsync(key, operation, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish cache {Operation} notification for key: {Key}", operation, key);
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// 为一批已删除的完整 Redis 键逐键发布 <see cref="CacheOperation.Remove"/>（键先还原成逻辑键）。
    /// 一个后台任务顺序发完，而不是每键一个 <c>Task.Run</c>。
    /// </summary>
    private void PublishRemovedKeys(IEnumerable<RedisKey> deletedKeys, string context)
    {
        if (_cacheSyncService == null)
            return;

        var logicalKeys = deletedKeys.Select(k => ToLogicalKey(k.ToString())).ToArray();
        if (logicalKeys.Length == 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var key in logicalKeys)
                {
                    await _cacheSyncService.PublishCacheInvalidationAsync(key, CacheOperation.Remove, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish cache remove notifications for {Context}", context);
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// 获取缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <returns>缓存值</returns>
    public T? Get<T>(string key)
    {
        Check.NotNullOrEmpty(key);

        try
        {
            var cacheKey = GetCacheKey(key);
            var value = GetDatabase().StringGet(cacheKey);
            if (value.IsNullOrEmpty)
            {
                _performanceMonitor?.RecordCacheMiss(key);
                return default;
            }

            _performanceMonitor?.RecordCacheHit(key);
            return JsonSerializer.Deserialize<T>(value.ToString(), TnziJsonDefaults.Options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache value for key: {Key}", key);
            return default;
        }
    }

    /// <summary>
    /// 异步获取缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>缓存值</returns>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);

        try
        {
            var cacheKey = GetCacheKey(key);
            var value = await GetDatabase().StringGetAsync(cacheKey);
            if (value.IsNullOrEmpty)
            {
                _performanceMonitor?.RecordCacheMiss(key);
                return default;
            }

            _performanceMonitor?.RecordCacheHit(key);
            return JsonSerializer.Deserialize<T>(value.ToString(), TnziJsonDefaults.Options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache value for key: {Key}", key);
            return default;
        }
    }

    /// <summary>
    /// 读取缓存值并回答「键在不在」：一次 <c>GET</c>，以值是否存在判断，不再追问一次 <c>EXISTS</c>。
    /// </summary>
    /// <remarks>
    /// ★ 存着却读不出来的条目（部署改了缓存 DTO 的属性类型、枚举成员改名，而旧条目还没过期）按<b>未命中</b>回答。
    /// 默认接口实现是「取值为 null 就再问存在性」：<see cref="GetAsync{T}"/> 把反序列化失败吞成 null，存在性又是 true，
    /// 于是回答「命中了 null」。<c>GetOrAddAsync</c> 见命中就返回、不调工厂、不覆盖坏条目，
    /// 在剩余 TTL 内（没有 TTL 的永远）对每个调用都答 null。按未命中回答之后，<c>GetOrAddAsync</c> 走工厂重建并覆盖它。
    /// </remarks>
    public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);
        return TryGetCoreAsync<T>(key, recordMetrics: true);
    }

    /// <summary>
    /// 取值，没有就调工厂建一个并写入；每次调用恰好计一次命中或一次未命中。
    /// </summary>
    /// <remarks>
    /// ★ 默认接口实现未命中时先 <see cref="TryGetAsync{T}"/>、拿到锁后再 <see cref="TryGetAsync{T}"/> 复查，
    /// 两次都计数：一次未命中记成两次，<c>admin/performance/runtime</c> 的命中率被系统性低估
    /// （真实 50% 显示约 33%）。这里语义与默认实现相同（按键加锁防击穿、读不出的旧条目按未命中重建、
    /// 工厂返回 null 不写缓存），只是复查不计数。锁是进程内的：它挡的是同一实例上的并发重建。
    /// </remarks>
    public async Task<T?> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);
        Check.NotNull(factory);

        var (found, value) = await TryGetCoreAsync<T>(key, recordMetrics: true);
        if (found)
            return value;

        await using (await StampedeGuard.LockAsync(key, cancellationToken))
        {
            // 复查：另一个调用方可能已经填好了。本次调用已记过一次未命中，这里不再计数
            (found, value) = await TryGetCoreAsync<T>(key, recordMetrics: false);
            if (found)
                return value;

            value = await factory();

            if (value is not null)
                await SetAsync(key, value, expiration, cancellationToken);
        }

        return value;
    }

    /// <summary><see cref="GetOrAddAsync{T}"/> 的按键锁（防缓存击穿）。</summary>
    private static readonly KeyedAsyncLock StampedeGuard = new();

    private async Task<(bool Found, T? Value)> TryGetCoreAsync<T>(string key, bool recordMetrics)
    {
        RedisValue value;
        try
        {
            value = await GetDatabase().StringGetAsync(GetCacheKey(key));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache value for key: {Key}", key);
            return (false, default);
        }

        if (value.IsNull)
        {
            if (recordMetrics) _performanceMonitor?.RecordCacheMiss(key);
            return (false, default);
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<T>(value.ToString(), TnziJsonDefaults.Options);
            if (recordMetrics) _performanceMonitor?.RecordCacheHit(key);
            return (true, deserialized);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            _logger.LogWarning(ex,
                "Cache entry {Key} cannot be read as {Type}; treating it as a miss so it gets rebuilt", key, typeof(T).Name);
            if (recordMetrics) _performanceMonitor?.RecordCacheMiss(key);
            return (false, default);
        }
    }

    /// <summary>
    /// 设置缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="expirationSeconds">过期时间（秒），null表示不过期</param>
    public void Set<T>(string key, T value, int? expirationSeconds = null)
    {
        Check.NotNullOrEmpty(key);

        try
        {
            var cacheKey = GetCacheKey(key);
            var json = JsonSerializer.Serialize(value, TnziJsonDefaults.Options);
            var expiry = expirationSeconds.HasValue && expirationSeconds.Value > 0
                ? TimeSpan.FromSeconds(expirationSeconds.Value)
                : (TimeSpan?)null;

            GetDatabase().StringSet(cacheKey, json, expiry, When.Always);
            PublishInvalidation(key, CacheOperation.Update);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache value for key: {Key}", key);
        }
    }

    /// <summary>
    /// 异步设置缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="expiration">过期时间，null 使用默认过期时间，非正值表示不过期</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);

        try
        {
            var cacheKey = GetCacheKey(key);
            var json = JsonSerializer.Serialize(value, TnziJsonDefaults.Options);

            await WriteStringAsync(GetDatabase(), cacheKey, json, ResolveExpiration(expiration));
            PublishInvalidation(key, CacheOperation.Update);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache value for key: {Key}", key);
        }
    }

    /// <summary>
    /// 删除缓存
    /// </summary>
    /// <param name="key">缓存键</param>
    public void Remove(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        try
        {
            var cacheKey = GetCacheKey(key);
            GetDatabase().KeyDelete(cacheKey);
            PublishInvalidation(key, CacheOperation.Remove);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache value for key: {Key}", key);
            throw new CacheWriteException($"Failed to remove cache key '{key}'.", key, ex);
        }
    }

    /// <summary>
    /// 异步删除缓存
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key))
            return;

        try
        {
            var cacheKey = GetCacheKey(key);
            await GetDatabase().KeyDeleteAsync(cacheKey);
            PublishInvalidation(key, CacheOperation.Remove);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache value for key: {Key}", key);
            throw new CacheWriteException($"Failed to remove cache key '{key}'.", key, ex);
        }
    }

    /// <summary>
    /// 检查缓存是否存在
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <returns>是否存在</returns>
    public bool Exists(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        try
        {
            var cacheKey = GetCacheKey(key);
            return GetDatabase().KeyExists(cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cache existence for key: {Key}", key);
            return false;
        }
    }

    /// <summary>
    /// 异步检查缓存是否存在
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否存在</returns>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        try
        {
            var cacheKey = GetCacheKey(key);
            return await GetDatabase().KeyExistsAsync(cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cache existence for key: {Key}", key);
            return false;
        }
    }

    /// <summary>
    /// 刷新缓存（延长过期时间）
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="expirationSeconds">新的过期时间（秒）</param>
    public void Refresh(string key, int expirationSeconds)
    {
        if (string.IsNullOrEmpty(key) || expirationSeconds <= 0)
            return;

        try
        {
            var cacheKey = GetCacheKey(key);
            GetDatabase().KeyExpire(cacheKey, TimeSpan.FromSeconds(expirationSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing cache for key: {Key}", key);
        }
    }

    /// <summary>
    /// 异步刷新缓存（延长过期时间）
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="expirationSeconds">新的过期时间（秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task RefreshAsync(string key, int expirationSeconds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key) || expirationSeconds <= 0)
            return;

        try
        {
            var cacheKey = GetCacheKey(key);
            await GetDatabase().KeyExpireAsync(cacheKey, TimeSpan.FromSeconds(expirationSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing cache for key: {Key}", key);
        }
    }

    /// <summary>
    /// 收集所有主节点上匹配指定模式的 keys（单机、主从、Sentinel 拓扑下连接可能有多个端点）。
    /// </summary>
    /// <remarks>
    /// 只有失效路径（RemoveByPattern / RemoveByPrefix / Clear）调用它，所以它必须 <b>fail-closed</b>：
    /// 副本被跳过（键空间的权威副本在主节点上），但一个<b>断开的主节点</b>或一次扫描失败都意味着
    /// 「有键没扫到」，此时抛出而不是返回残缺的集合 —— 残缺集合会让后续 DEL 对着空数组「成功」，
    /// 调用方看到删除成功而键一个都没动，外层的 <see cref="CacheWriteException"/> 永远碰不到。
    /// <para>
    /// ★ 不支持 Redis Cluster：收集到的键随后由一条多键 <c>DEL</c> 删除，而键没有 hash tag、分布在不同槽，
    /// 集群模式下客户端以「多键操作必须在同一个槽」拒绝它（<c>GetManyAsync</c> 的 <c>MGET</c>、
    /// <c>RemoveManyAsync</c> / <c>RemoveByTagAsync</c> 的 <c>DEL</c> 同理）。
    /// </para>
    /// </remarks>
    /// <exception cref="RedisConnectionException">某个主节点当前未连接。</exception>
    private RedisKey[] CollectKeys(string cachePattern)
    {
        var allKeys = new HashSet<string>();
        var endpoints = _connectionMultiplexer.GetEndPoints();

        foreach (var endpoint in endpoints)
        {
            var server = _connectionMultiplexer.GetServer(endpoint);
            if (server.IsReplica)
                continue;

            ThrowIfPrimaryDisconnected(server, endpoint);
            foreach (var key in server.Keys(pattern: cachePattern))
            {
                allKeys.Add(key.ToString());
            }
        }

        return allKeys.Select(k => (RedisKey)k).ToArray();
    }

    /// <summary>
    /// 异步收集所有主节点上匹配指定模式的 keys。语义见 <see cref="CollectKeys"/>。
    /// </summary>
    /// <exception cref="RedisConnectionException">某个主节点当前未连接。</exception>
    private async Task<RedisKey[]> CollectKeysAsync(string cachePattern)
    {
        var allKeys = new HashSet<string>();
        var endpoints = _connectionMultiplexer.GetEndPoints();

        foreach (var endpoint in endpoints)
        {
            var server = _connectionMultiplexer.GetServer(endpoint);
            if (server.IsReplica)
                continue;

            ThrowIfPrimaryDisconnected(server, endpoint);
            await foreach (var key in server.KeysAsync(pattern: cachePattern))
            {
                allKeys.Add(key.ToString());
            }
        }

        return allKeys.Select(k => (RedisKey)k).ToArray();
    }

    /// <summary>
    /// 主节点未连接时抛出：这里没有异常可包，但「跳过它」等于把一整个节点的键当作不存在。
    /// </summary>
    private static void ThrowIfPrimaryDisconnected(IServer server, EndPoint endpoint)
    {
        if (server.IsConnected)
            return;

        throw new RedisConnectionException(
            $"Redis endpoint '{endpoint}' is not connected; its key space cannot be scanned for removal.",
            endpoint.ToString());
    }

    /// <summary>
    /// 批量删除缓存（按模式匹配）
    /// </summary>
    /// <param name="pattern">键模式（支持通配符，如 "user:*"）</param>
    /// <returns>删除的键数量</returns>
    public int RemoveByPattern(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return 0;

        try
        {
            var cachePattern = string.IsNullOrEmpty(_instanceName)
                ? pattern
                : $"{_instanceName}:{pattern}";

            var database = GetDatabase();
            var keys = CollectKeys(cachePattern);

            if (keys.Length == 0)
                return 0;

            database.KeyDelete(keys);
            PublishRemovedKeys(keys, $"pattern '{pattern}'");
            return keys.Length;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache by pattern: {Pattern}", pattern);
            throw new CacheWriteException($"Failed to remove cache keys matching pattern '{pattern}'.", pattern, ex);
        }
    }

    /// <summary>
    /// 异步批量删除缓存（按模式匹配）
    /// </summary>
    /// <param name="pattern">键模式（支持通配符，如 "user:*"）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pattern))
            return;

        try
        {
            var cachePattern = string.IsNullOrEmpty(_instanceName)
                ? pattern
                : $"{_instanceName}:{pattern}";

            var database = GetDatabase();
            var keys = await CollectKeysAsync(cachePattern);

            if (keys.Length > 0)
            {
                await database.KeyDeleteAsync(keys);
                PublishRemovedKeys(keys, $"pattern '{pattern}'");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache by pattern: {Pattern}", pattern);
            throw new CacheWriteException($"Failed to remove cache keys matching pattern '{pattern}'.", pattern, ex);
        }
    }

    /// <summary>
    /// 清空本实例名下的所有缓存
    /// </summary>
    /// <exception cref="InvalidOperationException">没有配置实例名（见 <see cref="ThrowIfClearWouldWipeTheDatabase"/>）。</exception>
    public void Clear()
    {
        ThrowIfClearWouldWipeTheDatabase();

        try
        {
            var database = GetDatabase();

            var keys = CollectKeys($"{_instanceName}:*");
            if (keys.Length > 0)
            {
                database.KeyDelete(keys);
            }

            // 一条通配 Clear 而不是逐键：订阅方据此整体清空 L1。键量可能很大，逐键发只会把通道刷满
            PublishInvalidation("*", CacheOperation.Clear);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
            throw new CacheWriteException("Failed to clear the cache.", null, ex);
        }
    }

    /// <summary>
    /// 异步清空本实例名下的所有缓存
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <exception cref="InvalidOperationException">没有配置实例名（见 <see cref="ThrowIfClearWouldWipeTheDatabase"/>）。</exception>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClearWouldWipeTheDatabase();

        try
        {
            var database = GetDatabase();

            var keys = await CollectKeysAsync($"{_instanceName}:*");
            if (keys.Length > 0)
            {
                await database.KeyDeleteAsync(keys);
            }

            // 一条通配 Clear 而不是逐键：订阅方据此整体清空 L1。键量可能很大，逐键发只会把通道刷满
            PublishInvalidation("*", CacheOperation.Clear);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
            throw new CacheWriteException("Failed to clear the cache.", null, ex);
        }
    }

    /// <summary>
    /// 没有实例名时拒绝清空：那时「本实例的键」就是整个库，<c>SCAN *</c> + <c>DEL</c> 会一并删掉分布式锁
    /// （正在持有的 Outbox 中继、数据销毁的互斥随之失效）、会话、数据保护密钥，以及同库其它应用的全部数据。
    /// 失败关闭，而不是照做。
    /// </summary>
    private void ThrowIfClearWouldWipeTheDatabase()
    {
        if (string.IsNullOrEmpty(_instanceName))
        {
            throw new InvalidOperationException(
                "Clearing the cache requires Caching:RedisInstanceName. Without an instance name every key in the " +
                "Redis database belongs to the cache, so a clear would also delete distributed locks, sessions and " +
                "any other application's data in the same database. Set an instance name, or remove keys by prefix.");
        }
    }

    /// <summary>
    /// 递增缓存值
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="increment">递增量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>递增后的值</returns>
    /// <exception cref="CacheWriteException">递增失败时抛出（fail-closed，语义见类型 remarks）。</exception>
    public async Task<long> IncrementAsync(string key, long increment = 1, CancellationToken cancellationToken = default)
    {
        return await IncrementAsync(key, increment, default(TimeSpan), cancellationToken);
    }

    /// <summary>
    /// INCRBY 后仅当键<b>还没有</b> TTL 时才 PEXPIRE：过期时刻在键首次创建时定下，之后的递增沿用（固定窗口）。
    /// 原子脚本，任何 Redis 版本都支持（<c>EXPIRE ... NX</c> 要 7.0）。
    /// </summary>
    private const string IncrementWithWindowScript = @"
            local v = redis.call('incrby', KEYS[1], ARGV[1])
            if redis.call('pttl', KEYS[1]) == -1 then
                redis.call('pexpire', KEYS[1], ARGV[2])
            end
            return v";

    /// <summary>
    /// 递增缓存值（带过期时间）。<b>固定窗口</b>：过期时刻在键首次创建时定下，之后的递增沿用它。
    /// </summary>
    /// <remarks>
    /// 此前每次递增都重设 TTL，那是滑动惩罚窗口：限流 100 次 / 60 秒，客户端超限后每次重试都把窗口续到 60 秒之后，
    /// 只要它以任何小于 60 秒的间隔重试，计数器永不过期 —— 被限流的调用方再也恢复不了，与配置的窗口长度无关。
    /// <c>RateLimitService</c>、2FA 失败锁定、每日计数这类调用方要的都是「首个命中起 W 内 N 次」。
    /// </remarks>
    /// <param name="key">缓存键</param>
    /// <param name="increment">递增量</param>
    /// <param name="expiration">过期时间（仅键首次创建时生效）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>递增后的值</returns>
    /// <exception cref="CacheWriteException">递增失败时抛出（fail-closed，语义见类型 remarks）。</exception>
    public async Task<long> IncrementAsync(string key, long increment, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);

        var cacheKey = GetCacheKey(key);
        try
        {
            var database = GetDatabase();
            if (expiration <= TimeSpan.Zero)
            {
                return await database.StringIncrementAsync(cacheKey, increment);
            }

            var result = await database.ScriptEvaluateAsync(
                IncrementWithWindowScript,
                new RedisKey[] { cacheKey },
                new RedisValue[] { increment, (long)expiration.TotalMilliseconds });

            return (long)result!;
        }
        catch (Exception ex)
        {
            // 计数器失败必须对调用方可见：记录后抛出，绝不静默返回 0
            _logger.LogError(ex, "Error incrementing cache value for key: {Key}", key);
            throw new CacheWriteException($"Failed to increment counter for cache key '{key}'.", key, ex);
        }
    }

    /// <summary>
    /// 递减缓存值
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="decrement">递减量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>递减后的值</returns>
    /// <exception cref="CacheWriteException">递减失败时抛出（fail-closed，语义见类型 remarks）。</exception>
    public async Task<long> DecrementAsync(string key, long decrement = 1, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);

        var cacheKey = GetCacheKey(key);
        try
        {
            var database = GetDatabase();
            return await database.StringDecrementAsync(cacheKey, decrement);
        }
        catch (Exception ex)
        {
            // 计数器失败必须对调用方可见：记录后抛出，绝不静默返回 0
            _logger.LogError(ex, "Error decrementing cache value for key: {Key}", key);
            throw new CacheWriteException($"Failed to decrement counter for cache key '{key}'.", key, ex);
        }
    }

    /// <summary>
    /// 尝试设置缓存值（仅在键不存在时设置）
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="expiration">过期时间，null 使用默认过期时间，非正值表示不过期</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果设置成功返回 true，如果键已存在返回 false</returns>
    public async Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);

        try
        {
            var cacheKey = GetCacheKey(key);
            var json = JsonSerializer.Serialize(value, TnziJsonDefaults.Options);

            // 使用 Redis SET NX 原子操作，只在键不存在时设置
            var success = await WriteStringAsync(GetDatabase(), cacheKey, json, ResolveExpiration(expiration), When.NotExists);
            if (success)
            {
                PublishInvalidation(key, CacheOperation.Update);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to set cache value for key: {Key}", key);
            return false;
        }
    }

    /// <summary>
    /// 按前缀删除缓存。与 <see cref="RemoveByPatternAsync"/> 同一机制（各节点 SCAN 一遍键空间），
    /// 只是把前缀补上 <c>*</c> 组成模式；键很多时代价与库内键总数成正比，不是前缀索引查找。
    /// </summary>
    /// <param name="prefix">缓存键前缀，如 "user:" 将匹配所有以 "user:" 开头的键</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(prefix))
            return;

        try
        {
            var cachePrefix = GetCacheKey(prefix);
            var database = GetDatabase();
            var keys = await CollectKeysAsync($"{cachePrefix}*");

            if (keys.Length > 0)
            {
                await database.KeyDeleteAsync(keys);
                PublishRemovedKeys(keys, $"prefix '{prefix}'");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache by prefix: {Prefix}", prefix);
            throw new CacheWriteException($"Failed to remove cache keys with prefix '{prefix}'.", prefix, ex);
        }
    }

    /// <summary>
    /// 批量获取缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="keys">缓存键集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>键值对字典，不存在的键不包含在结果中</returns>
    public async Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, T?>();
        var database = GetDatabase();

        var keysList = keys.Where(k => !string.IsNullOrEmpty(k)).Select(GetCacheKey).ToList();
        if (keysList.Count == 0)
        {
            return result;
        }

        try
        {
            var redisKeys = keysList.Select(k => (RedisKey)k).ToArray();
            var values = await database.StringGetAsync(redisKeys);

            for (int i = 0; i < keysList.Count; i++)
            {
                var logicalKey = ToLogicalKey(keysList[i]);
                if (values[i].IsNullOrEmpty)
                {
                    _performanceMonitor?.RecordCacheMiss(logicalKey);
                    continue;
                }

                try
                {
                    result[logicalKey] = JsonSerializer.Deserialize<T>(values[i].ToString(), TnziJsonDefaults.Options);
                    _performanceMonitor?.RecordCacheHit(logicalKey);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error deserializing cache value for key: {Key}", keysList[i]);
                    _performanceMonitor?.RecordCacheMiss(logicalKey);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting multiple cache values");
        }

        return result;
    }

    /// <summary>
    /// 批量设置缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="items">键值对集合</param>
    /// <param name="expiration">过期时间，null 使用默认过期时间，非正值表示不过期</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SetManyAsync<T>(IEnumerable<KeyValuePair<string, T>> items, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        if (items == null)
        {
            return;
        }

        // 提前物化，避免多次枚举 IEnumerable（调用方可能传入延迟查询）
        var itemsList = items as IReadOnlyList<KeyValuePair<string, T>> ?? items.ToList();
        if (itemsList.Count == 0)
        {
            return;
        }

        try
        {
            var database = GetDatabase();
            var batch = database.CreateBatch();
            var tasks = new List<Task>();
            var expiry = ResolveExpiration(expiration);

            foreach (var item in itemsList)
            {
                if (string.IsNullOrEmpty(item.Key))
                {
                    continue;
                }

                var cacheKey = GetCacheKey(item.Key);
                var json = JsonSerializer.Serialize(item.Value, TnziJsonDefaults.Options);
                tasks.Add(batch.StringSetAsync(cacheKey, json, expiry, When.Always));
            }

            batch.Execute();
            await Task.WhenAll(tasks);

            foreach (var item in itemsList)
            {
                if (!string.IsNullOrEmpty(item.Key))
                {
                    PublishInvalidation(item.Key, CacheOperation.Update);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting multiple cache values");
        }
    }

    /// <summary>
    /// 批量移除缓存项
    /// </summary>
    /// <param name="keys">缓存键集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys == null)
        {
            return;
        }

        // 提前物化，避免多次枚举 IEnumerable
        var keyList = keys as IReadOnlyList<string> ?? keys.ToList();
        if (keyList.Count == 0)
        {
            return;
        }

        try
        {
            var database = GetDatabase();
            var redisKeys = keyList.Where(k => !string.IsNullOrEmpty(k)).Select(GetCacheKey).Select(k => (RedisKey)k).ToArray();

            if (redisKeys.Length > 0)
            {
                await database.KeyDeleteAsync(redisKeys);
                PublishRemovedKeys(redisKeys, $"{redisKeys.Length} key(s)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing multiple cache values");
            throw new CacheWriteException($"Failed to remove {keyList.Count} cache key(s).", null, ex);
        }
    }

    /// <summary>
    /// 设置带标签的缓存
    /// 标签可用于批量失效相关缓存项
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="tags">标签列表</param>
    /// <param name="expiration">过期时间，null 使用默认过期时间，非正值表示不过期</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SetWithTagsAsync<T>(string key, T value, IEnumerable<string> tags, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);

        try
        {
            var cacheKey = GetCacheKey(key);
            var database = GetDatabase();

            // 序列化值
            var json = JsonSerializer.Serialize(value, TnziJsonDefaults.Options);
            var expiry = ResolveExpiration(expiration);

            // 设置缓存值（与所有其它写路径一致，走裸 IDatabase string）
            await WriteStringAsync(database, cacheKey, json, expiry);

            // 为每个标签创建索引（标签键带实例前缀，避免多应用共享 Redis 时互相污染）
            foreach (var tag in tags)
            {
                var tagKey = GetTagKey(tag);
                await database.SetAddAsync(tagKey, cacheKey);

                // 如果设置了过期时间，标签索引也需要设置过期时间（比缓存值长 1 小时，避免索引先于成员失效）
                if (expiry.HasValue)
                {
                    await database.KeyExpireAsync(tagKey, expiry.Value + TimeSpan.FromHours(1));
                }
            }

            PublishInvalidation(key, CacheOperation.Update);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache with tags for key: {Key}", key);
            throw new CacheWriteException($"Failed to set cache key '{key}' with tags.", key, ex);
        }
    }

    /// <summary>
    /// 按标签删除缓存
    /// 删除所有带有指定标签的缓存项
    /// </summary>
    /// <param name="tag">标签名</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tag))
            return;

        try
        {
            var database = GetDatabase();
            var tagKey = GetTagKey(tag);

            // 获取该标签下的所有缓存键（成员本身已是带前缀的完整键）
            var keys = await database.SetMembersAsync(tagKey);

            // 删除所有缓存键
            if (keys.Length > 0)
            {
                var keysToDelete = keys.Select(k => (RedisKey)k.ToString()).ToArray();
                await database.KeyDeleteAsync(keysToDelete);
                PublishRemovedKeys(keysToDelete, $"tag '{tag}'");
            }

            // 删除标签索引
            await database.KeyDeleteAsync(tagKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache by tag: {Tag}", tag);
            throw new CacheWriteException($"Failed to remove cache keys tagged '{tag}'.", tag, ex);
        }
    }
}
