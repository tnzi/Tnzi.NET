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

        // 测试渠道（NullProvider）仅在显式开启时可用，防止生产环境无实际收款即"支付成功"
        if (string.Equals(provider.ChannelCode, "Null", StringComparison.OrdinalIgnoreCase))
        {
            if (_paymentOptions.CurrentValue.AllowTestProvider)
                return provider;

            _logger.LogWarning("Test payment channel 'Null' is disabled. Set Payment:AllowTestProvider=true to enable (non-production only).");
            return null;
        }

        var channelOptions = _paymentOptions.CurrentValue.Channels
            .FirstOrDefault(x => string.Equals(x.Key, provider.ChannelCode, StringComparison.OrdinalIgnoreCase))
            .Value;

        if (channelOptions?.Enabled != true)
        {
            _logger.LogWarning("Payment channel '{ChannelCode}' is not enabled.", provider.ChannelCode);
            return null;
        }

        return provider;
    }

    public IEnumerable<IPaymentProvider> GetEnabledProviders()
    {
        return _providers.Values
            .Where(x => GetProvider(x.ChannelCode) != null)
            .ToList();
    }
}
