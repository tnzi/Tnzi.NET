namespace Tnzi.Payment.Services;

/// <summary>
/// 支付服务接口
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// 创建支付订单。会依次应用优惠券折扣与税额，落地的 <c>PayableAmount</c> 即向渠道收取的金额。
    /// </summary>
    Task<Result<PaymentOrderResultDto>> CreatePaymentAsync(CreatePaymentDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取支付信息
    /// </summary>
    Task<Result<PaymentDto>> GetPaymentAsync(string tradeNo, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取支付列表
    /// </summary>
    Task<Result<IPagedList<PaymentDto>>> GetPaymentListAsync(PaymentQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 关闭支付订单
    /// </summary>
    Task<Result> ClosePaymentAsync(string tradeNo, string? reason = null, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 处理支付回调
    /// </summary>
    Task<Result> HandleCallbackAsync(PaymentCallbackDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取支付参数字段（含渠道客户端密钥，用于收银台页面刷新后恢复支付）
    /// </summary>
    Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 同步订单状态
    /// </summary>
    Task<Result> SyncOrderAsync(string tradeNo, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 手动确认线下收款（管理端调用）。仅适用于线下渠道，需登记收款凭证。
    /// </summary>
    Task<Result<PaymentDto>> ConfirmOfflinePaymentAsync(string tradeNo, ConfirmOfflinePaymentDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 自动关闭过期支付（后台任务调用）
    /// </summary>
    Task<Result<int>> CloseExpiredPaymentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// off-session 自动扣款（后台续费/试用转正调用）。
    /// 成功时落地一笔 Succeeded 支付并发布 <see cref="Events.PaymentCompletedEvent"/>；
    /// 失败时落地一笔 Failed 支付并发布 <see cref="Events.PaymentFailedEvent"/>，由订阅状态机据此推进/降级。
    /// </summary>
    Task<Result<PaymentDto>> ChargeOffSessionAsync(OffSessionChargeDto request, CancellationToken cancellationToken = default);
}
