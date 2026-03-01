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
}
