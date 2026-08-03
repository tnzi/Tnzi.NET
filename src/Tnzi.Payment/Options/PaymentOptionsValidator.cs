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

        // 验证订阅配置
        if (options.Subscription.AutoRenewalReminderDays < 0)
            errors.Add("Subscription:AutoRenewalReminderDays cannot be negative.");

        if (options.Subscription.GracePeriodDays < 0)
            errors.Add("Subscription:GracePeriodDays cannot be negative.");

        if (options.Subscription.MaxRetryCount < 0)
            errors.Add("Subscription:MaxRetryCount cannot be negative.");

        if (options.Subscription.DefaultTrialDays < 0)
            errors.Add("Subscription:DefaultTrialDays cannot be negative.");

        if (options.Subscription.MaxPauseDays < 0)
            errors.Add("Subscription:MaxPauseDays cannot be negative.");

        // 验证发票配置
        if (options.Invoice.Enabled && string.IsNullOrWhiteSpace(options.Invoice.DefaultTemplate))
            errors.Add("Invoice:DefaultTemplate is required when invoice is enabled.");

        // 验证税务配置（税率是百分数，超过 100 几乎必然是把 0-1 的小数写成了百分比或反之）
        if (options.Tax.Enabled && (options.Tax.DefaultTaxRate < 0 || options.Tax.DefaultTaxRate > 100))
            errors.Add("Tax:DefaultTaxRate must be between 0 and 100 (percentage) when tax is enabled.");

        // 验证促销配置
        if (options.Promotion.MaxCouponUsagePerUser <= 0)
            errors.Add("Promotion:MaxCouponUsagePerUser must be greater than 0.");
    }
}

/// <summary>
/// Stripe 配置验证器
/// </summary>
public class StripeOptionsValidator : OptionsValidatorBase<StripeOptions>
{
    protected override void ValidateOptions(StripeOptions options, List<string> errors)
    {
        if (!options.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(options.SecretKey))
            errors.Add("Stripe:SecretKey is required when Stripe is enabled.");

        if (string.IsNullOrWhiteSpace(options.PublishableKey))
            errors.Add("Stripe:PublishableKey is required when Stripe is enabled.");

        if (string.IsNullOrWhiteSpace(options.WebhookSecret))
            errors.Add("Stripe:WebhookSecret is required when Stripe is enabled.");

        if (string.IsNullOrWhiteSpace(options.Currency))
            errors.Add("Stripe:Currency is required when Stripe is enabled.");

        if (options.ConnectEnabled && string.IsNullOrWhiteSpace(options.ConnectClientId))
            errors.Add("Stripe:ConnectClientId is required when Connect is enabled.");
    }
}

/// <summary>
/// PayPal 配置验证器
/// </summary>
public class PayPalOptionsValidator : OptionsValidatorBase<PayPalOptions>
{
    protected override void ValidateOptions(PayPalOptions options, List<string> errors)
    {
        if (!options.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(options.ClientId))
            errors.Add("PayPal:ClientId is required when PayPal is enabled.");

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            errors.Add("PayPal:ClientSecret is required when PayPal is enabled.");

        if (string.IsNullOrWhiteSpace(options.WebhookId))
            errors.Add("PayPal:WebhookId is required when PayPal is enabled.");

        if (!string.Equals(options.Mode, "sandbox", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.Mode, "live", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("PayPal:Mode must be either 'sandbox' or 'live'.");
        }

        if (string.IsNullOrWhiteSpace(options.Currency))
            errors.Add("PayPal:Currency is required when PayPal is enabled.");

        if (!options.EnableVault)
            return;

        // 绑定 PayPal 账户必须把付款人送到 PayPal 授权再跳回来。没有回跳地址这条链路根本走不完，
        // 而失败点在"用户已经在 PayPal 点了同意之后"——那时才发现配置缺失代价最大。
        if (string.IsNullOrWhiteSpace(options.VaultReturnUrl) && string.IsNullOrWhiteSpace(options.ReturnUrl))
            errors.Add("PayPal:VaultReturnUrl (or PayPal:ReturnUrl) is required when PayPal:EnableVault is true.");

        if (string.IsNullOrWhiteSpace(options.VaultUsagePattern))
            errors.Add("PayPal:VaultUsagePattern is required when PayPal:EnableVault is true.");
    }
}
