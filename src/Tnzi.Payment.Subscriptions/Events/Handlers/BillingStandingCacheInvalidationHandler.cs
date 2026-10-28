namespace Tnzi.Payment.Subscriptions.Events;

/// <summary>
/// 账户境况一变就清掉门禁缓存：补缴成功的下一个请求就放行，暂停的下一个请求就拦下，不必等缓存过期。
/// </summary>
/// <remarks>
/// 这是辅助操作不是业务逻辑，失败了还有 TTL 兜底（<c>AccessGate:CacheSeconds</c>）。不包 try/catch：交给事件总线的重试与死信。
/// </remarks>
public class BillingStandingCacheInvalidationHandler : IEventHandler<BillingAccountStandingChangedEvent>
{
    private readonly SubscriptionStandingCache _cache;

    /// <summary>DI 构造函数。</summary>
    public BillingStandingCacheInvalidationHandler(SubscriptionStandingCache cache)
    {
        _cache = Check.NotNull(cache);
    }

    /// <inheritdoc />
    public Task HandleAsync(BillingAccountStandingChangedEvent eventData, CancellationToken cancellationToken = default)
    {
        Check.NotNull(eventData);
        return _cache.InvalidateAsync(eventData.BillingAccountType, eventData.BillingAccountId, cancellationToken);
    }
}
