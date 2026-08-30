namespace Tnzi.Payment.Stripe.Services;

/// <summary>
/// 把促销登记成 Stripe 侧优惠券的实现。
/// </summary>
/// <remarks>
/// <para>
/// 拆分之前这段代码住在 <c>PromotionService</c> 里，靠把 <c>IPaymentProvider</c> 向下转型成
/// <c>StripeProvider</c> 再取一个 <c>internal</c> 的客户端访问器。那一步跨不过程序集边界，
/// 也让「促销服务」直接依赖了某一个渠道的具体类型；现在它是父模块声明的一个契约。
/// </para>
/// <para>
/// 只创建不更新：Stripe 的 Coupon 一旦创建，折扣数值与币种就不可改。已存在同名券时
/// Stripe 会拒绝，错误如实回给调用方 —— 不去猜「大概是上次同步过了」而报成功，
/// 因为那两种情形（真的同步过 / 有人手工建了同名券）在这里分不出来。
/// </para>
/// </remarks>
public class StripeCouponSync : IPaymentChannelCouponSync
{
    private readonly IOptions<StripeOptions> _options;
    private readonly ILogger<StripeCouponSync> _logger;

    /// <inheritdoc />
    public string ChannelCode => PaymentConstants.StripeChannelCode;

    /// <summary>初始化一个 <see cref="StripeCouponSync"/> 实例。</summary>
    public StripeCouponSync(IOptions<StripeOptions> options, ILogger<StripeCouponSync> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<Result<string>> SyncCouponAsync(PaymentChannelCouponDto coupon, CancellationToken cancellationToken = default)
    {
        Check.NotNull(coupon);

        try
        {
            var couponService = new global::Stripe.CouponService(StripeClientFactory.Create(_options.Value));

            var couponOptions = new CouponCreateOptions
            {
                Id = coupon.PromotionCode,
                Name = coupon.Name,
                Metadata = new Dictionary<string, string>
                {
                    { "PromotionId", coupon.PromotionId.ToString() },
                    { "PromotionCode", coupon.PromotionCode }
                }
            };

            if (coupon.DiscountType == DiscountType.Percentage)
            {
                couponOptions.PercentOff = coupon.DiscountValue;
            }
            else
            {
                // 币种取促销自身的币种，而不是写死 usd：固定金额折扣与币种强相关
                couponOptions.AmountOff = CurrencyInfo.ToMinorUnits(coupon.DiscountValue, coupon.Currency);
                couponOptions.Currency = coupon.Currency.ToLowerInvariant();
            }

            if (coupon.RedeemBy.HasValue)
                couponOptions.RedeemBy = coupon.RedeemBy.Value;

            if (coupon.MaxRedemptions.HasValue)
                couponOptions.MaxRedemptions = coupon.MaxRedemptions.Value;

            var stripeCoupon = await couponService.CreateAsync(couponOptions, cancellationToken: cancellationToken);

            _logger.LogInformation("Promotion synced to Stripe. PromotionCode: {Code}, CouponId: {CouponId}",
                coupon.PromotionCode, stripeCoupon.Id);

            return Result<string>.Success(stripeCoupon.Id);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to sync promotion to Stripe. PromotionId: {Id}", coupon.PromotionId);
            return Result.Failure<string>($"Stripe sync failed: {ex.Message}", 400);
        }
    }
}
