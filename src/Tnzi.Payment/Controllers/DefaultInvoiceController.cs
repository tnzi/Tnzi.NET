namespace Tnzi.Payment.Controllers;

/// <summary>
/// 发票控制器基类
/// </summary>
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
[Route("invoices")]
[DefaultController]
public class DefaultInvoiceController : ApiControllerBase
{
    private readonly IPaymentInvoiceService _invoiceService;

    public DefaultInvoiceController(IPaymentInvoiceService invoiceService)
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
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _invoiceService.GetListAsync(query, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取发票信息
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<PaymentInvoiceDto>> Get(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _invoiceService.GetAsync(id, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取我的发票列表
    /// </summary>
    [HttpGet("my")]
    public virtual async Task<ApiResult<IPagedList<PaymentInvoiceDto>>> GetMyInvoices()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _invoiceService.GetUserInvoicesAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 下载PDF
    /// </summary>
    [HttpGet("{id:guid}/pdf")]
    public virtual async Task<ApiResult<string>> DownloadPdf(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _invoiceService.GetPdfUrlAsync(id, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 发送发票
    /// </summary>
    [HttpPost("{id:guid}/send")]
    public virtual async Task<ApiResult> Send(Guid id, [FromBody] SendInvoiceDto? request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _invoiceService.SendAsync(id, request?.RecipientEmail, userId);
        return result.ToApiResult();
    }
}
