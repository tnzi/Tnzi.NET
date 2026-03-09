namespace Tnzi.Payment.Controllers;

/// <summary>
/// 退款控制器基类
/// </summary>
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
[Route("refunds")]
[DefaultController]
public class DefaultRefundController : ApiControllerBase
{
    private readonly IRefundService _refundService;

    public DefaultRefundController(IRefundService refundService)
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
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _refundService.CreateRefundAsync(request, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取退款信息
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<RefundDto>> Get(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _refundService.GetRefundAsync(id, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取退款列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<RefundDto>>> GetList([FromQuery] RefundQueryDto query)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _refundService.GetRefundListAsync(query, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据支付单号获取退款列表
    /// </summary>
    [HttpGet("trade/{tradeNo}")]
    public virtual async Task<ApiResult<List<RefundDto>>> GetByTradeNo(string tradeNo)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _refundService.GetRefundsByTradeNoAsync(tradeNo, userId);
        return result.ToApiResult();
    }
}
