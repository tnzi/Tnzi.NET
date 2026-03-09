namespace Tnzi.Payment.Controllers.Admin;

/// <summary>
/// 支付管理控制器基类
/// </summary>
[Route("admin/payments")]
[DefaultController]
public class DefaultPaymentAdminController : ApiAdminControllerBase
{
    private readonly IPaymentService _paymentService;

    public DefaultPaymentAdminController(IPaymentService paymentService)
    {
        _paymentService = Check.NotNull(paymentService);
    }

    protected IPaymentService PaymentService => _paymentService;

    /// <summary>
    /// 获取支付信息
    /// </summary>
    [HttpGet("{tradeNo}")]
    public virtual async Task<ApiResult<PaymentDto>> Get(string tradeNo)
    {
        var result = await _paymentService.GetPaymentAsync(tradeNo);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取支付列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<PaymentDto>>> GetList([FromQuery] PaymentQueryDto query)
    {
        var result = await _paymentService.GetPaymentListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 关闭支付订单
    /// </summary>
    [HttpPost("{tradeNo}/close")]
    public virtual async Task<ApiResult> Close(string tradeNo, [FromBody] ClosePaymentDto request)
    {
        var result = await _paymentService.ClosePaymentAsync(tradeNo, request.Reason);
        return result.ToApiResult();
    }

    /// <summary>
    /// 同步订单状态
    /// </summary>
    [HttpPost("{tradeNo}/sync")]
    public virtual async Task<ApiResult> Sync(string tradeNo)
    {
        var result = await _paymentService.SyncOrderAsync(tradeNo);
        return result.ToApiResult();
    }
}
