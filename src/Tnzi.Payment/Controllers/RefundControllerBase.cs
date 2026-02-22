namespace Tnzi.Payment.Controllers;

/// <summary>
/// 退款控制器基类
/// </summary>
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
[Route("refunds")]
public abstract class RefundControllerBase : ApiControllerBase
{
    private readonly IRefundService _refundService;

    protected RefundControllerBase(IRefundService refundService)
    {
        _refundService = Check.NotNull(refundService);
    }

    protected IRefundService RefundService => _refundService;

    /// <summary>
    /// 申请退款
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<RefundDto>> Create([FromBody] CreateRefundDto request)
    {
        var result = await _refundService.CreateRefundAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取退款信息
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<RefundDto>> Get(Guid id)
    {
        var result = await _refundService.GetRefundAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取退款列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<RefundDto>>> GetList([FromQuery] RefundQueryDto query)
    {
        var result = await _refundService.GetRefundListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据支付单号获取退款列表
    /// </summary>
    [HttpGet("trade/{tradeNo}")]
    public virtual async Task<ApiResult<List<RefundDto>>> GetByTradeNo(string tradeNo)
    {
        var result = await _refundService.GetRefundsByTradeNoAsync(tradeNo);
        return result.ToApiResult();
    }
}
