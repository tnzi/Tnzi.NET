namespace Tnzi.Payment.Options;

/// <summary>
/// Payment 配置验证器
/// </summary>
public class PaymentOptionsValidator : OptionsValidatorBase<PaymentOptions>
{
    protected override void ValidateOptions(PaymentOptions options, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.DefaultCurrency))
            errors.Add("DefaultCurrency is required.");

        if (string.IsNullOrWhiteSpace(options.DefaultChannelCode))
            errors.Add("DefaultChannelCode is required.");

        if (options.AutoCloseExpireMinutes <= 0)
            errors.Add("AutoCloseExpireMinutes must be greater than 0.");

        if (options.BackgroundTaskIntervalMinutes <= 0)
            errors.Add("BackgroundTaskIntervalMinutes must be greater than 0.");

        if (options.BillingLockMinutes <= 0)
            errors.Add("BillingLockMinutes must be greater than 0.");

        if (options.RefundReconcileLookbackDays <= 0)
            errors.Add("RefundReconcileLookbackDays must be greater than 0.");

        if (options.MaxRefundAmountPerDay < 0)
            errors.Add("MaxRefundAmountPerDay cannot be negative.");

        if (options.RefundApprovalThreshold < 0)
            errors.Add("RefundApprovalThreshold cannot be negative.");

        // 验证税务配置（税率是百分数，超过 100 几乎必然是把 0-1 的小数写成了百分比或反之）
        if (options.Tax.Enabled && (options.Tax.DefaultTaxRate < 0 || options.Tax.DefaultTaxRate > 100))
            errors.Add("Tax:DefaultTaxRate must be between 0 and 100 (percentage) when tax is enabled.");
    }
}

// 渠道凭据的验证器随各自的 Options 类搬去了可选子模块：
// StripeOptionsValidator 在 Tnzi.Payment.Stripe，PayPalOptionsValidator 在 Tnzi.Payment.PayPal。
// 发票配置同理：InvoiceOptions 与 InvoiceOptionsValidator 在 Tnzi.Payment.Billing。
// 订阅配置同理：SubscriptionOptions 与 SubscriptionOptionsValidator 在 Tnzi.Payment.Subscriptions，
// 校验的还是同一个 Payment:Subscription 节（五条断言与文案逐字不变），
// 只是不加载那个包时不再校验一段没人读的配置。
// 促销配置同理：PromotionOptions 与 PromotionOptionsValidator 在 Tnzi.Payment.Promotions，
// 校验的还是同一个 Payment:Promotion 节（那一条断言与文案逐字不变）。
