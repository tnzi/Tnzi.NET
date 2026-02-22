namespace Tnzi.Caching;

/// <summary>
/// 缓存过期策略接口
/// </summary>
public interface ICacheExpirationStrategy
{
    /// <summary>
    /// 获取过期时间
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="defaultExpiration">默认过期时间（秒）</param>
    /// <returns>过期时间（秒），null表示不过期</returns>
    int? GetExpirationSeconds(string key, int defaultExpiration);
}

