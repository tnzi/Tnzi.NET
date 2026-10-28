namespace Tnzi.Payment.Services;

/// <summary>
/// 退款服务接口
/// </summary>
public interface IRefundService
{
    /// <summary>
    /// 申请退款
    /// </summary>
    Task<Result<RefundDto>> CreateRefundAsync(CreateRefundDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 审批退款
    /// </summary>
    Task<Result> ApproveRefundAsync(Guid refundId, ApproveRefundDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行退款
    /// </summary>
    Task<Result> ProcessRefundAsync(Guid refundId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消退款
    /// </summary>
    Task<Result> CancelRefundAsync(Guid refundId, string? reason = null, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取退款信息
    /// </summary>
    Task<Result<RefundDto>> GetRefundAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取退款列表
    /// </summary>
    Task<Result<IPagedList<RefundDto>>> GetRefundListAsync(RefundQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据支付单号获取退款列表
    /// </summary>
    Task<Result<List<RefundDto>>> GetRefundsByTradeNoAsync(string tradeNo, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 对账在途退款（后台任务调用）：回查渠道，把仍处于 Refunding 的退款推进到终态。
    /// </summary>
    /// <remarks>
    /// 渠道退款常常不是同步完成的（银行卡退回通常数日），
    /// 没有这条回收链路，本地账面会永远停在"退款中"或错误地停在"已成功"。
    /// </remarks>
    Task<Result<int>> ReconcilePendingRefundsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 记下渠道回报的退款（渠道回调调用，内部方法）：本模块发起的那几笔（带 <see cref="PaymentProviderExternalRefund.LocalRefundNo"/>）
    /// 只推进它们自己的状态；在渠道后台手工退的那几笔按渠道退款号幂等地各记一条 <see cref="RefundOrigin.Channel"/> 退款。
    /// 返回这次新记下或推进了状态的条数。调用方须已切到支付所属的租户。
    /// </summary>
    Task<Result<int>> RecordChannelRefundsAsync(Guid paymentId, IReadOnlyList<PaymentProviderExternalRefund> refunds, CancellationToken cancellationToken = default);
}
