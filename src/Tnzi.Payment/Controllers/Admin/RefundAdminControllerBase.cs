namespace Tnzi.Payment.Controllers.Admin;

/// <summary>
/// 退款管理控制器基类
/// </summary>
[Route("admin/refunds")]
public abstract class RefundAdminControllerBase : ApiAdminControllerBase
{
    private readonly IRefundService _refundService;

    protected RefundAdminControllerBase(IRefundService refundService)
    {
        _refundService = Check.NotNull(refundService);
    }

    protected IRefundService RefundService => _refundService;

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

    /// <summary>
    /// 审批退款
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    public virtual async Task<ApiResult> Approve(Guid id, [FromBody] ApproveRefundDto request)
    {
        var result = await _refundService.ApproveRefundAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 执行退款
    /// </summary>
    [HttpPost("{id:guid}/process")]
    public virtual async Task<ApiResult> Process(Guid id)
    {
        var result = await _refundService.ProcessRefundAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 取消退款
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public virtual async Task<ApiResult> Cancel(Guid id, [FromBody] CancelRefundDto request)
    {
        var result = await _refundService.CancelRefundAsync(id, request.Reason);
        return result.ToApiResult();
    }
}
