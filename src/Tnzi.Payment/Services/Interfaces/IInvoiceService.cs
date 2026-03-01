namespace Tnzi.Payment.Services;

/// <summary>
/// 发票服务接口
/// </summary>
public interface IInvoiceService
{
    /// <summary>
    /// 从支付创建发票
    /// </summary>
    Task<Result<InvoiceDto>> CreateFromPaymentAsync(Guid paymentId, CreateInvoiceDto? request = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 手动创建发票
    /// </summary>
    Task<Result<InvoiceDto>> CreateManualAsync(CreateInvoiceDto request, CancellationToken cancellationToken = default);

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
    Task<Result<InvoiceDto>> GetAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取发票列表
    /// </summary>
    Task<Result<IPagedList<InvoiceDto>>> GetListAsync(InvoiceQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户发票列表
    /// </summary>
    Task<Result<IPagedList<InvoiceDto>>> GetUserInvoicesAsync(Guid userId, CancellationToken cancellationToken = default);
}
