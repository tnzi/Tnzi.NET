namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 订阅收款在发票 / 收据上的明细行：计划名加这笔钱覆盖的服务期；续费连带收了超额存储费时另起一行。
/// </summary>
/// <remarks>
/// <para>
/// 服务期取自建单时写进支付的计费元数据（<see cref="SubscriptionBillingMetadata.PeriodStart"/> / <see cref="SubscriptionBillingMetadata.PeriodEnd"/>），
/// 不在开票时回头去推：开票发生在付款回流之后，那时订阅的周期可能已经推进了。
/// 早于引入服务期的支付没有这两项，答 <see langword="null"/>，开票方按支付描述生成一行，与此前相同。
/// </para>
/// <para>
/// ★ <b>超额存储那一行</b>（<see cref="SubscriptionBillingMetadata.StorageOverage"/>）：数量是 GB-月、单价是每 GB-月的价，
/// 服务期是被计量的那一期（上一期，后付）。税是对整笔支付算的，两行按各自的税前金额分摊，
/// 分摊的差额落在计划那一行，两行之和与支付的税额分毫不差：同一个税率，就是「与计划那一行同样计税」。
/// 没有超额的续费没有这一行。
/// </para>
/// </remarks>
public class SubscriptionPaymentLineDescriber : IPaymentLineDescriber
{
    /// <summary>超额存储那一行的 <see cref="PaymentLineDescription.Kind"/>。</summary>
    public const string StorageOverageLineKind = "storage-overage";

    /// <inheritdoc />
    public Task<IReadOnlyList<PaymentLineDescription>?> DescribeAsync(PaymentLineContext payment, CancellationToken cancellationToken = default)
    {
        Check.NotNull(payment);

        if (payment.BusinessType != BusinessType.Subscription)
            return Task.FromResult<IReadOnlyList<PaymentLineDescription>?>(null);

        var meta = SubscriptionBillingMetadata.TryParse(payment.ExtraData);
        if (meta?.PeriodStart == null)
            return Task.FromResult<IReadOnlyList<PaymentLineDescription>?>(null);

        var description = meta.Purpose switch
        {
            SubscriptionBillingPurpose.Proration => $"{meta.PlanName ?? payment.Description}: plan change, prorated",
            _ => meta.PlanName ?? payment.Description ?? payment.BusinessOrderNo
        };

        var overage = meta.StorageOverage is { Amount: > 0m } charge && charge.Amount <= payment.OriginalAmount
            ? charge
            : null;

        var planAmount = payment.OriginalAmount - (overage?.Amount ?? 0m);
        var overageTax = overage == null ? 0m : ShareOfTax(payment, overage.Amount, planAmount);

        var lines = new List<PaymentLineDescription>
        {
            new()
            {
                Description = description,
                Quantity = 1,
                UnitPrice = planAmount,
                Amount = planAmount,
                DiscountAmount = payment.DiscountAmount,
                TaxAmount = payment.TaxAmount - overageTax,
                PeriodStart = meta.PeriodStart,
                PeriodEnd = meta.PeriodEnd,
                ProductCode = payment.BusinessOrderNo
            }
        };

        if (overage != null)
        {
            lines.Add(new PaymentLineDescription
            {
                Description = DescribeOverage(overage),
                Quantity = overage.Quantity,
                UnitPrice = overage.UnitPrice,
                Amount = overage.Amount,
                TaxAmount = overageTax,
                PeriodStart = overage.PeriodStart,
                PeriodEnd = overage.PeriodEnd,
                ProductCode = payment.BusinessOrderNo,
                Kind = StorageOverageLineKind
            });
        }

        return Task.FromResult<IReadOnlyList<PaymentLineDescription>?>(lines);
    }

    /// <summary>「Storage above 250 GB included (GB-month)」：数量与单价各有自己的列，描述只说这一行是什么、单位是什么。</summary>
    internal static string DescribeOverage(StorageOverageCharge overage)
    {
        var includedGb = (decimal)overage.IncludedBytes / StorageOverageCalculator.BytesPerGb;
        return $"Storage above {includedGb.ToString("0.###", CultureInfo.InvariantCulture)} GB included (GB-month)";
    }

    /// <summary>超额那一行分到的税：按两行的计税基数（税前金额减折扣）比例，按币种取整。</summary>
    private static decimal ShareOfTax(PaymentLineContext payment, decimal overageAmount, decimal planAmount)
    {
        var planBase = Math.Max(0m, planAmount - payment.DiscountAmount);
        var total = planBase + overageAmount;
        if (total <= 0m || payment.TaxAmount == 0m)
            return 0m;

        return CurrencyInfo.Round(payment.TaxAmount * overageAmount / total, payment.Currency);
    }
}
