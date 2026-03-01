namespace Tnzi.Payment.Controllers;

/// <summary>
/// 发票控制器基类
/// </summary>
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
[Route("invoices")]
public abstract class InvoiceControllerBase : ApiControllerBase
{
    private readonly IInvoiceService _invoiceService;

    protected InvoiceControllerBase(IInvoiceService invoiceService)
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
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _invoiceService.GetListAsync(query, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取发票信息
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<InvoiceDto>> Get(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _invoiceService.GetAsync(id, userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取我的发票列表
    /// </summary>
    [HttpGet("my")]
    public virtual async Task<ApiResult<IPagedList<InvoiceDto>>> GetMyInvoices()
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
