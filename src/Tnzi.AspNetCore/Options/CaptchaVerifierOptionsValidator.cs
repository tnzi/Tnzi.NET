namespace Tnzi.AspNetCore.Options;

/// <summary>
/// <see cref="CaptchaVerifierOptions"/> 校验器。
/// </summary>
public class CaptchaVerifierOptionsValidator : OptionsValidatorBase<CaptchaVerifierOptions>
{
    /// <summary>需要站点密钥与服务端密钥的托管型提供商。</summary>
    internal static readonly string[] HostedProviders = ["recaptcha", "recaptcha-v3", "hcaptcha", "turnstile"];

    /// <inheritdoc />
    protected override void ValidateOptions(CaptchaVerifierOptions options, List<string> errors)
    {
        if (options.ScoreThreshold is < 0 or > 1)
            errors.Add("ScoreThreshold must be between 0 and 1.");

        if (options.TimeoutSeconds <= 0)
            errors.Add("TimeoutSeconds must be greater than 0.");

        if (options.Altcha.MaxNumber is <= 0 or > AltchaOptions.MaxNumberUpperBound)
            errors.Add($"Altcha.MaxNumber must be between 1 and {AltchaOptions.MaxNumberUpperBound}.");

        if (options.Altcha.ExpiresSeconds <= 0)
            errors.Add("Altcha.ExpiresSeconds must be greater than 0.");

        if (!string.IsNullOrWhiteSpace(options.VerifyUrl) && !Uri.TryCreate(options.VerifyUrl, UriKind.Absolute, out _))
            errors.Add("VerifyUrl must be an absolute URL.");

        var provider = options.Provider?.Trim();
        if (string.IsNullOrEmpty(provider))
            return;

        if (HostedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.SiteKey))
                errors.Add($"SiteKey is required when Provider is '{provider}'.");
            if (string.IsNullOrWhiteSpace(options.SecretKey))
                errors.Add($"SecretKey is required when Provider is '{provider}'.");
        }

        if (string.Equals(provider, "altcha", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.Altcha.HmacKey))
                errors.Add("Altcha.HmacKey is required when Provider is 'altcha'.");
            else if (options.Altcha.HmacKey.Length < 32)
                errors.Add("Altcha.HmacKey must be at least 32 characters.");
        }
    }
}
