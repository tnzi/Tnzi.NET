namespace Tnzi.Payment.Controllers;

/// <summary>
/// 支付控制器基类
/// </summary>
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
[Route("payments")]
public abstract class PaymentControllerBase : ApiControllerBase
{
    private readonly IPaymentService _paymentService;

    protected PaymentControllerBase(IPaymentService paymentService)
    {
        _paymentService = Check.NotNull(paymentService);
    }

    protected IPaymentService PaymentService => _paymentService;

    /// <summary>
    /// 创建支付订单
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<PaymentOrderResultDto>> Create([FromBody] CreatePaymentDto request)
    {
        var result = await _paymentService.CreatePaymentAsync(request);
        return result.ToApiResult();
    }

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
    /// 获取支付参数字段
    /// </summary>
    [HttpGet("{tradeNo}/params")]
    public virtual async Task<ApiResult<PaymentParamsDto>> GetPaymentParams(string tradeNo)
    {
        var result = await _paymentService.GetPaymentParamsAsync(tradeNo);
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
