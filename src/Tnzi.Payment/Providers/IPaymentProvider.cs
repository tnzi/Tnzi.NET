namespace Tnzi.Payment.Providers;

/// <summary>
/// 支付渠道接口
/// </summary>
public interface IPaymentProvider
{
    /// <summary>
    /// 渠道代码
    /// </summary>
    string ChannelCode { get; }

    /// <summary>
    /// 渠道名称
    /// </summary>
    string ChannelName { get; }

    /// <summary>
    /// 是否支持该支付方式
    /// </summary>
    bool IsSupported(PaymentMethod method);

    /// <summary>
    /// 创建支付订单
    /// </summary>
    Task<Result<PaymentProviderOrderResult>> CreatePaymentAsync(PaymentProviderCreateDto input);

    /// <summary>
    /// 查询支付状态
    /// </summary>
    Task<Result<PaymentProviderQueryResult>> QueryPaymentAsync(string tradeNo);

    /// <summary>
    /// 发起退款
    /// </summary>
    Task<Result<PaymentProviderRefundResult>> RefundAsync(PaymentProviderRefundDto input);

    /// <summary>
    /// 查询退款状态
    /// </summary>
    Task<Result<PaymentProviderRefundQueryResult>> QueryRefundAsync(string refundNo);

    /// <summary>
    /// 处理回调
    /// </summary>
    Task<Result<PaymentProviderCallbackResult>> HandleCallbackAsync(IDictionary<string, string> parameters);

    /// <summary>
    /// 验证签名
    /// </summary>
    Task<bool> VerifySignatureAsync(IDictionary<string, string> parameters);

    /// <summary>
    /// 同步订单状态
    /// </summary>
    Task<Result<PaymentProviderQueryResult>> SyncOrderAsync(string tradeNo);

    /// <summary>
    /// 获取支付参数
    /// </summary>
    Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo);

    /// <summary>
    /// 更新支付方式
    /// </summary>
    Task<Result> UpdatePaymentMethodAsync(string subscriptionNo, string paymentMethodId);
}

/// <summary>
/// 支付渠道工厂接口
/// </summary>
public interface IPaymentProviderFactory
{
    /// <summary>
    /// 获取支付渠道
    /// </summary>
    IPaymentProvider? GetProvider(string channelCode);

    /// <summary>
    /// 获取所有已启用的渠道
    /// </summary>
    IEnumerable<IPaymentProvider> GetEnabledProviders();
}
