namespace Tnzi.Payment.Controllers.Admin;

/// <summary>
/// 发票管理控制器基类
/// </summary>
[Route("admin/invoices")]
public abstract class InvoiceAdminControllerBase : ApiAdminControllerBase
{
    private readonly IInvoiceService _invoiceService;

    protected InvoiceAdminControllerBase(IInvoiceService invoiceService)
    {
        _invoiceService = Check.NotNull(invoiceService);
    }

    protected IInvoiceService InvoiceService => _invoiceService;

    /// <summary>
    /// 获取发票列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<InvoiceDto>>> GetList([FromQuery] InvoiceQueryDto query)
    {
        var result = await _invoiceService.GetListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取发票信息
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<InvoiceDto>> Get(Guid id)
    {
        var result = await _invoiceService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 手动创建发票
    /// </summary>
    [HttpPost("manual")]
    public virtual async Task<ApiResult<InvoiceDto>> CreateManual([FromBody] CreateInvoiceDto request)
    {
        var result = await _invoiceService.CreateManualAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 发送发票
    /// </summary>
    [HttpPost("{id:guid}/send")]
    public virtual async Task<ApiResult> Send(Guid id, [FromBody] SendInvoiceDto? request)
    {
        var result = await _invoiceService.SendAsync(id, request?.RecipientEmail);
        return result.ToApiResult();
    }

    /// <summary>
    /// 标记为已支付
    /// </summary>
    [HttpPost("{id:guid}/mark-paid")]
    public virtual async Task<ApiResult> MarkAsPaid(Guid id, [FromBody] MarkInvoicePaidDto request)
    {
        var result = await _invoiceService.MarkAsPaidAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 取消发票
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public virtual async Task<ApiResult> Cancel(Guid id, [FromBody] CancelInvoiceDto request)
    {
        var result = await _invoiceService.CancelAsync(id, request.Reason);
        return result.ToApiResult();
    }
}
