namespace Tnzi.Payment.Controllers.Admin;

/// <summary>
/// 发票管理控制器基类
/// </summary>
[Route("admin/invoices")]
[DefaultController]
[ApiAuthorize(PermissionName = "payment.invoice.view")]
public class DefaultInvoiceAdminController : ApiAdminControllerBase
{
    private readonly IPaymentInvoiceService _invoiceService;

    public DefaultInvoiceAdminController(IPaymentInvoiceService invoiceService)
    {
        _invoiceService = Check.NotNull(invoiceService);
    }

    protected IPaymentInvoiceService PaymentInvoiceService => _invoiceService;

    /// <summary>
    /// 获取发票列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<PaymentInvoiceDto>>> GetList([FromQuery] PaymentInvoiceQueryDto query)
    {
        var result = await _invoiceService.GetListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取发票信息
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<PaymentInvoiceDto>> Get(Guid id)
    {
        var result = await _invoiceService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 手动创建发票
    /// </summary>
    [HttpPost("manual")]
    [ApiAuthorize(PermissionName = "payment.invoice.create")]
    public virtual async Task<ApiResult<PaymentInvoiceDto>> CreateManual([FromBody] CreatePaymentInvoiceDto request)
    {
        var result = await _invoiceService.CreateManualAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 发送发票
    /// </summary>
    [HttpPost("{id:guid}/send")]
    [ApiAuthorize(PermissionName = "payment.invoice.update")]
    public virtual async Task<ApiResult> Send(Guid id, [FromBody] SendInvoiceDto? request)
    {
        var result = await _invoiceService.SendAsync(id, request?.RecipientEmail);
        return result.ToApiResult();
    }

    /// <summary>
    /// 标记为已支付
    /// </summary>
    [HttpPost("{id:guid}/mark-paid")]
    [ApiAuthorize(PermissionName = "payment.invoice.update")]
    public virtual async Task<ApiResult> MarkAsPaid(Guid id, [FromBody] MarkInvoicePaidDto request)
    {
        var result = await _invoiceService.MarkAsPaidAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 取消发票
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ApiAuthorize(PermissionName = "payment.invoice.update")]
    public virtual async Task<ApiResult> Cancel(Guid id, [FromBody] CancelInvoiceDto request)
    {
        var result = await _invoiceService.CancelAsync(id, request.Reason);
        return result.ToApiResult();
    }
}
