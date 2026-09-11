namespace Tnzi.Payment.Billing.Controllers;

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
    /// 下载发票文件。
    /// </summary>
    /// <remarks>
    /// 直接返回文件字节。此前这里返回的是一个字符串，而那个字符串正是本端点自己的路径 ——
    /// 一条指向自己的链接，任何客户端顺着它走都只会再拿到同一个字符串。
    /// 直接吐文件是唯一在两种落地方式下都成立的做法：只有 Storage 给得出 HTTP 可达的地址，
    /// 而未加载 Storage 的宿主把产物写在本地磁盘上。要在邮件里放链接请用 <c>{id}/pdf-url</c>。
    /// </remarks>
    [HttpGet("{id:guid}/pdf")]
    public virtual async Task<IActionResult> DownloadPdf(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _invoiceService.GetPdfContentAsync(id, userId);

        if (!result.Succeeded || result.Data == null)
            return StatusCode(result.Code ?? 400, ApiResult.Error(result.Message ?? ErrorCodes.InvoiceDocumentUnavailable, result.Code ?? 400));

        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }

    /// <summary>
    /// 获取发票文件的可分享地址（仅在产物由 Storage 承载时有值；本地回退时为空）。
    /// </summary>
    [HttpGet("{id:guid}/pdf-url")]
    public virtual async Task<ApiResult<string>> GetPdfUrl(Guid id)
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
