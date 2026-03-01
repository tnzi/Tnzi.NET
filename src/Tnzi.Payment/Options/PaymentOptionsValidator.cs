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

        if (options.AutoCloseExpireMinutes <= 0)
            errors.Add("AutoCloseExpireMinutes must be greater than 0.");

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

        // 验证发票配置
        if (options.Invoice.Enabled && string.IsNullOrWhiteSpace(options.Invoice.DefaultTemplate))
            errors.Add("Invoice:DefaultTemplate is required when invoice is enabled.");

        // 验证税务配置
        if (options.Tax.Enabled && options.Tax.DefaultTaxRate < 0)
            errors.Add("Tax:DefaultTaxRate cannot be negative when tax is enabled.");

        // 验证促销配置
        if (options.Promotion.DefaultFirstSubscriptionDiscount < 0 || options.Promotion.DefaultFirstSubscriptionDiscount > 1)
            errors.Add("Promotion:DefaultFirstSubscriptionDiscount must be between 0 and 1.");

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
    }
}
