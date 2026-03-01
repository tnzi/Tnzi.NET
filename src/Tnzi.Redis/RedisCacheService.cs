
namespace Tnzi.Redis;

/// <summary>
/// Redis缓存服务实现
/// </summary>
public class RedisCacheService : ICache, IPatternCache
{
    private readonly IDistributedCache _distributedCache;
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly string? _instanceName;
    private readonly ICacheSyncService? _cacheSyncService;

    /// <summary>
    /// 初始化一个<see cref="RedisCacheService"/>类型的新实例
    /// </summary>
    /// <param name="distributedCache">分布式缓存</param>
    /// <param name="connectionMultiplexer">Redis连接</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="instanceName">实例名称（用于键前缀）</param>
    /// <param name="cacheSyncService">缓存同步服务（可选）</param>
    public RedisCacheService(
        IDistributedCache distributedCache,
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisCacheService> logger,
        string? instanceName = null,
        ICacheSyncService? cacheSyncService = null)
    {
        _distributedCache = Check.NotNull(distributedCache);
        _connectionMultiplexer = Check.NotNull(connectionMultiplexer);
        _logger = Check.NotNull(logger);
        _instanceName = instanceName;
        _cacheSyncService = cacheSyncService;
    }

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
            var value = _distributedCache.GetString(cacheKey);
            if (string.IsNullOrEmpty(value))
                return default;

            return JsonSerializer.Deserialize<T>(value, TnziJsonDefaults.Options);
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
            var value = await _distributedCache.GetStringAsync(cacheKey, cancellationToken);
            if (string.IsNullOrEmpty(value))
                return default;

