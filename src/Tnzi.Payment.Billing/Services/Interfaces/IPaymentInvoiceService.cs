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
    /// 为一笔已成功的退款开贷项通知单，冲减这笔支付开出的那张票据（原票据不动）。按退款幂等；
    /// 这笔支付没开过票时答 404（没有可冲减的东西）。
    /// </summary>
    Task<Result<PaymentInvoiceDto>> CreateCreditNoteAsync(CreateCreditNoteDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送发票
    /// </summary>
    Task<Result> SendAsync(Guid invoiceId, string? recipientEmail = null, Guid? ownerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 首次发送（支付完成后的自动发送用）：只在发票从未发出过时发一封，已经发过（含正在发送）时什么也不做。
    /// <c>Data</c> 表示这次是否真的发出。
    /// </summary>
    /// <remarks>
    /// 默认实现先读后发，两次并发调用可能各发一封；框架实现以一条条件写认领首发名额，并发的两次至多发一封。
    /// 自定义实现应提供同样的原子认领（分布式事件总线至少投递一次，同一事件可能被并发处理两次）。
    /// </remarks>
    async Task<Result<bool>> SendFirstAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await GetAsync(invoiceId, null, cancellationToken);
        if (!invoice.Succeeded || invoice.Data == null)
        {
            return Result.Failure<bool>(invoice.Message ?? "Invoice not found.", invoice.Code ?? 404, invoice.ErrorCode, invoice.ErrorDetails);
        }

        if (invoice.Data.SendCount > 0 || invoice.Data.LastSentTime != null)
            return Result.Success(false);

        var sent = await SendAsync(invoiceId, null, null, cancellationToken);
        return sent.Succeeded
            ? Result.Success(true)
            : Result.Failure<bool>(sent.Message ?? "Invoice delivery failed.", sent.Code ?? 500, sent.ErrorCode, sent.ErrorDetails);
    }

    /// <summary>
    /// 生成PDF
    /// </summary>
    Task<Result<string>> GeneratePdfAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取发票文件在 Storage 里的下载地址（站内相对路径，按登录会话鉴权：归属用户或持 <c>payment.invoice.view</c> 的人可读）。
    /// 未加载 Storage 时恒为空串（本地回退没有 HTTP 可达的地址）。不是可外发的公开链接。
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
    /// 标记为已支付（已付或已作废的拒绝；备注追加进内部备注；已生成的发票文件随之作废，下次取用时重新生成）
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
    /// 获取用户发票列表（按发票日期倒序分页；<paramref name="query"/> 为 null 时取最新 100 条）
    /// </summary>
    Task<Result<IPagedList<PaymentInvoiceDto>>> GetUserInvoicesAsync(Guid userId, PagedQueryDto? query = null, CancellationToken cancellationToken = default);
}
