namespace Tnzi.Payment.PayPal.Options;

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
