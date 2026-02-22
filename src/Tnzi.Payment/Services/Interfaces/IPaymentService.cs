namespace Tnzi.Payment.Services;

/// <summary>
/// 支付服务接口
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// 创建支付订单
    /// </summary>
    Task<Result<PaymentOrderResultDto>> CreatePaymentAsync(CreatePaymentDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取支付信息
    /// </summary>
    Task<Result<PaymentDto>> GetPaymentAsync(string tradeNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取支付列表
    /// </summary>
    Task<Result<IPagedList<PaymentDto>>> GetPaymentListAsync(PaymentQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 关闭支付订单
    /// </summary>
    Task<Result> ClosePaymentAsync(string tradeNo, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 处理支付回调
    /// </summary>
    Task<Result> HandleCallbackAsync(PaymentCallbackDto request, CancellationToken cancellationToken = default);


    /// <summary>
    /// 获取支付参数字段
    /// </summary>
    Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// 同步订单状态
    /// </summary>
    Task<Result> SyncOrderAsync(string tradeNo, CancellationToken cancellationToken = default);
}
