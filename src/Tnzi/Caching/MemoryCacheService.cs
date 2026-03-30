
namespace Tnzi.Caching;

/// <summary>
/// 线程安全的内存缓存服务
/// 支持原子计数器操作和模式匹配删除
/// </summary>
public class MemoryCacheService : ICache, IDisposable
{
    private readonly TimeSpan _defaultExpiration;

    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<MemoryCacheService> _logger;

    // 追踪所有缓存键（用于模式匹配删除）
    private readonly ConcurrentDictionary<string, byte> _trackedKeys = new();

    // 标签索引：标签 -> 缓存键集合
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagIndex = new();

    // 计数器同步锁
    private readonly object _incrementLock = new();

    // 编译后的正则表达式缓存（避免每次 RemoveByPattern 重编译）
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();

    private readonly IPerformanceMonitorService? _monitor;

    public MemoryCacheService(
        IMemoryCache memoryCache,
        ILogger<MemoryCacheService> logger,
        IOptions<CachingOptions> cachingOptions,
        IServiceProvider? serviceProvider = null)
    {
        _memoryCache = Check.NotNull(memoryCache);
        _logger = Check.NotNull(logger);
        _defaultExpiration = TimeSpan.FromMinutes(Check.NotNull(cachingOptions).Value.DefaultExpirationMinutes);
        _monitor = serviceProvider?.GetService<IPerformanceMonitorService>();
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_memoryCache.TryGetValue(key, out T? value))
        {
            _monitor?.RecordCacheHit(key);
            return Task.FromResult(value);
        }

        _monitor?.RecordCacheMiss(key);
        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var options = new MemoryCacheEntryOptions();
        if (expiration.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = expiration;
        }
        else
        {
            options.SlidingExpiration = _defaultExpiration;
        }

        // 如果 MemoryCache 配置了 SizeLimit，必须为每个条目指定 Size
        options.Size = 1;

