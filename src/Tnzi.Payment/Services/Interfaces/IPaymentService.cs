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
    /// off-session 自动扣款（后台续费/试用转正调用）。三种结果：
    /// 渠道当场收款 → 落地一笔 Succeeded 支付并发布 <see cref="Events.PaymentCompletedEvent"/>，返回成功、<c>Status = Succeeded</c>；
    /// 渠道明确拒付 → 落地一笔 Failed 支付并发布 <see cref="Events.PaymentFailedEvent"/>，返回失败；
    /// ★ <b>结果未落定</b>（渠道回报处理中，或调用异常 / 渠道返回失败、请求是否到达渠道无从得知）→ 支付保持 Processing、
    /// <b>不发任何事件</b>，返回成功、<c>Status = Processing</c>：钱可能还在路上，调用方不能当它已付，也不能换一笔再扣，
    /// 等渠道回调落定，或到 <c>ExpireTime</c> 由过期清扫核实后发完成 / 过期事件。
    /// ★ 例外：建单之前就被拒绝（金额非正、无支付方式、渠道不可用或不支持、计税失败）时<b>没有支付记录也没有事件</b>，
    /// 返回的失败带 <c>ErrorCode = </c><see cref="ErrorCodes.PaymentOffSessionNotAttempted"/>，调用方须自己收口。
    /// </summary>
    Task<Result<PaymentDto>> ChargeOffSessionAsync(OffSessionChargeDto request, CancellationToken cancellationToken = default);
}
