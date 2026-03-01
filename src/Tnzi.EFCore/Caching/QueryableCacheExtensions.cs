
namespace Tnzi.EFCore.Caching;

/// <summary>
/// IQueryable查询缓存扩展方法
/// </summary>
public static class QueryableCacheExtensions
{
    /// <summary>
    /// 将查询结果转换为列表并缓存
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    /// <param name="source">查询源</param>
    /// <param name="cache">缓存服务</param>
    /// <param name="cacheKey">缓存键（可选，不提供时自动生成）</param>
    /// <param name="expiration">过期时间（可选）</param>
    /// <param name="keyGenerator">缓存键生成器（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>查询结果列表</returns>
    public static async Task<List<T>> ToCachedListAsync<T>(
        this IQueryable<T> source,
        ICache cache,
        string? cacheKey = null,
        TimeSpan? expiration = null,
        ICacheKeyGenerator? keyGenerator = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(cache);

        // 生成缓存键
        var key = cacheKey ?? GenerateCacheKey(source, keyGenerator);

        // 尝试从缓存获取
        var cached = await cache.GetAsync<List<T>>(key, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        // 执行查询
        var result = await source.ToListAsync(cancellationToken);

        // 存入缓存
        await cache.SetAsync(key, result, expiration, cancellationToken);

        return result;
    }

    /// <summary>
    /// 将查询结果转换为第一个元素并缓存
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    /// <param name="source">查询源</param>
    /// <param name="cache">缓存服务</param>
    /// <param name="cacheKey">缓存键（可选，不提供时自动生成）</param>
    /// <param name="expiration">过期时间（可选）</param>
    /// <param name="keyGenerator">缓存键生成器（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>查询结果或null</returns>
    public static async Task<T?> ToCachedFirstOrDefaultAsync<T>(
        this IQueryable<T> source,
        ICache cache,
        string? cacheKey = null,
        TimeSpan? expiration = null,
        ICacheKeyGenerator? keyGenerator = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(cache);

        // 生成缓存键
        var key = cacheKey ?? GenerateCacheKey(source, keyGenerator);

        // 尝试从缓存获取
        var cached = await cache.GetAsync<T>(key, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        // 执行查询
        var result = await source.FirstOrDefaultAsync(cancellationToken);

        // 存入缓存（即使为null也缓存，避免缓存穿透）
        await cache.SetAsync(key, result, expiration, cancellationToken);

        return result;
    }

    /// <summary>
    /// 将查询结果转换为分页列表并缓存
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    /// <param name="source">查询源</param>
    /// <param name="query">分页查询条件</param>
    /// <param name="cache">缓存服务</param>
    /// <param name="cacheKey">缓存键（可选，不提供时自动生成）</param>
    /// <param name="expiration">过期时间（可选）</param>
    /// <param name="keyGenerator">缓存键生成器（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分页列表</returns>
    public static async Task<IPagedList<T>> ToCachedPagedListAsync<T>(
        this IQueryable<T> source,
        PagedQuery query,
        ICache cache,
        string? cacheKey = null,
        TimeSpan? expiration = null,
        ICacheKeyGenerator? keyGenerator = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(cache);
        Check.NotNull(query);

        // 生成缓存键（包含分页参数）
        var key = cacheKey ?? GenerateCacheKey(source, keyGenerator, query.PageIndex, query.PageSize);

        // 尝试从缓存获取
        var cached = await cache.GetAsync<IPagedList<T>>(key, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        // 执行查询
        var totalCount = await source.CountAsync(cancellationToken);
        var items = await source.Skip((query.PageIndex - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var result = new PagedList<T>(items, query.PageIndex, query.PageSize, totalCount);

        // 存入缓存
        await cache.SetAsync(key, result, expiration, cancellationToken);

        return result;
    }

    /// <summary>
    /// 将查询结果转换为数组并缓存
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    /// <param name="source">查询源</param>
    /// <param name="cache">缓存服务</param>
    /// <param name="cacheKey">缓存键（可选，不提供时自动生成）</param>
    /// <param name="expiration">过期时间（可选）</param>
    /// <param name="keyGenerator">缓存键生成器（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>查询结果数组</returns>
    public static async Task<T[]> ToCachedArrayAsync<T>(
        this IQueryable<T> source,
        ICache cache,
        string? cacheKey = null,
        TimeSpan? expiration = null,
        ICacheKeyGenerator? keyGenerator = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(cache);

        // 生成缓存键
        var key = cacheKey ?? GenerateCacheKey(source, keyGenerator);

        // 尝试从缓存获取
        var cached = await cache.GetAsync<T[]>(key, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        // 执行查询
        var result = await source.ToArrayAsync(cancellationToken);

        // 存入缓存
        await cache.SetAsync(key, result, expiration, cancellationToken);

        return result;
    }

    /// <summary>
    /// 生成缓存键
    /// 警告：基于 Expression.ToString() 的自动键生成不稳定，不同 EF Core 版本或查询构建方式可能产生不同的字符串。
    /// 建议始终提供显式的 cacheKey 参数。
    /// </summary>
    private static string GenerateCacheKey<T>(IQueryable<T> source, ICacheKeyGenerator? keyGenerator, params object[] additionalParams)
    {
        if (keyGenerator != null)
        {
            var expressionString = source.Expression.ToString();
            return keyGenerator.GenerateForQuery<T>(expressionString, additionalParams);
        }

        // 基于表达式和参数生成哈希（不可靠，仅作为后备）
        var combined = $"{typeof(T).FullName}:{source.Expression}:{string.Join("|", additionalParams)}";

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
        return $"cache:query:{Convert.ToBase64String(hash)[..16]}";
    }
}