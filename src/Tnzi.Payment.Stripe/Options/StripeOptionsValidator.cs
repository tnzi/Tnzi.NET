namespace Tnzi.Payment.Stripe.Options;

/// <summary>
/// Stripe 配置验证器
/// </summary>
/// <remarks>
/// 只在 <see cref="StripeOptions.Enabled"/> 为 true 时校验：加载了本模块但没打算启用 Stripe
/// （例如同一份程序集同时部署到只走线下收款的环境）是正当状态，不该因此起不来。
/// </remarks>
public class StripeOptionsValidator : OptionsValidatorBase<StripeOptions>
{
    /// <inheritdoc />
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
