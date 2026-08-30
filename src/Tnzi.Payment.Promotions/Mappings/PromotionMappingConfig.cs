namespace Tnzi.Payment.Promotions.Mappings;

/// <summary>
/// 促销模块映射配置。
/// </summary>
/// <remarks>
/// 规则与拆分前父模块 <c>PaymentMappingConfig</c> 里的那一条一字不变。
/// 源类型 <see cref="CouponUsage"/> 是本模块的实体，目标 <c>CouponUsageDto</c> 留在父模块
/// （它在父模块自己的 <c>ICouponService.ApplyCouponAsync</c> 签名上）——
/// 方向是「子模块把自己的实体映到父模块的 DTO」，属于允许的子 → 父。
///
/// <c>IMappingConfig</c> 是按<b>程序集</b>发现的：不加载本包时，这条规则连同它引用的实体一起不存在，
/// 父模块也不会再为一个它没有的实体注册映射。
/// </remarks>
public class PromotionMappingConfig : IMappingConfig
{
    /// <summary>
    /// 配置映射
    /// </summary>
    public void Configure(IMappingConfigContext context)
    {
        // 优惠券使用记录映射：映射关联的 CouponCode，使用 CreationTime 作为 UsedTime
        context.NewConfig<CouponUsage, CouponUsageDto>()
            .Map(dest => dest.CouponCode, src => src.Coupon != null ? src.Coupon.PromotionCode : null)
            .Map(dest => dest.UsedTime, src => src.CreationTime);
    }
}
