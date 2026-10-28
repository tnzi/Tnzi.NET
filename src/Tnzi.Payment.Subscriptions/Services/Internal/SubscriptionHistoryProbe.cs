namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 回答「这个用户以前订阅过吗」——<c>FirstSubscriptionOnly</c> 促销的判据。
/// </summary>
/// <remarks>
/// <para>
/// 刻意<b>包含</b>已取消 / 已过期的订阅：它回答的是「他不是新客」，不是「他现在是订户」。
/// 一个退订过再回来的人不该再拿一次首单折扣。
/// </para>
/// <para>
/// ★ 但只算<b>真正开始过</b>的订阅：开过试用、付过一笔订阅款，或此刻处在只有开始之后才到得了的状态。
/// 从没开始的那种（<c>Pending</c>，或没付首期就被取消 / 过期的）不算。此前任何一行都算，
/// 而开通链路是先落库订阅、再建首期支付并在建单时核销券：核销时探针看到的正是刚插入的这条 Pending，
/// 于是「仅限首次订阅」的券对每一个新用户都核销不掉，预检与试算却都答「可用」。
/// 只有开启全局工作单元、插入被推迟到之后才写库的宿主侥幸正常。
/// </para>
/// </remarks>
public class SubscriptionHistoryProbe : ISubscriptionHistoryProbe
{
    private readonly IRepository<Subscription, Guid> _subscriptionRepository;

    public SubscriptionHistoryProbe(IRepository<Subscription, Guid> subscriptionRepository)
    {
        _subscriptionRepository = Check.NotNull(subscriptionRepository);
    }

    /// <inheritdoc />
    public Task<bool> HasAnySubscriptionAsync(BillingAccountRef account, CancellationToken cancellationToken = default)
    {
        Check.NotNull(account);

        return _subscriptionRepository.AsNoTracking().AnyAsync(
            s => s.BillingAccountType == account.Type && s.BillingAccountId == account.Id
                && (s.Status == SubscriptionStatus.Trial
                    || s.Status == SubscriptionStatus.Active
                    || s.Status == SubscriptionStatus.PendingRenewal
                    || s.Status == SubscriptionStatus.Paused
                    || s.Status == SubscriptionStatus.PastDue
                    || s.Status == SubscriptionStatus.Suspended
                    || s.TrialStartTime != null
                    || (s.LastBillingTradeNo != null && s.LastBillingTradeNo != "")
                    || s.PaidAmount > 0),
            cancellationToken);
    }
}
