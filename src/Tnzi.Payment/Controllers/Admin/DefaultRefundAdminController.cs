namespace Tnzi.Payment.Controllers.Admin;

/// <summary>
/// 退款管理控制器基类
/// </summary>
[Route("admin/refunds")]
[DefaultController]
[ApiAuthorize(PermissionName = "payment.refund.view")]
public class DefaultRefundAdminController : ApiAdminControllerBase
{
    private readonly IRefundService _refundService;

    public DefaultRefundAdminController(IRefundService refundService)
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
    /// 代客发起退款。客服代客退款是退款场景里占比最高的一种，
    /// 此前只有用户端能创建退款记录，管理端只能审批既有申请。
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "payment.refund.create")]
    public virtual async Task<ApiResult<RefundDto>> Create([FromBody] CreateRefundDto request)
    {
        var result = await _refundService.CreateRefundAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 审批退款
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [ApiAuthorize(PermissionName = "payment.refund.update")]
    public virtual async Task<ApiResult> Approve(Guid id, [FromBody] ApproveRefundDto request)
    {
        var result = await _refundService.ApproveRefundAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 执行退款
    /// </summary>
    [HttpPost("{id:guid}/process")]
    [ApiAuthorize(PermissionName = "payment.refund.update")]
    public virtual async Task<ApiResult> Process(Guid id)
    {
        var result = await _refundService.ProcessRefundAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 取消退款
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ApiAuthorize(PermissionName = "payment.refund.update")]
    public virtual async Task<ApiResult> Cancel(Guid id, [FromBody] CancelRefundDto request)
    {
        var result = await _refundService.CancelRefundAsync(id, request.Reason);
        return result.ToApiResult();
    }
}
