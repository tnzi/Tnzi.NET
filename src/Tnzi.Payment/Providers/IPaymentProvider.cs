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
    /// off-session 自动扣款（后台续费/试用转正用，使用渠道侧已保存的支付方式无人值守扣款）。
    /// 默认实现返回"不支持"，渠道按需覆写（Stripe；PayPal 需开启 <c>Payment:PayPal:EnableVault</c>）。
    /// </summary>
    Task<Result<PaymentProviderChargeResult>> ChargeOffSessionAsync(PaymentProviderChargeDto input)
        => Task.FromResult(Result.Failure<PaymentProviderChargeResult>(ErrorCodes.PaymentOffSessionNotSupported, 400));

    /// <summary>
    /// 是否支持 off-session 自动扣款
    /// </summary>
    bool SupportsOffSessionCharge => false;

    /// <summary>
    /// 是否支持保存支付方式（绑卡），即能否为后续 off-session 扣款留存可复用的凭据
    /// </summary>
    bool SupportsPaymentMethodStorage => false;

    /// <summary>
    /// 创建绑卡会话：向渠道申请一次“只收集支付方式、不收款”的会话。
    /// 结果有两种形态——内嵌式渠道回 <c>ClientSecret</c>（前端就地收集，含 3DS），
    /// 重定向式渠道回 <c>ApprovalUrl</c>（把付款人整页送去渠道授权）。
    /// 两者完成后都调用 <see cref="ResolvePaymentMethodAsync"/> 登记为可复用的支付方式。
    /// </summary>
    Task<Result<PaymentProviderSetupResult>> CreateSetupSessionAsync(PaymentProviderSetupDto input)
        => Task.FromResult(Result.Failure<PaymentProviderSetupResult>(ErrorCodes.PaymentMethodStorageNotSupported, 400));

    /// <summary>
    /// 校验并规范化渠道侧支付方式：确认该支付方式存在、归属指定客户（必要时完成绑定），
    /// 返回可长期保存的引用与展示信息。这是“绑卡真正落库”的唯一入口。
    /// </summary>
    Task<Result<PaymentProviderPaymentMethodResult>> ResolvePaymentMethodAsync(PaymentProviderResolveMethodDto input)
        => Task.FromResult(Result.Failure<PaymentProviderPaymentMethodResult>(ErrorCodes.PaymentMethodStorageNotSupported, 400));

    /// <summary>
    /// 解绑渠道侧支付方式（用户删除已保存的卡）。渠道侧已不存在时应视为成功（幂等）。
    /// </summary>
    Task<Result> DetachPaymentMethodAsync(PaymentProviderResolveMethodDto input)
        => Task.FromResult(Result.Failure(ErrorCodes.PaymentMethodStorageNotSupported, 400));
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
