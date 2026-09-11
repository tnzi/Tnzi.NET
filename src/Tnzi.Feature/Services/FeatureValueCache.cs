namespace Tnzi.Feature.Services;

/// <summary>
/// 按作用域缓存已解析的功能值 —— 包括「这个作用域没有值」这个答案 ——
/// 让热路径上的 <see cref="IFeatureChecker"/> 不再每次检查都两趟查库。
/// </summary>
/// <remarks>
/// <para>
/// 键 = provider 名 + provider 键 + 功能名，与 <see cref="FeatureValueChangedEvent"/> /
/// <see cref="FeatureValueDeletedEvent"/> 携带的三个字段一一对应，于是管理端的每一次写入都能
/// <b>精确</b>失效那一条，不必按前缀扫（<see cref="FeatureValueCacheInvalidationHandler"/>）。
/// </para>
/// <para>
/// ★<b>负结果也缓存</b>：绝大多数检查命中的是「这个作用域没有值、往下落到默认值」，
/// 不缓存它就等于没有缓存。
/// </para>
/// <para>
/// ★TTL 由 <see cref="FeatureOptions.ValueCacheSeconds"/> 热读，0 = 关。本实例的写入经事件立刻失效；
/// 多实例部署下其它实例最多滞后一个 TTL —— 值变更事件走的是本地总线，这是刻意的取舍：
/// 功能开关的定义快照本来就按 <see cref="FeatureOptions.CacheRefreshIntervalMinutes"/> 分钟级刷新。
/// </para>
/// <para>
/// ★缓存自身出错（Redis 抖动、序列化失败）时降级为直查并记 Warning —— 功能检查不能因为缓存而失败。
/// </para>
/// </remarks>
public sealed class FeatureValueCache
{
    private const string KeyPrefix = "Feature:Value:";

    private readonly ICache _cache;
    private readonly IOptionsMonitor<FeatureOptions> _options;
    private readonly ILogger<FeatureValueCache> _logger;

    /// <summary>
    /// 初始化功能值缓存。
    /// </summary>
    public FeatureValueCache(ICache cache, IOptionsMonitor<FeatureOptions> options, ILogger<FeatureValueCache> logger)
    {
        _cache = Check.NotNull(cache);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 取缓存值，未命中时调用 <paramref name="loader"/> 并把结果（含 null）写回。
    /// </summary>
    /// <param name="providerName">provider 名（持久化用的写法）。</param>
    /// <param name="providerKey">provider 键；无键 provider 传 null。</param>
    /// <param name="featureName">功能名。</param>
    /// <param name="loader">未命中时的真实查询。</param>
    public async Task<string?> GetOrLoadAsync(string providerName, string? providerKey, string featureName, Func<Task<string?>> loader)
    {
        Check.NotNullOrWhiteSpace(providerName);
        Check.NotNullOrWhiteSpace(featureName);
        Check.NotNull(loader);

        var seconds = _options.CurrentValue.ValueCacheSeconds;
        if (seconds <= 0)
        {
            return await loader();
        }

        var key = BuildKey(providerName, providerKey, featureName);

        try
        {
            var hit = await _cache.GetAsync<FeatureValueCacheEntry>(key);
            if (hit != null)
            {
                return hit.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feature value cache read failed for {Key}; falling back to the database.", key);
            return await loader();
        }

        var value = await loader();

        try
        {
            await _cache.SetAsync(key, new FeatureValueCacheEntry(value), TimeSpan.FromSeconds(seconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feature value cache write failed for {Key}; the value was still resolved.", key);
        }

        return value;
    }

    /// <summary>
    /// 失效一条作用域的缓存 —— 键的三段与值变更 / 删除事件的字段一一对应。
    /// </summary>
    public async Task InvalidateAsync(string providerName, string? providerKey, string featureName)
    {
        Check.NotNullOrWhiteSpace(providerName);
        Check.NotNullOrWhiteSpace(featureName);

        var key = BuildKey(providerName, providerKey, featureName);
        try
        {
            await _cache.RemoveAsync(key);
        }
        catch (Exception ex)
        {
            // 失效失败只意味着这一条最多再活一个 TTL，不该让写入路径失败。
            _logger.LogWarning(ex, "Feature value cache invalidation failed for {Key}; the entry expires with its TTL.", key);
        }
    }

    /// <summary>
    /// 缓存键。provider 键为空时用 <c>-</c> 占位，与「有键但为空串」区分开。
    /// </summary>
    public static string BuildKey(string providerName, string? providerKey, string featureName)
        => $"{KeyPrefix}{providerName}:{(string.IsNullOrEmpty(providerKey) ? "-" : providerKey)}:{featureName}";
}

/// <summary>
/// 缓存里的一条功能值；<see cref="Value"/> 为 null 表示「该作用域没有值」（负缓存），
/// 与「缓存未命中」（整个条目不存在）是两件事。
/// </summary>
public sealed record FeatureValueCacheEntry(string? Value);
