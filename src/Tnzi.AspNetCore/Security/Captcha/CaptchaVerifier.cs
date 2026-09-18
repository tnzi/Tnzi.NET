namespace Tnzi.AspNetCore.Security;

/// <summary>
/// <see cref="ICaptchaVerifier"/> 默认实现：按 <c>AspNetCore:Captcha:Provider</c> 从注册的
/// <see cref="ICaptchaProvider"/> 里挑一家，套上跨提供商的策略。
/// </summary>
public class CaptchaVerifier : ICaptchaVerifier
{
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
