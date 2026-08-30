namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 回答「这个用户以前订阅过吗」——<c>FirstSubscriptionOnly</c> 促销的判据。
/// </summary>
/// <remarks>
/// 查询逐字搬自父模块拆分前的 <c>PromotionService</c>：无状态过滤的
/// <c>AnyAsync(s =&gt; s.UserId == userId)</c>。刻意<b>包含</b>已取消 / 已过期的订阅 ——
/// 它回答的是「他不是新客」，不是「他现在是订户」。一个退订过再回来的人不该再拿一次首单折扣。
/// </remarks>
public class SubscriptionHistoryProbe : ISubscriptionHistoryProbe
{
    private readonly IRepository<Subscription, Guid> _subscriptionRepository;

    public SubscriptionHistoryProbe(IRepository<Subscription, Guid> subscriptionRepository)
    {
        _subscriptionRepository = Check.NotNull(subscriptionRepository);
    }

    /// <inheritdoc />
    public Task<bool> HasAnySubscriptionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return _subscriptionRepository.AsNoTracking().AnyAsync(s => s.UserId == userId, cancellationToken);
    }
}
