

namespace Tnzi.Caching;

/// <summary>
/// 默认缓存过期策略（固定时间）
/// </summary>
public class FixedExpirationStrategy : ICacheExpirationStrategy
{
    private readonly int _expirationSeconds;

    /// <summary>
    /// 初始化一个<see cref="FixedExpirationStrategy"/>类型的新实例
    /// </summary>
    /// <param name="expirationSeconds">过期时间（秒）</param>
    public FixedExpirationStrategy(int expirationSeconds)
    {
        Check.GreaterThan(expirationSeconds, 0);

        _expirationSeconds = expirationSeconds;
    }

    /// <summary>
    /// 获取过期时间
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="defaultExpiration">默认过期时间（秒）</param>
    /// <returns>过期时间（秒）</returns>
    public int? GetExpirationSeconds(string key, int defaultExpiration)
    {
        return _expirationSeconds;
    }
}

/// <summary>
/// 基于模式的缓存过期策略（根据键模式设置不同的过期时间）
/// </summary>
public class PatternBasedExpirationStrategy : ICacheExpirationStrategy
{
    private readonly Dictionary<string, int> _patternExpirations;
    private readonly List<(Regex Pattern, int ExpirationSeconds)> _compiledPatterns;

    /// <summary>
    /// 初始化一个<see cref="PatternBasedExpirationStrategy"/>类型的新实例
    /// </summary>
    /// <param name="patternExpirations">模式与过期时间的映射（键为模式，值为过期时间秒数）</param>
    public PatternBasedExpirationStrategy(Dictionary<string, int> patternExpirations)
    {
        _patternExpirations = Check.NotNull(patternExpirations);
        
        // 编译正则表达式模式
        _compiledPatterns = new List<(Regex Pattern, int ExpirationSeconds)>();
        foreach (var kvp in _patternExpirations)
        {
            var pattern = new Regex(kvp.Key, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _compiledPatterns.Add((pattern, kvp.Value));
        }
    }

    /// <summary>
    /// 获取过期时间
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="defaultExpiration">默认过期时间（秒）</param>
    /// <returns>过期时间（秒），null表示使用默认值</returns>
    public int? GetExpirationSeconds(string key, int defaultExpiration)
    {
        if (string.IsNullOrEmpty(key))
            return defaultExpiration;

        // 按顺序匹配模式，返回第一个匹配的过期时间
        foreach (var (pattern, expiration) in _compiledPatterns)
        {
            if (pattern.IsMatch(key))
            {
                return expiration;
            }
        }

        // 没有匹配的模式，返回默认值
        return defaultExpiration;
    }
}

/// <summary>
/// 滑动过期策略（每次访问时延长过期时间）
/// </summary>
public class SlidingExpirationStrategy : ICacheExpirationStrategy
{
    private readonly int _slidingExpirationSeconds;

    /// <summary>
    /// 初始化一个<see cref="SlidingExpirationStrategy"/>类型的新实例
    /// </summary>
    /// <param name="slidingExpirationSeconds">滑动过期时间（秒）</param>
    public SlidingExpirationStrategy(int slidingExpirationSeconds)
    {
        Check.GreaterThan(slidingExpirationSeconds, 0);

        _slidingExpirationSeconds = slidingExpirationSeconds;
    }

    /// <summary>
    /// 获取过期时间
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="defaultExpiration">默认过期时间（秒）</param>
    /// <returns>过期时间（秒）</returns>
    public int? GetExpirationSeconds(string key, int defaultExpiration)
    {
        return _slidingExpirationSeconds;
    }
}

/// <summary>
/// 组合过期策略（支持多个策略的组合）
/// </summary>
public class CompositeExpirationStrategy : ICacheExpirationStrategy
{
    private readonly List<ICacheExpirationStrategy> _strategies;

    /// <summary>
    /// 初始化一个<see cref="CompositeExpirationStrategy"/>类型的新实例
    /// </summary>
    /// <param name="strategies">策略列表</param>
    public CompositeExpirationStrategy(params ICacheExpirationStrategy[] strategies)
    {
        _strategies = Check.NotNull(strategies).ToList();
    }

    /// <summary>
    /// 获取过期时间（返回第一个非null的策略结果）
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="defaultExpiration">默认过期时间（秒）</param>
    /// <returns>过期时间（秒）</returns>
    public int? GetExpirationSeconds(string key, int defaultExpiration)
    {
        foreach (var strategy in _strategies)
        {
            var expiration = strategy.GetExpirationSeconds(key, defaultExpiration);
            if (expiration.HasValue)
            {
                return expiration;
            }
        }

        return defaultExpiration;
    }
}