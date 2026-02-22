namespace Tnzi.Payment.Providers;

/// <summary>
/// 支付渠道工厂实现
/// </summary>
public class PaymentProviderFactory : IPaymentProviderFactory
{
    private readonly IReadOnlyDictionary<string, IPaymentProvider> _providers;
    private readonly IOptions<PaymentOptions> _paymentOptions;
    private readonly ILogger<PaymentProviderFactory> _logger;

    public PaymentProviderFactory(
        IEnumerable<IPaymentProvider> providers,
        IOptions<PaymentOptions> paymentOptions,
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

        // NullProvider 始终可用（测试用途）
        if (string.Equals(provider.ChannelCode, "Null", StringComparison.OrdinalIgnoreCase))
            return provider;

        var channelOptions = _paymentOptions.Value.Channels
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
