namespace Tnzi.Payment.Billing.Services;

/// <summary>
/// 发票服务接口
/// </summary>
public interface IPaymentInvoiceService
{
    /// <summary>
    /// 从支付创建发票
    /// </summary>
    Task<Result<PaymentInvoiceDto>> CreateFromPaymentAsync(Guid paymentId, CreatePaymentInvoiceDto? request = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 手动创建发票
    /// </summary>
    Task<Result<PaymentInvoiceDto>> CreateManualAsync(CreatePaymentInvoiceDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送发票
    /// </summary>
    Task<Result> SendAsync(Guid invoiceId, string? recipientEmail = null, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成PDF
    /// </summary>
    Task<Result<string>> GeneratePdfAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取PDF URL
    /// </summary>
    Task<Result<string>> GetPdfUrlAsync(Guid invoiceId, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取发票产物的字节（PDF 或 HTML）。产物不在就先生成一次。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="GetPdfUrlAsync"/> 分工明确：那一个只在 Storage 给得出 HTTP 可达地址时有值
    /// （适合放进邮件正文），而这一个在两种落地方式下都能用 —— 未加载 Storage 的宿主把产物
    /// 写在本地磁盘上，任何 URL 都指不到它。
    /// </remarks>
    Task<Result<InvoiceDocumentDto>> GetPdfContentAsync(Guid invoiceId, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 标记为已支付
    /// </summary>
    Task<Result> MarkAsPaidAsync(Guid invoiceId, MarkInvoicePaidDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消发票
    /// </summary>
    Task<Result> CancelAsync(Guid invoiceId, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取发票信息
    /// </summary>
    Task<Result<PaymentInvoiceDto>> GetAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取发票列表
    /// </summary>
    Task<Result<IPagedList<PaymentInvoiceDto>>> GetListAsync(PaymentInvoiceQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户发票列表
    /// </summary>
    Task<Result<IPagedList<PaymentInvoiceDto>>> GetUserInvoicesAsync(Guid userId, CancellationToken cancellationToken = default);
}
