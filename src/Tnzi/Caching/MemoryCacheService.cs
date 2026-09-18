
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

    // 追踪所有缓存键（用于模式匹配删除）。值是每次写入独有的条目令牌：
    // 驱逐回调只在「令牌仍是自己的」时摘掉跟踪，上一版条目的迟到回调碰不到新条目。
    private readonly ConcurrentDictionary<string, object> _trackedKeys = new();

    // 标签索引：标签 -> 缓存键集合
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagIndex = new();

    // 计数器同步锁
    private readonly object _incrementLock = new();

    // 计数器窗口：键首次创建时定下的绝对过期时刻，之后的递增沿用它（固定窗口，见 IncrementInternalAsync）
    private readonly ConcurrentDictionary<string, CounterWindow> _counterWindows = new();
    private readonly TimeProvider _timeProvider;

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
        _timeProvider = serviceProvider?.GetService<TimeProvider>() ?? TimeProvider.System;
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

    /// <summary>
    /// 单次查找即可回答「在不在」（默认接口实现要访问两次）。
    /// </summary>
    public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_memoryCache.TryGetValue(key, out T? value))
        {
            _monitor?.RecordCacheHit(key);
            return Task.FromResult((true, value));
        }

        _monitor?.RecordCacheMiss(key);
        return Task.FromResult((false, default(T)));
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

        TrackEntry(key, options);
        _memoryCache.Set(key, value, options);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 把键登记进跟踪表，并在条目的驱逐回调里按<b>条目身份</b>撤销登记。
    /// </summary>
    /// <remarks>
    /// ★ 必须按身份不能按键名：<c>MemoryCache.Set</c> 覆盖已有键时，旧条目以
    /// <c>EvictionReason.Replaced</c> 从线程池异步回调，多半落在新条目登记之后 ——
    /// 回调若无条件 <c>TryRemove(key)</c>，就把刚登记的新条目一起摘掉了，
    /// 于是这把键活在缓存里却再也不被 RemoveByPrefix / Pattern / Tag / Many 看见
    /// （<c>FunctionAuthCache.ClearAllAsync</c> 清不干净，用户持旧权限直到过期）。
    /// 只看 <c>reason == Replaced</c> 也不够：上一版条目已过期但尚未被扫描时再写同键，
    /// 旧回调带的是 <c>Expired</c>。每次写入发一枚新令牌，回调只撤销与自己令牌相同的登记，
    /// 两种迟到回调都碰不到新条目。
    /// 登记放在 <c>Set</c> 之前，而且必须在之前：两个并发写入 A、B 各发一枚令牌，登记表最终留下的是
    /// 后登记的那枚；放在 <c>Set</c> 之后，登记顺序与落地顺序可以相反（A 落地、B 落地、B 登记、A 登记），
    /// 于是表里留的是被顶替的 A，A 的 Replaced 回调随即把它摘掉 —— 活着的 B 就再也不被批量删除看见。
    /// 代价是「有令牌」不等于「已在缓存里」，所以撤销登记的<b>唯一</b>出处是被驱逐条目自己的回调，
    /// 删除路径不得拿着令牌手工摘（见 <see cref="RemoveEntry"/>）。
    /// </remarks>
    private void TrackEntry(string key, MemoryCacheEntryOptions options)
    {
        var token = new object();
        _trackedKeys[key] = token;

        options.RegisterPostEvictionCallback((_, _, _, _) =>
        {
            if (_trackedKeys.TryRemove(new KeyValuePair<string, object>(key, token)))
            {
                // 条目真的没了才清标签索引；被新条目顶替时索引仍归新条目所有
                CleanupKeyFromTagIndex(key);
            }
        });
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        RemoveEntry(key);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 从真实缓存删除一个键并清掉它的标签索引引用；跟踪登记交给被删条目自己的驱逐回调撤销。
    /// </summary>
    /// <remarks>
    /// ★ 这里不碰 <c>_trackedKeys</c>：登记发生在 <c>Set</c> 之前（原因见 <see cref="TrackEntry"/>），
    /// 表里的令牌可能属于一个还没落地的并发写入。曾经的写法是「先取令牌、删缓存、再按令牌摘登记」——
    /// 删除若恰好落在那个写入的登记与落地之间，取到的就是新条目的令牌，摘掉的是新条目的登记，
    /// 而它随后照常落地：活在缓存里，却再也不被 RemoveByPrefix / Pattern / Tag / Many 看见。
    /// <c>MemoryCache.Remove</c> 只会驱逐当下真在缓存里的那一版，它的回调带的正是自己的令牌，
    /// 由它撤销登记不会摘错版本；表里偶尔多留一个不在缓存里的键，批量删除对它是无害的空操作。
    /// </remarks>
    private void RemoveEntry(string key)
    {
        _memoryCache.Remove(key);
        CleanupKeyFromTagIndex(key);
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
            RemoveEntry(key);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        // 以真实缓存为准，不能查 _trackedKeys：后者只在 MemoryCache 触发驱逐回调时才被清理，
        // 已过期但尚未被扫描到的键会被误报为存在（例如限流封禁键到期后仍显示封禁）
        return Task.FromResult(_memoryCache.TryGetValue(key, out _));
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

    /// <summary>
    /// 原子递增。<b>固定窗口</b>：过期时刻在键首次创建时定下，之后的递增沿用它，而不是每次把 TTL 往后推。
    /// </summary>
    /// <remarks>
    /// 每次重设 TTL 是滑动惩罚窗口：限流 100 次 / 60 秒，客户端超限后每次重试都把窗口续到 60 秒之后，
    /// 只要它以任何小于 60 秒的间隔重试，计数器永不过期 —— 被限流的调用方再也恢复不了。
    /// <c>RateLimitService</c>、2FA 失败锁定、每日计数这类调用方要的都是「首个命中起 W 内 N 次」。
    /// 窗口记录只在条目真的消失（过期 / 删除）时撤销；被下一次递增顶替（<see cref="EvictionReason.Replaced"/>）不算，
    /// 且只撤销自己那一份 —— 条目已过期但回调尚未触发时又有新递增开了新窗口，迟到的回调不能把新窗口摘掉。
    /// </remarks>
    private Task<long> IncrementInternalAsync(string key, long increment, TimeSpan expiration, CancellationToken cancellationToken)
    {
        lock (_incrementLock)
        {
            var exists = _memoryCache.TryGetValue(key, out long existing);
            var newValue = (exists ? existing : 0L) + increment;

            var window = exists && _counterWindows.TryGetValue(key, out var currentWindow)
                ? currentWindow
                : new CounterWindow(_timeProvider.GetUtcNow() + expiration);
            _counterWindows[key] = window;

            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = window.ExpiresAt,
                Size = 1
            };
            options.RegisterPostEvictionCallback((_, _, reason, _) =>
            {
                if (reason != EvictionReason.Replaced)
                {
                    _counterWindows.TryRemove(new KeyValuePair<string, CounterWindow>(key, window));
                }
            });

            TrackEntry(key, options);
            _memoryCache.Set(key, newValue, options);

            return Task.FromResult(newValue);
        }
    }

    /// <summary>
    /// 计数器的固定窗口：首次创建时定下的绝对过期时刻。引用相等即身份，回调据此只撤销自己那一份 ——
    /// 所以是 class 不是 record：record 的值相等会让两个过期时刻相同的窗口（同一时钟读数内删除再递增）互换身份，
    /// 迟到的回调就能摘掉活着的新窗口。
    /// </summary>
    private sealed class CounterWindow(DateTimeOffset expiresAt)
    {
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
    }

    public Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        lock (_incrementLock)
        {
            // 检查键是否存在（以真实缓存为准，理由同 ExistsAsync：
            // 查 _trackedKeys 会让已过期的键继续占位，SETNX 语义的消费方永远拿不到锁/一次性令牌）
            if (_memoryCache.TryGetValue(key, out _))
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

            TrackEntry(key, options);
            _memoryCache.Set(key, value, options);

            return Task.FromResult(true);
        }
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var keysToRemove = _trackedKeys.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        foreach (var key in keysToRemove)
        {
            RemoveEntry(key);
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

        TrackEntry(key, options);
        _memoryCache.Set(key, value, options);

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
            // 以真实缓存为准：跟踪表只是候选集，不拿它当「还在不在」的门 ——
            // 门一旦漏（历史上就漏过），条目就活在缓存里再也删不到。Remove 对不存在的键是无害的。
            foreach (var key in keySet.Keys)
            {
                RemoveEntry(key);
                removedCount++;
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

            TrackEntry(item.Key, options);
            _memoryCache.Set(item.Key, item.Value, options);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveManyAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var removedCount = 0;

        // 同 RemoveByTagAsync：以真实缓存为准，跟踪表不是门
        foreach (var key in keys)
        {
            RemoveEntry(key);
            removedCount++;
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
        _counterWindows.Clear();
    }
}