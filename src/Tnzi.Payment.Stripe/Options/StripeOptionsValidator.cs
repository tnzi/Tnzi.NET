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
    /// <summary>本渠道自己写进 Stripe 元数据的键：应用标记不能占用它们。</summary>
    private static readonly string[] ReservedMetadataKeys = ["TradeNo", "BusinessOrderNo", "OffSession", "UserId", "BillingAccount"];

    /// <summary>Stripe 拒收的账单描述字符。</summary>
    private const string ForbiddenDescriptorCharacters = "<>\\'\"*";

    /// <inheritdoc />
    protected override void ValidateOptions(StripeOptions options, List<string> errors)
    {
        if (!options.Enabled)
            return;

        if (!string.IsNullOrWhiteSpace(options.MetadataStampValue))
        {
            if (string.IsNullOrWhiteSpace(options.MetadataStampKey))
                errors.Add("Stripe:MetadataStampKey is required when Stripe:MetadataStampValue is set.");
            else if (options.MetadataStampKey.Length > 40)
                errors.Add("Stripe:MetadataStampKey cannot exceed 40 characters (Stripe metadata key limit).");
            else if (ReservedMetadataKeys.Contains(options.MetadataStampKey, StringComparer.OrdinalIgnoreCase))
                errors.Add($"Stripe:MetadataStampKey cannot be '{options.MetadataStampKey}': the Stripe channel writes that key itself.");

            if (options.MetadataStampValue.Length > 500)
                errors.Add("Stripe:MetadataStampValue cannot exceed 500 characters (Stripe metadata value limit).");
        }

        if (!string.IsNullOrEmpty(options.StatementDescriptorSuffix))
        {
            var suffix = options.StatementDescriptorSuffix;
            if (suffix.Length > StripeOptions.StatementDescriptorMaxLength)
                errors.Add($"Stripe:StatementDescriptorSuffix cannot exceed {StripeOptions.StatementDescriptorMaxLength} characters.");
            if (!suffix.Any(char.IsLetter))
                errors.Add("Stripe:StatementDescriptorSuffix must contain at least one letter.");
            if (suffix.IndexOfAny(ForbiddenDescriptorCharacters.ToCharArray()) >= 0)
                errors.Add("Stripe:StatementDescriptorSuffix cannot contain any of < > \\ ' \" *.");
        }

        if (string.IsNullOrWhiteSpace(options.SecretKey))
            errors.Add("Stripe:SecretKey is required when Stripe is enabled.");

        if (string.IsNullOrWhiteSpace(options.WebhookSecret))
            errors.Add("Stripe:WebhookSecret is required when Stripe is enabled.");
    }
}
