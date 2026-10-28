namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 门禁用的账户境况缓存。每个请求都查一次订阅表太贵；境况变化时经事件主动失效，TTL 只是兜底。
/// </summary>
/// <remarks>
/// 键按账户 + 产品（空 = 所有产品，记作 <c>*</c>）；失效按账户前缀一次清掉，不必知道门禁配的是哪个产品。
/// 走框架的 <see cref="ICache"/>：装了分布式缓存的多实例部署，一个实例上的失效其余实例同时看得见。
/// </remarks>
public class SubscriptionStandingCache
{
    private const string KeyPrefix = "payment:subscription-standing:";

    private readonly ICache _cache;

    /// <summary>DI 构造函数。</summary>
    public SubscriptionStandingCache(ICache cache)
    {
        _cache = Check.NotNull(cache);
    }

    /// <summary>读缓存的境况；没有返回 null。</summary>
    public async Task<BillingStanding?> GetAsync(BillingAccountRef account, string? productCode, CancellationToken cancellationToken = default)
    {
        var entry = await _cache.GetAsync<CachedStanding>(KeyOf(account, productCode), cancellationToken);
        return entry?.Standing;
    }

    /// <summary>写入境况。<paramref name="ttl"/> 不为正时不写。</summary>
    public Task SetAsync(BillingAccountRef account, string? productCode, BillingStanding standing, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (ttl <= TimeSpan.Zero)
            return Task.CompletedTask;

        return _cache.SetAsync(KeyOf(account, productCode), new CachedStanding { Standing = standing }, ttl, cancellationToken);
    }

    /// <summary>清掉这个账户的全部缓存境况（不论产品）。</summary>
    public Task InvalidateAsync(string accountType, string accountId, CancellationToken cancellationToken = default)
        => _cache.RemoveByPrefixAsync($"{KeyPrefix}{accountType}:{accountId}:", cancellationToken);

    private static string KeyOf(BillingAccountRef account, string? productCode)
        => $"{KeyPrefix}{account.Type}:{account.Id}:{(string.IsNullOrEmpty(productCode) ? "*" : productCode)}";

    /// <summary>缓存载体：引用类型，让「没缓存」与「缓存了 None」分得开。</summary>
    public sealed class CachedStanding
    {
        /// <summary>境况。</summary>
        public BillingStanding Standing { get; set; }
    }
}