        // 当缓存项被移除时，从键集合中删除
        options.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
        {
            var keyStr = evictedKey.ToString()!;
            _trackedKeys.TryRemove(keyStr, out _);
        });

        _memoryCache.Set(key, value, options);
        _trackedKeys.TryAdd(key, 0);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _memoryCache.Remove(key);
        _trackedKeys.TryRemove(key, out _);

        // 清理标签索引中的键引用
        CleanupKeyFromTagIndex(key);

        return Task.CompletedTask;
    }

    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var regex = _regexCache.GetOrAdd(pattern, static p =>
        {
            var regexPattern = "^" + Regex.Escape(p)
                .Replace(@"\*", ".*")
                .Replace(@"\?", ".") + "$";
            return new Regex(regexPattern, RegexOptions.Compiled);
        });

        var keysToRemove = _trackedKeys.Keys.Where(k => regex.IsMatch(k)).ToList();

        foreach (var key in keysToRemove)
        {
            _memoryCache.Remove(key);
            _trackedKeys.TryRemove(key, out _);
            CleanupKeyFromTagIndex(key);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_trackedKeys.ContainsKey(key));
    }

    public Task<long> IncrementAsync(string key, long increment = 1, CancellationToken cancellationToken = default)
    {
        return IncrementInternalAsync(key, increment, _defaultExpiration, cancellationToken);
    }

    public Task<long> IncrementAsync(string key, long increment, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        return IncrementInternalAsync(key, increment, expiration, cancellationToken);
    }

    public Task<long> DecrementAsync(string key, long decrement = 1, CancellationToken cancellationToken = default)
    {
        return IncrementInternalAsync(key, -decrement, _defaultExpiration, cancellationToken);
    }

    private Task<long> IncrementInternalAsync(string key, long increment, TimeSpan expiration, CancellationToken cancellationToken)
    {
        lock (_incrementLock)
        {
            var current = _memoryCache.TryGetValue(key, out long existing) ? existing : 0L;
            var newValue = current + increment;

            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration,
                Size = 1
            };
            options.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
            {
                var keyStr = evictedKey.ToString()!;
                _trackedKeys.TryRemove(keyStr, out _);
            });

            _memoryCache.Set(key, newValue, options);
            _trackedKeys.TryAdd(key, 0);

            return Task.FromResult(newValue);
        }
    }

    public Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        lock (_incrementLock)
        {
            // 检查键是否存在
            if (_trackedKeys.ContainsKey(key))
            {
                return Task.FromResult(false);
            }

            var expiry = expiration ?? _defaultExpiration;

            // 设置实际值
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry,
                Size = 1
            };

            options.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
            {
                var keyStr = evictedKey.ToString()!;
                _trackedKeys.TryRemove(keyStr, out _);
                CleanupKeyFromTagIndex(keyStr);
            });

            _memoryCache.Set(key, value, options);
            _trackedKeys.TryAdd(key, 0);

            return Task.FromResult(true);
        }
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var keysToRemove = _trackedKeys.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        foreach (var key in keysToRemove)
        {
            _memoryCache.Remove(key);
            _trackedKeys.TryRemove(key, out _);
        }

        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug("Removed {Count} cache entries with prefix '{Prefix}'", keysToRemove.Count, prefix);
        }

        return Task.CompletedTask;
    }

    public Task SetWithTagsAsync<T>(string key, T value, IEnumerable<string> tags, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var options = new MemoryCacheEntryOptions();
        if (expiration.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = expiration;
        }
        else
        {
            options.SlidingExpiration = _defaultExpiration;
        }
        options.Size = 1;

        // 注册移除回调，清理标签索引中的键引用
        options.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
        {
            var keyStr = evictedKey.ToString()!;
            _trackedKeys.TryRemove(keyStr, out _);

            // 清理所有标签索引中的键引用
            CleanupKeyFromTagIndex(keyStr);
        });

        _memoryCache.Set(key, value, options);
        _trackedKeys.TryAdd(key, 0);

        // 添加到标签索引
        foreach (var tag in tags)
        {
            var tagSet = _tagIndex.GetOrAdd(tag, _ => new ConcurrentDictionary<string, byte>());
            tagSet.TryAdd(key, 0);
        }

        return Task.CompletedTask;
    }

    public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return Task.CompletedTask;
        }

        if (_tagIndex.TryRemove(tag, out var keySet))
        {
            var removedCount = 0;
            foreach (var key in keySet.Keys)
            {
                if (_trackedKeys.TryRemove(key, out _))
                {
                    _memoryCache.Remove(key);
                    CleanupKeyFromTagIndex(key);
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                _logger.LogDebug("Removed {Count} cache entries with tag '{Tag}'", removedCount, tag);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 从所有标签索引中清理指定的键（防止内存泄漏）
    /// </summary>
    private void CleanupKeyFromTagIndex(string key)
    {
        foreach (var tagSet in _tagIndex.Values)
        {
            tagSet.TryRemove(key, out _);
        }
    }

    /// <inheritdoc />
    public Task<Dictionary<string, T?>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, T?>();

        foreach (var key in keys)
        {
            if (_memoryCache.TryGetValue(key, out var value) && value is T typedValue)
            {
                result[key] = typedValue;
                _monitor?.RecordCacheHit(key);
            }
        }

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task SetManyAsync<T>(
        IEnumerable<KeyValuePair<string, T>> items,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var expiry = expiration ?? _defaultExpiration;

        foreach (var item in items)
        {
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry,
                Size = 1
            };

            options.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
            {
                var keyStr = evictedKey.ToString()!;
                _trackedKeys.TryRemove(keyStr, out _);
                CleanupKeyFromTagIndex(keyStr);
            });

            _memoryCache.Set(item.Key, item.Value, options);
            _trackedKeys.TryAdd(item.Key, 0);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveManyAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var removedCount = 0;

        foreach (var key in keys)
        {
            if (_trackedKeys.TryRemove(key, out _))
            {
                _memoryCache.Remove(key);
                CleanupKeyFromTagIndex(key);
                removedCount++;
            }
        }

        if (removedCount > 0)
        {
            _logger.LogDebug("Removed {Count} cache entries", removedCount);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _trackedKeys.Clear();
        _tagIndex.Clear();
    }
}