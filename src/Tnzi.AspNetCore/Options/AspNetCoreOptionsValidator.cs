
namespace Tnzi.AspNetCore.Options;

/// <summary>
/// AspNetCore配置验证器
/// </summary>
public class AspNetCoreOptionsValidator : OptionsValidatorBase<AspNetCoreOptions>
{
    /// <summary>
    /// 验证配置选项
    /// </summary>
    protected override void ValidateOptions(AspNetCoreOptions options, List<string> errors)
    {
        // 验证HTTP加密配置
        if (options.HttpEncrypt?.Enabled == true)
        {
            if (string.IsNullOrEmpty(options.HttpEncrypt.HostPublicKey))
                errors.Add("HttpEncrypt.HostPublicKey is required when HttpEncrypt.Enabled is true.");

            if (string.IsNullOrEmpty(options.HttpEncrypt.HostPrivateKey))
                errors.Add("HttpEncrypt.HostPrivateKey is required when HttpEncrypt.Enabled is true.");
        }

        // 验证请求验证配置
        if (options.RequestValidation?.Enabled == true)
        {
            if (options.RequestValidation.RequireSignature &&
                string.IsNullOrEmpty(options.RequestValidation.SignatureSecretKey))
            {
                errors.Add("RequestValidation.SignatureSecretKey is required when RequestValidation.RequireSignature is true.");
            }

            if (options.RequestValidation.TimestampWindowSeconds <= 0)
            {
                errors.Add("RequestValidation.TimestampWindowSeconds must be greater than 0.");
            }

            if (options.RequestValidation.NonceExpirationSeconds <= 0)
            {
                errors.Add("RequestValidation.NonceExpirationSeconds must be greater than 0.");
            }
        }

        // 验证限流配置
        if (options.RateLimit?.Enabled == true)
        {
            if (options.RateLimit.DefaultLimit <= 0)
            {
                errors.Add("RateLimit.DefaultLimit must be greater than 0.");
            }

            if (options.RateLimit.DefaultWindowSeconds <= 0)
            {
                errors.Add("RateLimit.DefaultWindowSeconds must be greater than 0.");
            }

            if (options.RateLimit.ByIp != null)
            {
                if (options.RateLimit.ByIp.Limit <= 0)
                    errors.Add("RateLimit.ByIp.Limit must be greater than 0.");
                if (options.RateLimit.ByIp.WindowSeconds <= 0)
                    errors.Add("RateLimit.ByIp.WindowSeconds must be greater than 0.");
            }

            if (options.RateLimit.ByUser != null)
            {
                if (options.RateLimit.ByUser.Limit <= 0)
                    errors.Add("RateLimit.ByUser.Limit must be greater than 0.");
                if (options.RateLimit.ByUser.WindowSeconds <= 0)
                    errors.Add("RateLimit.ByUser.WindowSeconds must be greater than 0.");
            }

            if (options.RateLimit.ByPath != null)
            {
                foreach (var pathRule in options.RateLimit.ByPath)
                {
                    if (pathRule.Value.Limit <= 0)
                        errors.Add($"RateLimit.ByPath[\"{pathRule.Key}\"].Limit must be greater than 0.");
                    if (pathRule.Value.WindowSeconds <= 0)
                        errors.Add($"RateLimit.ByPath[\"{pathRule.Key}\"].WindowSeconds must be greater than 0.");
                }
            }
        }

    }
}
