namespace Tnzi.Payment.Providers;

/// <summary>
/// 支付渠道工厂实现
/// </summary>
public class PaymentProviderFactory : IPaymentProviderFactory
{
    private readonly IReadOnlyDictionary<string, IPaymentProvider> _providers;
    private readonly IOptionsMonitor<PaymentOptions> _paymentOptions;
    private readonly ILogger<PaymentProviderFactory> _logger;

    public PaymentProviderFactory(
        IEnumerable<IPaymentProvider> providers,
        IOptionsMonitor<PaymentOptions> paymentOptions,
        ILogger<PaymentProviderFactory> logger)
    {
        Check.NotNull(providers);
        _paymentOptions = Check.NotNull(paymentOptions);
        _logger = Check.NotNull(logger);
        _providers = providers.ToDictionary(x => x.ChannelCode, StringComparer.OrdinalIgnoreCase);
    }

    public IPaymentProvider? GetProvider(string channelCode)
    {
        if (string.IsNullOrWhiteSpace(channelCode))
            return null;

        if (!_providers.TryGetValue(channelCode, out var provider))
            return null;

        if (IsChannelEnabled(_paymentOptions.CurrentValue, provider.ChannelCode))
            return provider;

        // 测试渠道走的是另一个开关，提示要指对
        if (IsTestChannel(provider.ChannelCode))
            _logger.LogWarning("Test payment channel 'Null' is disabled. Set Payment:AllowTestProvider=true to enable (non-production only).");
        else
            _logger.LogWarning("Payment channel '{ChannelCode}' is not enabled.", provider.ChannelCode);

        return null;
    }

    /// <summary>
    /// 渠道启用规则的唯一真值源：测试渠道看 <see cref="PaymentOptions.AllowTestProvider"/>，
    /// 其余看 <c>Payment:Channels:{code}:Enabled</c>（键不区分大小写）。
    /// </summary>
    /// <remarks>
    /// <see cref="GetProvider"/> 与 <c>PaymentModule</c> 的启动诊断共用这一条。启动诊断不能直接调
    /// <see cref="GetProvider"/>：它每判一次不可用就记一条 Warning，而「一个渠道都没启用」的部署
    /// 在启动期应该只收到一条 Information，不该先被几条 Warning 轰一遍。
    /// </remarks>
    public static bool IsChannelEnabled(PaymentOptions options, string channelCode)
    {
        Check.NotNull(options);

        if (IsTestChannel(channelCode))
            return options.AllowTestProvider;

        var channelOptions = options.Channels
            .FirstOrDefault(x => string.Equals(x.Key, channelCode, StringComparison.OrdinalIgnoreCase))
            .Value;

        return channelOptions?.Enabled == true;
    }

    // 测试渠道（NullProvider）仅在显式开启时可用，防止生产环境无实际收款即"支付成功"
    private static bool IsTestChannel(string channelCode)
        => string.Equals(channelCode, PaymentConstants.NullChannelCode, StringComparison.OrdinalIgnoreCase);

    public IEnumerable<IPaymentProvider> GetEnabledProviders()
    {
        return _providers.Values
            .Where(x => GetProvider(x.ChannelCode) != null)
            .ToList();
    }
}
