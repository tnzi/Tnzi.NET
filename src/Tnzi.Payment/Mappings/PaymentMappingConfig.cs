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

        // 订阅实体的映射随续费域搬去了可选子模块 Tnzi.Payment.Subscriptions
        // （SubscriptionMappingConfig，规则一字不变）。IMappingConfig 是按程序集发现的，
        // 不加载那个包时那条规则连同它引用的两个类型一起不存在。

        // 优惠券核销记录的映射随促销域搬去了可选子模块 Tnzi.Payment.Promotions
        // （PromotionMappingConfig，规则一字不变）。源类型是那边的实体，目标 CouponUsageDto
        // 留在本模块（它在 ICouponService 的签名上）—— 子模块把自己的实体映到父模块的 DTO，
        // 方向正确。IMappingConfig 按程序集发现，不加载那个包时这条规则不存在。
    }
}
