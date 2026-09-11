
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
        // 验证 PathBase 格式
        if (!string.IsNullOrEmpty(options.PathBase))
        {
            if (!options.PathBase.StartsWith('/'))
                errors.Add("PathBase must start with '/' (e.g., '/myapp').");
            if (options.PathBase.EndsWith('/'))
                errors.Add("PathBase must not end with '/' (e.g., use '/myapp' not '/myapp/').");
        }

        // 验证受信代理声明
        ValidateTrustedProxies(options.TrustedProxies, errors);

        // 验证 CORS 策略
        ValidateCors(options.Cors, errors);

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
                    ValidateRateLimitAlgorithm(pathRule.Value.Algorithm, $"RateLimit.ByPath[\"{pathRule.Key}\"]", errors);
                }
            }

            // 验证各维度的限流算法
            if (options.RateLimit.ByIp != null)
                ValidateRateLimitAlgorithm(options.RateLimit.ByIp.Algorithm, "RateLimit.ByIp", errors);
            if (options.RateLimit.ByUser != null)
                ValidateRateLimitAlgorithm(options.RateLimit.ByUser.Algorithm, "RateLimit.ByUser", errors);
        }

    }

    /// <summary>
    /// 收集不阻止启动的告警。
    /// </summary>
    protected override void CollectWarnings(AspNetCoreOptions options, List<string> warnings)
    {
        var cors = options.Cors;
        if (cors?.Enabled == true && !cors.AllowAnyOrigin && (cors.WithOrigins is null or { Length: 0 }))
        {
            // 开着 CORS 但一个来源都没允许，与压根没开在浏览器那边完全一样：
            // 预检没有 Access-Control-Allow-Origin，请求被拦在浏览器里，服务端日志干干净净。
            // ★ 只告警不失败：一条「什么都不允许」的策略是合法的，
            //   而这大概率是 WithOrigins 忘了填 —— 那两件事只有部署方分得清。
            warnings.Add(
                "Cors.Enabled is true but no origin is allowed (set Cors.WithOrigins or Cors.AllowAnyOrigin); "
                + "cross-origin requests will be blocked by the browser exactly as if CORS were switched off.");
        }
    }

    /// <summary>
    /// 验证 CORS 策略。
    /// </summary>
    /// <remarks>
    /// ★★★ <strong>非法组合此前无人校验，而策略是惰性构建的</strong> ——
    /// 应用照常启动、健康检查照常通过，直到第一个跨域请求打进来才在
    /// <c>CorsPolicyBuilder</c> 里抛异常，于是<b>每一个跨域请求都是 500</b>。
    /// 这类失败在部署后很久才显形，而现场看起来像「后端挂了」不像「配置写错了」。
    /// </remarks>
    private static void ValidateCors(CorsOptions? cors, List<string> errors)
    {
        if (cors?.Enabled != true)
        {
            return;
        }

        if (cors.AllowAnyOrigin && cors.AllowCredentials)
        {
            // CORS 规范禁止 `Access-Control-Allow-Origin: *` 与凭据并存
            // （否则任何站点都能拿着受害者的 Cookie 读到响应）。
            errors.Add(
                "Cors.AllowAnyOrigin cannot be combined with Cors.AllowCredentials: the CORS specification "
                + "forbids sending credentials to a wildcard origin. List the origins in Cors.WithOrigins instead.");
        }

        if (cors is { AllowCredentials: true, DisallowCredentials: true })
        {
            errors.Add("Cors.AllowCredentials and Cors.DisallowCredentials cannot both be true.");
        }

        if (string.IsNullOrWhiteSpace(cors.PolicyName))
        {
            errors.Add("Cors.PolicyName is required when Cors.Enabled is true.");
        }
    }

    /// <summary>
    /// 验证受信代理声明。
    /// </summary>
    /// <remarks>
    /// 写错的地址<b>必须启动就报</b>：跳过它的后果是那一跳不再受信，
    /// 于是所有调用方共用代理的地址 —— 限流把所有人算进一个桶，
    /// 看起来像「限流太严」而不像「配置里有个拼错的地址」。
    /// </remarks>
    private static void ValidateTrustedProxies(TrustedProxyOptions? trusted, List<string> errors)
    {
        if (trusted == null)
        {
            return;
        }

        if (trusted.ForwardLimit is <= 0)
        {
            errors.Add("TrustedProxies.ForwardLimit must be greater than 0 (use null for no limit).");
        }

        foreach (var proxy in trusted.KnownProxies ?? [])
        {
            if (!string.IsNullOrWhiteSpace(proxy) && !IPAddress.TryParse(proxy.Trim(), out _))
            {
                errors.Add($"TrustedProxies.KnownProxies contains '{proxy}', which is not a valid IP address.");
            }
        }

        foreach (var network in trusted.KnownNetworks ?? [])
        {
            if (!string.IsNullOrWhiteSpace(network) && !IPNetwork.TryParse(network.Trim(), out _))
            {
                errors.Add(
                    $"TrustedProxies.KnownNetworks contains '{network}', "
                    + "which is not valid CIDR notation (e.g. '10.0.0.0/8').");
            }
        }
    }

    /// <summary>
    /// 验证限流算法是否已实现
    /// </summary>
    private static void ValidateRateLimitAlgorithm(RateLimitAlgorithm algorithm, string path, List<string> errors)
    {
        if (algorithm is RateLimitAlgorithm.TokenBucket or RateLimitAlgorithm.LeakyBucket)
        {
            errors.Add($"{path}.Algorithm '{algorithm}' is not yet implemented. Use FixedWindow or SlidingWindow instead.");
        }
    }
}