            return JsonSerializer.Deserialize<T>(value, TnziJsonDefaults.Options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache value for key: {Key}", key);
            return default;
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
            var options = new DistributedCacheEntryOptions();

            if (expirationSeconds.HasValue && expirationSeconds.Value > 0)
            {
                options.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(expirationSeconds.Value);
            }

            _distributedCache.SetString(cacheKey, json, options);
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
    /// <param name="expiration">过期时间，null表示不过期</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);

        try
        {
            var cacheKey = GetCacheKey(key);
            var json = JsonSerializer.Serialize(value, TnziJsonDefaults.Options);
            var options = new DistributedCacheEntryOptions();

            if (expiration.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = expiration;
            }

            await _distributedCache.SetStringAsync(cacheKey, json, options, cancellationToken);

            // 发布缓存更新通知（如果启用了缓存同步）
            // 使用 CancellationToken.None：fire-and-forget 任务不应受调用方取消令牌影响
            if (_cacheSyncService != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _cacheSyncService.PublishCacheInvalidationAsync(key, CacheOperation.Update, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to publish cache update notification for key: {Key}", key);
                    }
                }, CancellationToken.None);
            }
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
            _distributedCache.Remove(cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache value for key: {Key}", key);
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
            await _distributedCache.RemoveAsync(cacheKey, cancellationToken);

            // 发布缓存删除通知（如果启用了缓存同步）
            // 使用 CancellationToken.None：fire-and-forget 任务不应受调用方取消令牌影响
            if (_cacheSyncService != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _cacheSyncService.PublishCacheInvalidationAsync(key, CacheOperation.Remove, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to publish cache remove notification for key: {Key}", key);
                    }
                }, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache value for key: {Key}", key);
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
            var database = _connectionMultiplexer.GetDatabase();
            return database.KeyExists(cacheKey);
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
            var database = _connectionMultiplexer.GetDatabase();
            return await database.KeyExistsAsync(cacheKey);
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
            var database = _connectionMultiplexer.GetDatabase();
            database.KeyExpire(cacheKey, TimeSpan.FromSeconds(expirationSeconds));
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
            var database = _connectionMultiplexer.GetDatabase();
            await database.KeyExpireAsync(cacheKey, TimeSpan.FromSeconds(expirationSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing cache for key: {Key}", key);
        }
    }

    /// <summary>
    /// 收集所有 endpoints 上匹配指定模式的 keys（支持 Redis Cluster）
    /// </summary>
    private RedisKey[] CollectKeys(string cachePattern)
    {
        var allKeys = new HashSet<string>();
        var endpoints = _connectionMultiplexer.GetEndPoints();

        foreach (var endpoint in endpoints)
        {
            try
            {
                var server = _connectionMultiplexer.GetServer(endpoint);
                if (server.IsConnected && !server.IsReplica)
                {
                    foreach (var key in server.Keys(pattern: cachePattern))
                    {
                        allKeys.Add(key.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error scanning keys on endpoint {Endpoint}", endpoint);
            }
        }

        return allKeys.Select(k => (RedisKey)k).ToArray();
    }

    /// <summary>
    /// 异步收集所有 endpoints 上匹配指定模式的 keys（支持 Redis Cluster）
    /// </summary>
    private async Task<RedisKey[]> CollectKeysAsync(string cachePattern)
    {
        var allKeys = new HashSet<string>();
        var endpoints = _connectionMultiplexer.GetEndPoints();

        foreach (var endpoint in endpoints)
        {
            try
            {
                var server = _connectionMultiplexer.GetServer(endpoint);
                if (server.IsConnected && !server.IsReplica)
                {
                    await foreach (var key in server.KeysAsync(pattern: cachePattern))
                    {
                        allKeys.Add(key.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error scanning keys on endpoint {Endpoint}", endpoint);
            }
        }

        return allKeys.Select(k => (RedisKey)k).ToArray();
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

            var database = _connectionMultiplexer.GetDatabase();
            var keys = CollectKeys(cachePattern);

            if (keys.Length == 0)
                return 0;

            database.KeyDelete(keys);
            return keys.Length;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache by pattern: {Pattern}", pattern);
            return 0;
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

            var database = _connectionMultiplexer.GetDatabase();
            var keys = await CollectKeysAsync(cachePattern);

            if (keys.Length > 0)
            {
                await database.KeyDeleteAsync(keys);

                // 发布缓存删除通知（如果启用了缓存同步）
                if (_cacheSyncService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            foreach (var deletedKey in keys)
                            {
                                var keyStr = deletedKey.ToString();
                                // 移除实例名称前缀
                                if (!string.IsNullOrEmpty(_instanceName) && keyStr.StartsWith(_instanceName + ":"))
                                {
                                    keyStr = keyStr[(_instanceName.Length + 1)..];
                                }
                                await _cacheSyncService.PublishCacheInvalidationAsync(keyStr, CacheOperation.Remove, CancellationToken.None);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to publish cache remove notifications for pattern: {Pattern}", pattern);
                        }
                    }, CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache by pattern: {Pattern}", pattern);
        }
    }

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    public void Clear()
    {
        try
        {
            var database = _connectionMultiplexer.GetDatabase();

            var pattern = string.IsNullOrEmpty(_instanceName)
                ? "*"
                : $"{_instanceName}:*";

            var keys = CollectKeys(pattern);
            if (keys.Length > 0)
            {
                database.KeyDelete(keys);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
        }
    }

    /// <summary>
    /// 异步清空所有缓存
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var database = _connectionMultiplexer.GetDatabase();

            var pattern = string.IsNullOrEmpty(_instanceName)
                ? "*"
                : $"{_instanceName}:*";

            var keys = await CollectKeysAsync(pattern);
            if (keys.Length > 0)
            {
                await database.KeyDeleteAsync(keys);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
        }
    }

    /// <summary>
    /// 递增缓存值
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="increment">递增量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>递增后的值</returns>
    public async Task<long> IncrementAsync(string key, long increment = 1, CancellationToken cancellationToken = default)
    {
        return await IncrementAsync(key, increment, default(TimeSpan), cancellationToken);
    }

    /// <summary>
    /// 递增缓存值（带过期时间）
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="increment">递增量</param>
    /// <param name="expiration">过期时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>递增后的值</returns>
    public async Task<long> IncrementAsync(string key, long increment, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);

        try
        {
            var cacheKey = GetCacheKey(key);
            var database = _connectionMultiplexer.GetDatabase();
            var result = await database.StringIncrementAsync(cacheKey, increment);

            // 如果指定了过期时间，设置过期时间
            if (expiration != default)
            {
                await database.KeyExpireAsync(cacheKey, expiration);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error incrementing cache value for key: {Key}", key);
            return 0;
        }
    }

    /// <summary>
    /// 递减缓存值
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="decrement">递减量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>递减后的值</returns>
    public async Task<long> DecrementAsync(string key, long decrement = 1, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);

        try
        {
            var cacheKey = GetCacheKey(key);
            var database = _connectionMultiplexer.GetDatabase();
            return await database.StringDecrementAsync(cacheKey, decrement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrementing cache value for key: {Key}", key);
            return 0;
        }
    }

    /// <summary>
    /// 尝试设置缓存值（仅在键不存在时设置）
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="expiration">过期时间，null表示不过期</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果设置成功返回 true，如果键已存在返回 false</returns>
    public async Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);

        try
        {
            var cacheKey = GetCacheKey(key);
            var database = _connectionMultiplexer.GetDatabase();
            var json = JsonSerializer.Serialize(value, TnziJsonDefaults.Options);

            // 使用 Redis SET NX 原子操作，只在键不存在时设置
            bool success;
            if (expiration.HasValue && expiration.Value.TotalSeconds > 0)
            {
                success = await database.StringSetAsync(cacheKey, json, expiration.Value, When.NotExists);
            }
            else
            {
                success = await database.StringSetAsync(cacheKey, json, when: When.NotExists);
            }

            // 如果设置成功且启用了缓存同步，发布通知
            // 使用 CancellationToken.None：fire-and-forget 任务不应受调用方取消令牌影响
            if (success && _cacheSyncService != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _cacheSyncService.PublishCacheInvalidationAsync(key, CacheOperation.Update, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to publish cache set notification for key: {Key}", key);
                    }
                }, CancellationToken.None);
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
    /// 按前缀删除缓存（比 RemoveByPatternAsync 更高效）
    /// 使用前缀索引，时间复杂度接近 O(n)，n 为匹配的键数
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
            var database = _connectionMultiplexer.GetDatabase();
            var keys = await CollectKeysAsync($"{cachePrefix}*");

            if (keys.Length > 0)
            {
                await database.KeyDeleteAsync(keys);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache by prefix: {Prefix}", prefix);
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
        var database = _connectionMultiplexer.GetDatabase();

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
                if (!values[i].IsNullOrEmpty)
                {
                    try
                    {
                        var value = JsonSerializer.Deserialize<T>(values[i].ToString(), TnziJsonDefaults.Options);
                        var originalKey = keysList[i];
                        if (_instanceName != null && originalKey.StartsWith($"{_instanceName}:"))
                        {
                            originalKey = originalKey.Substring(_instanceName.Length + 1);
                        }
                        result[originalKey] = value;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error deserializing cache value for key: {Key}", keysList[i]);
                    }
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
    /// <param name="expiration">过期时间，null 使用默认过期时间</param>
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
            var database = _connectionMultiplexer.GetDatabase();
            var batch = database.CreateBatch();
            var tasks = new List<Task>();

            foreach (var item in itemsList)
            {
                if (string.IsNullOrEmpty(item.Key))
                {
                    continue;
                }

                var cacheKey = GetCacheKey(item.Key);
                var json = JsonSerializer.Serialize(item.Value, TnziJsonDefaults.Options);

                if (expiration.HasValue && expiration.Value.TotalSeconds > 0)
                {
                    tasks.Add(batch.StringSetAsync(cacheKey, json, expiration.Value));
                }
                else
                {
                    tasks.Add(batch.StringSetAsync(cacheKey, json));
                }
            }

            batch.Execute();
            await Task.WhenAll(tasks);

            // 发布缓存更新通知（如果启用了缓存同步）
            // 使用 CancellationToken.None：fire-and-forget 任务不应受调用方取消令牌影响
            if (_cacheSyncService != null)
            {
                foreach (var item in itemsList)
                {
                    if (!string.IsNullOrEmpty(item.Key))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _cacheSyncService.PublishCacheInvalidationAsync(item.Key, CacheOperation.Update, CancellationToken.None);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to publish cache batch update notification for key: {Key}", item.Key);
                            }
                        }, CancellationToken.None);
                    }
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
            var database = _connectionMultiplexer.GetDatabase();
            var redisKeys = keyList.Where(k => !string.IsNullOrEmpty(k)).Select(GetCacheKey).Select(k => (RedisKey)k).ToArray();

            if (redisKeys.Length > 0)
            {
                await database.KeyDeleteAsync(redisKeys);
            }

            // 发布缓存删除通知（如果启用了缓存同步）
            // 使用 CancellationToken.None：fire-and-forget 任务不应受调用方取消令牌影响
            if (_cacheSyncService != null)
            {
                foreach (var key in keyList)
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _cacheSyncService.PublishCacheInvalidationAsync(key, CacheOperation.Remove, CancellationToken.None);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to publish cache batch remove notification for key: {Key}", key);
                            }
                        }, CancellationToken.None);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing multiple cache values");
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
    /// <param name="expiration">过期时间，null 使用默认过期时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SetWithTagsAsync<T>(string key, T value, IEnumerable<string> tags, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(key);

        try
        {
            var cacheKey = GetCacheKey(key);
            var database = _connectionMultiplexer.GetDatabase();

            // 序列化值
            var json = JsonSerializer.Serialize(value, TnziJsonDefaults.Options);
            var expirationSeconds = expiration?.TotalSeconds ?? 0;

            // 设置缓存值
            if (expirationSeconds > 0)
            {
                await database.StringSetAsync(cacheKey, json, TimeSpan.FromSeconds(expirationSeconds));
            }
            else
            {
                await database.StringSetAsync(cacheKey, json);
            }

            // 为每个标签创建索引
            foreach (var tag in tags)
            {
                var tagKey = $"tag:{tag}";
                await database.SetAddAsync(tagKey, cacheKey);

                // 如果设置了过期时间，标签索引也需要设置过期时间（稍长一些）
                if (expirationSeconds > 0)
                {
                    await database.KeyExpireAsync(tagKey, TimeSpan.FromSeconds(expirationSeconds + 3600)); // 标签索引过期时间比缓存值长1小时
                }
            }

            // 触发缓存同步
            // 使用 CancellationToken.None：fire-and-forget 任务不应受调用方取消令牌影响
            if (_cacheSyncService != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _cacheSyncService.PublishCacheInvalidationAsync(key, CacheOperation.Update, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to publish cache set notification for key: {Key}", key);
                    }
                }, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache with tags for key: {Key}", key);
            throw;
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
            var database = _connectionMultiplexer.GetDatabase();
            var tagKey = $"tag:{tag}";

            // 获取该标签下的所有缓存键
            var keys = await database.SetMembersAsync(tagKey);

            // 删除所有缓存键
            if (keys.Length > 0)
            {
                var keysToDelete = keys.Select(k => (RedisKey)k.ToString()).ToArray();
                await database.KeyDeleteAsync(keysToDelete);
            }

            // 删除标签索引
            await database.KeyDeleteAsync(tagKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache by tag: {Tag}", tag);
        }
    }
}