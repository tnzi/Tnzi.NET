namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 订阅计划包含的存储与超额存储费：每天给计量中的账户采一次用量，续费时按上一期的平均用量算超额。
/// </summary>
/// <remarks>
/// <para>
/// 只有计划设了 <see cref="SubscriptionPlan.IncludedStorageBytes"/> 的订阅被计量；没设的计划（默认）一行都不采、一分都不收。
/// 用量取自核心的可选契约 <c>Tnzi.Storage.IStorageUsageReader</c>（加载 <c>Tnzi.Storage</c> 时才有）：
/// 没有它时不采样、不计超额，并记一条告警，<b>绝不当作 0 字节</b>。
/// </para>
/// <para>
/// 超额从不拦截上传，只计费：随结束那一期的续费一并后付，在收据上是单独的一行，与计划那一行同样计税。
/// </para>
/// </remarks>
public interface ISubscriptionStorageMeter
{
    /// <summary>
    /// 给每个计量中的账户记下今天（UTC）的用量。一个账户一天一条，同一天再跑不会多出一条。返回新记下的条数。
    /// </summary>
    Task<Result<int>> SampleAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 计费日为 <c>subscription.NextBillingTime</c> 的那一期的超额存储费；不计量、量不出来或没有超额时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="subscription">要续费的订阅。</param>
    /// <param name="plan">计价用的计划（续费所按的那个计划）。</param>
    /// <param name="measureWhenUnsampled">这一期一条样本都没有、计费日之后也没有时，当场量一次并记下（续费扣款时为 true；只是显示应付额时为 false）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<StorageOverageCharge?> PriceClosingPeriodAsync(Subscription subscription, SubscriptionPlan plan, bool measureWhenUnsampled = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 用户 <paramref name="userId"/> 所代表的账户当前的存储用量、包含量与本期至今累计的超额（需要查看账户的权限）。
    /// 还没有在用的订阅时，读数对着要买的那个计划的包含量（见 <see cref="StorageUsageDto.Subscribed"/>），不累计超额。
    /// </summary>
    Task<Result<StorageUsageDto>> GetUsageAsync(Guid userId, string? productCode = null, CancellationToken cancellationToken = default);
}
