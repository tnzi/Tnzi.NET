namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 续费域贡献给支付后台循环的租户来源：有存续订阅的租户。
/// </summary>
/// <remarks>
/// 父模块自己那条来源只看支付与退款表：一个只用试用、一笔支付都还没发生过的租户不在那里，
/// 它的试用到期转正就永远不会被扫到。存续的判据是「不是 Cancelled / Expired」——
/// Pending / Trial / Active / PastDue / PendingRenewal / Paused 都可能有事要做。
/// 跨租户只取 <c>TenantId</c> 一列；只在多租户开启时被调用（<see cref="IPaymentTenantSource"/> 的约定）。
/// </remarks>
public class SubscriptionTenantSource : IPaymentTenantSource
{
    private readonly IRepository<Subscription, Guid> _subscriptionRepository;

    public SubscriptionTenantSource(IRepository<Subscription, Guid> subscriptionRepository)
    {
        _subscriptionRepository = Check.NotNull(subscriptionRepository);
    }

    public async Task<IReadOnlyList<Guid?>> GetTenantIdsAsync(CancellationToken cancellationToken = default)
    {
        return await _subscriptionRepository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(s => !s.IsDeleted
                && s.Status != SubscriptionStatus.Cancelled
                && s.Status != SubscriptionStatus.Expired)
            .Select(s => s.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
