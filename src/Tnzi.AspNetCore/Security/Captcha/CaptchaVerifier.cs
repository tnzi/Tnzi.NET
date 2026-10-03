namespace Tnzi.AspNetCore.Security;

/// <summary>
/// <see cref="ICaptchaVerifier"/> 默认实现：按 <c>AspNetCore:Captcha:Provider</c> 从注册的
/// <see cref="ICaptchaProvider"/> 里挑一家，套上跨提供商的策略。
/// </summary>
public class CaptchaVerifier : ICaptchaVerifier
{
    /// <summary>
    /// 令牌长度上限（字符）。超过的一律拒绝，不交给提供商。
    /// </summary>
    /// <remarks>
    /// 令牌是匿名端点上完全由客户端决定的输入，托管型提供商会把它原样转发给外部验证服务。
    /// 已知提供商的令牌都在几 KB 以内（reCAPTCHA / Turnstile 约 2 KB，hCaptcha 稍长，Altcha 载荷几百字节），
    /// 8 KB 给足余量；再长的只可能是构造出来的，没必要让它出站、进缓存键或进日志。
    /// </remarks>
    public const int MaxTokenLength = 8 * 1024;

    private readonly CaptchaVerifierOptions _options;
    private readonly IReadOnlyList<ICaptchaProvider> _providers;
    private readonly IScopedContext? _scopedContext;
    private readonly ILogger<CaptchaVerifier> _logger;
    private readonly Lazy<ICaptchaProvider?> _active;

    /// <summary>
    /// 初始化验证器。
    /// </summary>
    public CaptchaVerifier(
        IOptionsSnapshot<CaptchaVerifierOptions> options,
        IEnumerable<ICaptchaProvider> providers,
        ILogger<CaptchaVerifier> logger,
        IScopedContext? scopedContext = null)
    {
        _options = Check.NotNull(options).Value;
        _providers = Check.NotNull(providers).ToList();
        _logger = Check.NotNull(logger);
        _scopedContext = scopedContext;
        _active = new Lazy<ICaptchaProvider?>(ResolveActive);
    }

    /// <inheritdoc />
    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.Provider);

    /// <inheritdoc />
    public string? ProviderName => IsEnabled ? _options.Provider!.Trim() : null;

    /// <inheritdoc />
    public async Task<CaptchaVerification> VerifyAsync(string? token, string purpose, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(purpose);

        if (!IsEnabled)
            return CaptchaVerification.NotEnabled();

        var provider = _active.Value;
        if (provider == null)
        {
            // 启动期 EnsureConfigured 已经拦下；能走到这里只剩「配置在运行期被改成了一个没注册的名字」。
            _logger.LogError("Captcha provider '{Provider}' is configured but not registered; rejecting the request.", ProviderName);
            return CaptchaVerification.Fail(ProviderName!, CaptchaFailure.ProviderNotRegistered);
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogDebug("Captcha token missing for purpose {Purpose}.", purpose);
            return CaptchaVerification.Fail(provider.Name, CaptchaFailure.MissingToken);
        }

        if (token.Length > MaxTokenLength)
        {
            _logger.LogInformation("Captcha token for purpose {Purpose} is {Length} characters, above the {Max} limit; rejecting without contacting {Provider}.",
                purpose, token.Length, MaxTokenLength, provider.Name);
            return CaptchaVerification.Fail(provider.Name, CaptchaFailure.Rejected, "Token too long");
        }

        var request = new CaptchaVerificationRequest(token, purpose, _scopedContext?.ClientIpAddress);
        var verification = await provider.VerifyAsync(request, cancellationToken);

        if (verification.Failure == CaptchaFailure.VerifierUnavailable && _options.OnVerifierUnavailable == CaptchaUnavailablePolicy.Allow)
        {
            // 放行但必须留痕：这段时间里的验证码等于没有，运维要能在日志里看见它开始与结束。
            _logger.LogWarning(
                "Captcha provider {Provider} is unavailable ({Detail}); allowing the request for purpose {Purpose} because OnVerifierUnavailable=Allow.",
                provider.Name, verification.Detail, purpose);
            return CaptchaVerification.Pass(provider.Name, $"Verifier unavailable, allowed by policy: {verification.Detail}");
        }

        if (!verification.Passed)
        {
            _logger.LogInformation(
                "Captcha rejected by {Provider} for purpose {Purpose}: {Failure} {Detail}",
                provider.Name, purpose, verification.Failure, verification.Detail);
        }

        return verification;
    }

    /// <inheritdoc />
    public CaptchaClientConfigDto GetClientConfig()
    {
        if (!IsEnabled)
            return new CaptchaClientConfigDto { Enabled = false };

        var provider = _active.Value;
        var config = provider?.GetClientConfig() ?? new CaptchaClientConfigDto();
        config.Enabled = true;
        config.Provider = provider?.Name ?? ProviderName;
        return config;
    }

    /// <inheritdoc />
    public void EnsureConfigured()
    {
        if (!IsEnabled)
            return;

        if (_active.Value != null)
            return;

        var registered = _providers.Count == 0 ? "(none)" : string.Join(", ", _providers.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        throw new ConfigurationException(
            "AspNetCore:Captcha:Provider",
            $"Captcha provider '{ProviderName}' is configured but no ICaptchaProvider with that name is registered. " +
            $"Registered providers: {registered}. Load the module that ships it (Tnzi.Identity for 'image', Tnzi.Imaging for 'sliding'), " +
            "register your own with services.AddCaptchaProvider<T>(), or clear the setting to disable captcha verification.");
    }

    private ICaptchaProvider? ResolveActive()
    {
        var name = ProviderName;
        if (name == null) return null;

        var matches = _providers.Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 1)
        {
            // 两个实现抢同一个名字，取后注册的（消费方覆盖框架内置），并把这件事说出来。
            _logger.LogWarning("Multiple ICaptchaProvider registrations named '{Provider}'; using the last one ({Type}).", name, matches[^1].GetType().Name);
        }
        return matches.Count == 0 ? null : matches[^1];
    }
}
