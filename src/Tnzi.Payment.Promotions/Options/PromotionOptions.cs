namespace Tnzi.Payment.Promotions.Options;

/// <summary>
/// 促销配置选项。配置路径：<c>Payment:Promotion</c>。
/// </summary>
/// <remarks>
/// 节路径与拆分前<b>一字不变</b>：这个类一直带着绝对路径的 <c>[ConfigSection]</c>，
/// 拆分前由父模块的 <c>PaymentModule</c> 绑（那时它同时还是 <c>PaymentOptions.Promotion</c>
/// 嵌套属性，而<b>没有任何代码读过那个嵌套属性</b>），现在由
/// <see cref="PaymentPromotionsModule"/> 自己绑。运维手里的 appsettings.json 不需要改一个字符。
///
/// 随模块走还顺手修准了一件事：配置中心的分组是从<b>已加载模块的程序集</b>扫出来的，
/// 类留在父模块时，不做促销的宿主也会看到一个 "Promotion" 分组，
/// 改里面任何一个字段都不生效 —— 渲染出来却控制不了任何东西的设置项，比没有这一项更糟。
/// </remarks>
[ConfigSection("Payment:Promotion")]
[RuntimeSettingGroup(Key = "payment-promotion", Module = "Payment", DisplayName = "Promotion",
    I18nKey = "admin.modules.system.settings.groups.paymentPromotion",
    Icon = "mdi:tag-outline", Order = 530)]
public class PromotionOptions
{
    /// <summary>
    /// 每用户单张优惠券的默认使用次数上限：促销未单独设置 PerUserUsageLimit 时用它兜底。
    /// </summary>
    [RuntimeSetting(Label = "Max Coupon Usage Per User", I18n = "admin.modules.system.settings.fields.paymentMaxCouponUsagePerUser",
        Type = SettingFieldType.Int, Min = 1,
        Description = "Fallback per-user usage cap for promotions that do not set their own limit")]
    public int MaxCouponUsagePerUser { get; set; } = 5;

    /// <summary>
    /// 是否启用Stripe优惠券同步
    /// </summary>
    [RuntimeSetting(Label = "Enable Stripe Coupon Sync", I18n = "admin.modules.system.settings.fields.paymentEnableStripeCouponSync",
        Type = SettingFieldType.Boolean,
        Description = "Sync promotions to Stripe as coupons")]
    public bool EnableStripeCouponSync { get; set; } = true;
}
