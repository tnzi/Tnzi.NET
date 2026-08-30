namespace Tnzi.Payment.Promotions.Options;

/// <summary>
/// Payment:Promotion 配置验证器。
/// </summary>
/// <remarks>
/// 这一条断言与文案是从父模块的 <c>PaymentOptionsValidator</c> 里逐字搬来的
/// （那时它写作 <c>options.Promotion.MaxCouponUsagePerUser</c>）。跟着类一起搬的理由是：
/// 留在父模块就得留一条父 → 子的编译期引用，而校验一段没人读的配置本身也没有意义。
/// </remarks>
public class PromotionOptionsValidator : OptionsValidatorBase<PromotionOptions>
{
    protected override void ValidateOptions(PromotionOptions options, List<string> errors)
    {
        if (options.MaxCouponUsagePerUser <= 0)
            errors.Add("Promotion:MaxCouponUsagePerUser must be greater than 0.");
    }
}
