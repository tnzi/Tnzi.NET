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
    /// 查询渠道侧的支付状态（只读：不在渠道侧做任何会动钱的事）。这是渠道唯一的支付状态查询契约：
    /// 关单 / 过期清扫前的渠道核实与手动同步都经它读渠道状态。
    /// </summary>
    /// <remarks>
    /// 传入的是 <c>ExternalTradeNo ?? TradeNo</c>：渠道已回报自己的参考号时是那个号，否则是本地流水号。
    /// </remarks>
    Task<Result<PaymentProviderQueryResult>> SyncOrderAsync(string tradeNo);

    /// <summary>
    /// 付款人在渠道侧确认之后把这笔付款收下来，再回报此刻的状态。入参与结果与 <see cref="SyncOrderAsync"/> 同口径。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 有的渠道在付款人确认之后还要商户再做一步才收得到钱：PayPal 的 <c>intent=CAPTURE</c> 订单被批准后停在
    /// <c>APPROVED</c>，等商户调用 capture，不做这一步订单到期作废，付款人以为付了而商户一分钱没收到。
    /// 这类渠道在这里做那一步，且必须幂等（同一笔重复调用不会收两次）；付款人确认即收款的渠道用默认实现，
    /// 就是一次 <see cref="SyncOrderAsync"/>。
    /// </para>
    /// <para>
    /// 服务层只在「付款人要付」的路径上调用它：手动同步一笔还没结束的支付（付款人从渠道回到站点时前端调的那个），
    /// 以及回调带着 <see cref="PaymentProviderCallbackResult.RequiresFinalization"/>。本地关单 / 过期清扫、
    /// 已经结束的支付<b>绝不</b>调用它，只读渠道状态：替一张正在被关掉的单收钱，等于向一个刚取消的人扣款。
    /// </para>
    /// </remarks>
    Task<Result<PaymentProviderQueryResult>> FinalizePaymentAsync(string tradeNo) => SyncOrderAsync(tradeNo);

    /// <summary>
    /// 获取支付参数
    /// </summary>
    Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo);

    /// <summary>
    /// 是否能作废渠道侧的支付意图（默认 false）。为 true 的渠道在本地关单 / 过期前会先被调用
    /// <see cref="CancelPaymentAsync"/>；为 false 的渠道，服务层退回「先查渠道侧状态，只在渠道侧未支付时本地关单」。
    /// </summary>
    bool SupportsPaymentCancellation => false;

    /// <summary>
    /// 作废渠道侧的支付意图，让付款人不能再对这张单付款。本地关单 / 过期清扫在改写本地状态之前调用它：
    /// 只在本地关掉，渠道侧的意图原样活着，付款人正开着收银台时照样付得进去，而钱到账的回调会撞上
    /// 本地终态被幂等守卫吞掉 —— 没有事件、没有告警、没有退款线索。
    /// </summary>
    /// <remarks>
    /// 传入的是 <c>ExternalTradeNo ?? TradeNo</c>（与 <see cref="SyncOrderAsync"/> 同口径）。
    /// 渠道侧已不存在 / 已作废时必须视为成功（幂等）；**已经付掉时必须失败**，服务层随后查渠道状态把它记成功
    /// 而不是关单。默认实现返回"不支持"，服务层据 <see cref="SupportsPaymentCancellation"/> 判断而不据此返回值。
    /// </remarks>
    Task<Result> CancelPaymentAsync(string tradeNo)
        => Task.FromResult(Result.Failure(ErrorCodes.PaymentChannelCancelNotSupported, 400));

    /// <summary>
    /// off-session 自动扣款（后台续费/试用转正用，使用渠道侧已保存的支付方式无人值守扣款）。
    /// 默认实现返回"不支持"，渠道按需覆写（Stripe；PayPal 需开启 <c>Payment:PayPal:EnableVault</c>）。
    /// </summary>
    /// <remarks>
    /// 服务层按结果分三类处理，实现方必须如实回报：
    /// 成功且 <c>Status = Succeeded</c> 是已收款；成功且 <c>Status = Failed</c> / <c>Cancelled</c> 是<b>确定没收到钱</b>（拒付），
    /// 订阅随即降级并换一笔重试；成功且其它状态是「渠道收下了、还在处理」，等回调落定。
    /// 返回失败（或抛异常）一律按<b>结果未知</b>处理：请求可能已经到了渠道，服务层不会当成拒付再扣一笔。
    /// 确定请求没有发出去（拿不到访问令牌之类）时返回失败并带
    /// <c>ErrorCode = </c><see cref="ErrorCodes.PaymentOffSessionNotAttempted"/>，服务层才把它当成确定的失败。
    /// </remarks>
    Task<Result<PaymentProviderChargeResult>> ChargeOffSessionAsync(PaymentProviderChargeDto input)
        => Task.FromResult(Result.Failure<PaymentProviderChargeResult>(ErrorCodes.PaymentOffSessionNotSupported, 400));

    /// <summary>
    /// 是否支持 off-session 自动扣款
    /// </summary>
    bool SupportsOffSessionCharge => false;

    /// <summary>
    /// 浏览器端拉起渠道收银组件要用的公开密钥（Stripe 的 publishable key）。默认 null：渠道不需要，或没配置。
    /// </summary>
    /// <remarks>
    /// 只能是设计上就公开给浏览器的值。经 <c>GET /payment-methods/client-config</c> 下发给任何已登录用户，
    /// 绝不能把密钥类配置（secret key、webhook secret）从这里漏出去。
    /// </remarks>
    string? PublishableKey => null;

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
