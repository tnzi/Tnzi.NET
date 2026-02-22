namespace Tnzi.Payment.Mappings;

/// <summary>
/// 支付模块映射配置
/// </summary>
public class PaymentMappingConfig : IMappingConfig
{
    /// <summary>
    /// 配置映射
    /// </summary>
    public void Configure(IMappingConfigContext context)
    {
        // 退款实体映射：映射关联的 TradeNo
        context.NewConfig<Refund, RefundDto>()
            .Map(dest => dest.TradeNo, src => src.Payment != null ? src.Payment.TradeNo : null);

        // 订阅实体映射：映射关联的 PlanName
        context.NewConfig<Subscription, SubscriptionDto>()
            .Map(dest => dest.PlanName, src => src.Plan != null ? src.Plan.PlanName : null);

        // 优惠券使用记录映射：映射关联的 CouponCode，使用 CreationTime 作为 UsedTime
        context.NewConfig<CouponUsage, CouponUsageDto>()
            .Map(dest => dest.CouponCode, src => src.Coupon != null ? src.Coupon.PromotionCode : null)
            .Map(dest => dest.UsedTime, src => src.CreationTime);
    }
}
