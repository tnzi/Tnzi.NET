namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 把「一张已保存的支付方式被绑上 / 被解绑」落到订阅行上。
/// </summary>
/// <remarks>
/// <para>
/// 两段 SQL 逐字搬自父模块拆分前的 <c>PaymentMethodService.SyncToUnboundSubscriptionsAsync</c>
/// 与 <c>ClearSubscriptionBindingAsync</c>：过滤条件、写入的五个 / 四个字段、日志文案都没有改，
/// 换的只是「谁拥有这段代码」。父模块从此不认识订阅表。
/// </para>
/// <para>
/// ★ <b>不自开事务、不自行提交</b>：<see cref="OnUnboundAsync"/> 由调用方在已经开启的物理事务里调用
/// （<c>PaymentMethodService</c> 先 <c>EnsureTransactionStartedAsync</c> 再进来）。
/// 这两段都是 <c>ExecuteUpdateAsync</c> 裸 SQL，物理事务是延迟开启的 ——
/// 自己再包一层会把「置卡失效」与「清订阅快照」拆成两个事务，只落一半的后果是
/// 订阅没了卡，而支付方式却还显示可用。
/// </para>
/// </remarks>
public class SubscriptionBindingSink : IStoredPaymentMethodBindingSink
{
    private readonly IRepository<Subscription, Guid> _subscriptionRepository;
    private readonly ILogger<SubscriptionBindingSink> _logger;

    public SubscriptionBindingSink(
        IRepository<Subscription, Guid> subscriptionRepository,
        ILogger<SubscriptionBindingSink> logger)
    {
        _subscriptionRepository = Check.NotNull(subscriptionRepository);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 把新绑定的默认支付方式同步到该用户尚未绑卡的有效订阅，让「绑了卡就能自动续费」成立。
    /// 已显式绑过其它卡的订阅不动，避免覆盖用户的明确选择。
    /// </remarks>
    public async Task OnBoundAsync(Guid userId, StoredPaymentMethod method, CancellationToken cancellationToken = default)
    {
        Check.NotNull(method);

        var affected = await _subscriptionRepository.AsQueryable()
            .Where(s => s.UserId == userId
                && s.ChannelCode == method.ChannelCode
                && s.StoredPaymentMethodId == null
                && s.Status != SubscriptionStatus.Cancelled
                && s.Status != SubscriptionStatus.Expired)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.StoredPaymentMethodId, method.Id)
                .SetProperty(x => x.PaymentMethodToken, method.Token)
                .SetProperty(x => x.ProviderCustomerId, method.ProviderCustomerId)
                .SetProperty(x => x.PaymentMethodBrand, method.Brand)
                .SetProperty(x => x.PaymentMethodLast4, method.Last4), cancellationToken);

        if (affected > 0)
            _logger.LogInformation("Bound payment method to {Count} subscriptions without one. UserId: {UserId}", affected, userId);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 清掉引用该支付方式的订阅快照，否则后台会拿一个已经作废的凭据反复扣款失败。
    /// </remarks>
    public Task<int> OnUnboundAsync(Guid paymentMethodId, CancellationToken cancellationToken = default)
    {
        return _subscriptionRepository.AsQueryable()
            .Where(s => s.StoredPaymentMethodId == paymentMethodId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.StoredPaymentMethodId, (Guid?)null)
                .SetProperty(x => x.PaymentMethodToken, (string?)null)
                .SetProperty(x => x.PaymentMethodBrand, (string?)null)
                .SetProperty(x => x.PaymentMethodLast4, (string?)null), cancellationToken);
    }
}
