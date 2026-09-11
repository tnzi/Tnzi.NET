namespace Tnzi.Payment.Billing.Services;

/// <summary>
/// 发票服务实现
/// </summary>
public class PaymentInvoiceService : ApplicationService, IPaymentInvoiceService
{
    private readonly IRepository<Invoice, Guid> _invoiceRepository;
    private readonly IRepository<InvoiceLineItem, Guid> _lineItemRepository;
    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly IHtmlToPdfConverter? _pdfConverter;
    private readonly ITemplateRenderService? _templateRenderService;
    private readonly INotificationService? _notificationService;
    private readonly IFileStorageService? _fileStorage;
    private readonly IOptionsSnapshot<InvoiceOptions> _invoiceOptions;
    private readonly IOptionsMonitor<PaymentOptions> _paymentOptions;

    public PaymentInvoiceService(
        IRepository<Invoice, Guid> invoiceRepository,
        IRepository<InvoiceLineItem, Guid> lineItemRepository,
        IRepository<PaymentEntity, Guid> paymentRepository,
        IOptionsSnapshot<InvoiceOptions> invoiceOptions,
        IOptionsMonitor<PaymentOptions> paymentOptions,
        IServiceProvider serviceProvider,
        IHtmlToPdfConverter? pdfConverter = null,
        ITemplateRenderService? templateRenderService = null,
        INotificationService? notificationService = null,
        IFileStorageService? fileStorage = null)
        : base(serviceProvider)
    {
        _invoiceRepository = Check.NotNull(invoiceRepository);
        _lineItemRepository = Check.NotNull(lineItemRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _invoiceOptions = Check.NotNull(invoiceOptions);
        _paymentOptions = Check.NotNull(paymentOptions);
        _pdfConverter = pdfConverter;
        _templateRenderService = templateRenderService;
        _notificationService = notificationService;
        _fileStorage = fileStorage;
    }

    public async Task<Result<PaymentInvoiceDto>> CreateFromPaymentAsync(Guid paymentId, CreatePaymentInvoiceDto? request, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment == null)
            return Fail<PaymentInvoiceDto>(ErrorCodes.PaymentNotFound, 404);

        if (payment.Status is not (PaymentStatus.Succeeded or PaymentStatus.PartialRefunded or PaymentStatus.Refunded))
            return Fail<PaymentInvoiceDto>(ErrorCodes.InvoicePaymentNotSucceeded, 400);

        // 幂等：一笔支付只开一张发票。事件总线是 at-least-once 投递，
        // 没有这道拦截，一次重试就会给同一笔付款生成两张发票号。
        var existing = await _invoiceRepository
            .Where(i => i.PaymentId == paymentId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
            return Ok(existing.MapTo<PaymentInvoiceDto>());

        request ??= new CreatePaymentInvoiceDto();

        // 客户身份优先取支付上的快照：自动开票发生在事件处理器/回调里，那里没有当前用户上下文，
        // 只依赖 CurrentUser 会得到空邮箱，发票永远发不出去。
        var customerName = FirstNonEmpty(request.CustomerName, payment.CustomerName, CurrentUser?.UserName) ?? "Customer";
        var customerEmail = FirstNonEmpty(request.CustomerEmail, payment.CustomerEmail, CurrentUser?.Email);

        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            Logger.LogWarning(
                "Invoice for payment {TradeNo} has no customer email; it will be created but cannot be delivered.",
                payment.TradeNo);
        }

        var now = DateTime.UtcNow;
        var invoice = new Invoice
        {
            InvoiceNo = Invoice.GenerateInvoiceNo(),
            PaymentId = payment.Id,
            Type = request.Type,
            // 由已收款的支付生成的发票本就是"已付"凭据，落 Draft/未付会让账面凭空多出一笔应收
            Status = InvoiceStatus.Paid,
            Amount = payment.OriginalAmount,
            Currency = payment.Currency,
            TaxAmount = payment.TaxAmount,
            DiscountAmount = payment.DiscountAmount,
            DueAmount = payment.PayableAmount,
            PaidAmount = payment.PaidAmount,
            PaidDate = payment.PaidTime ?? now,
            UserId = payment.UserId ?? CurrentUser?.Id,
            CustomerName = customerName,
            CustomerEmail = customerEmail ?? string.Empty,
            CustomerCompany = request.CustomerCompany,
            CustomerTaxId = request.CustomerTaxId,
            CustomerAddress = request.CustomerAddress,
            BillingAddress = request.BillingAddress,
            InvoiceDate = now,
            DueDate = request.DueDate ?? payment.PaidTime ?? now,
            TemplateName = request.TemplateName ?? _invoiceOptions.Value.DefaultTemplate,
            Notes = request.Notes,
            InternalNotes = request.InternalNotes
        };

        // 使用事务保护发票和明细的创建
        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await _invoiceRepository.InsertAsync(invoice, ct);

            var lineItems = request.LineItems is { Count: > 0 }
                ? BuildLineItems(invoice.Id, request.LineItems)
                : [BuildPaymentLineItem(invoice.Id, payment)];

            await _lineItemRepository.InsertManyAsync(lineItems, ct);

            // 明细由调用方提供时，汇总以明细为准；否则保持支付侧的金额拆分
            if (request.LineItems is { Count: > 0 })
            {
                invoice.Amount = lineItems.Sum(l => l.Amount);
                invoice.TaxAmount = lineItems.Sum(l => l.TaxAmount);
                invoice.DiscountAmount = lineItems.Sum(l => l.DiscountAmount) + payment.DiscountAmount;
                invoice.DueAmount = invoice.Amount - invoice.DiscountAmount + invoice.TaxAmount;
                await _invoiceRepository.UpdateAsync(invoice, ct);
            }

            Logger.LogInformation("Invoice created from payment. PaymentId: {PaymentId}, InvoiceNo: {InvoiceNo}",
                paymentId, invoice.InvoiceNo);

            return Ok(invoice.MapTo<PaymentInvoiceDto>());
        }, cancellationToken);
    }

    public async Task<Result<PaymentInvoiceDto>> CreateManualAsync(CreatePaymentInvoiceDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (request.LineItems is not { Count: > 0 })
            return Fail<PaymentInvoiceDto>("At least one line item is required.", 400);

        if (string.IsNullOrWhiteSpace(request.CustomerName))
            return Fail<PaymentInvoiceDto>("Customer name is required.", 400);

        var invoice = new Invoice
        {
            InvoiceNo = Invoice.GenerateInvoiceNo(),
            Type = request.Type,
            Status = InvoiceStatus.Draft,
            Amount = request.LineItems.Sum(l => l.Quantity * l.UnitPrice),
            // 币种取请求或全局默认，不再写死 USD：写死会让非美元账套的发票金额全部标错币种
            Currency = request.Currency ?? _paymentOptions.CurrentValue.DefaultCurrency,
            TaxAmount = 0,
            DiscountAmount = request.LineItems.Sum(l => l.DiscountAmount),
            DueAmount = 0,
            PaidAmount = 0,
            UserId = request.UserId,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail ?? string.Empty,
            CustomerCompany = request.CustomerCompany,
            CustomerTaxId = request.CustomerTaxId,
            CustomerAddress = request.CustomerAddress,
            BillingAddress = request.BillingAddress,
            InvoiceDate = request.InvoiceDate,
            DueDate = request.DueDate ?? request.InvoiceDate.AddDays(PaymentConstants.DefaultInvoiceDueDays),
            TemplateName = request.TemplateName ?? _invoiceOptions.Value.DefaultTemplate,
            Notes = request.Notes,
            InternalNotes = request.InternalNotes
        };

        // 使用事务保护发票和明细的创建
        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await _invoiceRepository.InsertAsync(invoice, ct);

            var lineItems = BuildLineItems(invoice.Id, request.LineItems);

            await _lineItemRepository.InsertManyAsync(lineItems, ct);

            // 更新发票汇总信息
            invoice.TaxAmount = lineItems.Sum(l => l.TaxAmount);
            invoice.DiscountAmount = lineItems.Sum(l => l.DiscountAmount);
            invoice.DueAmount = invoice.Amount - invoice.DiscountAmount + invoice.TaxAmount;

            await _invoiceRepository.UpdateAsync(invoice, ct);

            Logger.LogInformation("Manual invoice created. InvoiceNo: {InvoiceNo}", invoice.InvoiceNo);

            return Ok(invoice.MapTo<PaymentInvoiceDto>());
        }, cancellationToken);
    }

    private static List<InvoiceLineItem> BuildLineItems(Guid invoiceId, List<InvoiceLineItemDto> items)
    {
        return items.Select((item, index) =>
        {
            var amount = item.Quantity * item.UnitPrice;
            return new InvoiceLineItem
            {
                InvoiceId = invoiceId,
                LineNumber = index + 1,
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Amount = amount,
                DiscountAmount = item.DiscountAmount,
                TaxRate = item.TaxRate,
                TaxAmount = (amount - item.DiscountAmount) * item.TaxRate / 100,
                ProductCode = item.ProductCode
            };
        }).ToList();
    }

    /// <summary>
    /// 支付未提供明细时生成的单行明细，保证发票总有可打印的内容
    /// </summary>
    private static InvoiceLineItem BuildPaymentLineItem(Guid invoiceId, PaymentEntity payment) => new()
    {
        InvoiceId = invoiceId,
        LineNumber = 1,
        Description = payment.Description ?? payment.BusinessOrderNo,
        Quantity = 1,
        UnitPrice = payment.OriginalAmount,
        Amount = payment.OriginalAmount,
        DiscountAmount = payment.DiscountAmount,
        TaxRate = 0,
        TaxAmount = payment.TaxAmount,
        ProductCode = payment.BusinessOrderNo
    };

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    public async Task<Result> SendAsync(Guid invoiceId, string? recipientEmail, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        // 501 而不是 500：这不是故障，是这台宿主没装通知模块 —— 500 会让监控与客户端
        // 一直重试一件永远不会好的事。指名要加载哪个模块，别让部署疏漏被读成偶发错误。
        if (_notificationService == null)
        {
            return Fail(
                "Sending an invoice requires the notification module. "
                + "Load Tnzi.Notification ([DependsOn(typeof(NotificationModule))]) "
                + "or register your own INotificationService.", 501);
        }

        var invoice = await _invoiceRepository.FirstOrDefaultAsync(
            i => i.Id == invoiceId && (!ownerUserId.HasValue || i.UserId == ownerUserId.Value), cancellationToken);
        if (invoice == null)
            return Fail(ErrorCodes.InvoiceNotFound, 404);

        if (invoice.Status == InvoiceStatus.Cancelled)
            return Fail(ErrorCodes.InvoiceCannotCancel, 400);

        // 重发次数上限：客服反复补发是常态，但要有上限防止被当成群发通道
        if (invoice.SendCount >= PaymentConstants.MaxInvoiceSendCount)
            return Fail(ErrorCodes.InvoiceSendLimitReached, 400);

        // 发送邮件
        var email = recipientEmail ?? invoice.CustomerEmail;
        if (string.IsNullOrEmpty(email))
            return Fail(ErrorCodes.InvoiceRecipientEmailRequired, 400);

        // 生成PDF
        var pdfResult = await GeneratePdfAsync(invoiceId, cancellationToken);
        if (!pdfResult.Succeeded)
            return Fail(pdfResult.Message ?? ErrorCodes.InvoiceNotFound);

        var sendResult = await _notificationService.CreateAndSendAsync(
            new CreateNotificationRequest
            {
                Type = NotificationType.Email,
                // 事务性：账单是既有付费关系下必需的往来，不是营销。
                IsTransactional = true,
                Subject = $"Invoice {invoice.InvoiceNo}",
                Content = $"Please find attached invoice {invoice.InvoiceNo}.",
                IsHtml = false,
                SendImmediately = true,
                Recipients =
                [
                    new RecipientInput
                    {
                        Address = email,
                        Name = invoice.CustomerName
                    }
                ]
            },
            cancellationToken);

        if (!sendResult.Succeeded)
            return Fail(sendResult.Message ?? "Failed to send invoice email.");

        // 只把"未发出"的发票推进为已发送：已支付的发票再补发一次不应被降级回 Sent，
        // 那会让一张已收款的发票在账面上重新变成未收款。
        if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Pending)
            invoice.Status = InvoiceStatus.Sent;

        invoice.SendCount++;
        invoice.LastSentTime = DateTime.UtcNow;
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        Logger.LogInformation("Invoice sent. InvoiceNo: {InvoiceNo}, Recipient: {Email}",
            invoice.InvoiceNo, email);

        return Ok();
    }

    public async Task<Result<string>> GeneratePdfAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice == null)
            return Fail<string>(ErrorCodes.InvoiceNotFound, 404);

        var lineItems = await _lineItemRepository.AsNoTracking()
            .Where(l => l.InvoiceId == invoiceId)
            .OrderBy(l => l.LineNumber)
            .ToListAsync(cancellationToken);

        var model = new InvoicePdfDto
        {
            Invoice = invoice.MapTo<PaymentInvoiceDto>(),
            LineItems = lineItems.MapToList<InvoiceLineItemDto>(),
            CompanyName = _invoiceOptions.Value.CompanyName ?? string.Empty,
            CompanyAddress = _invoiceOptions.Value.CompanyAddress ?? string.Empty,
            CompanyEmail = _invoiceOptions.Value.CompanyEmail ?? string.Empty,
            TaxId = _invoiceOptions.Value.TaxId ?? string.Empty
        };

        // 使用模板系统渲染发票 HTML
        var htmlContent = await RenderInvoiceHtmlAsync(invoice, model, cancellationToken);

        // 通过 Template 模块的 IHtmlToPdfConverter 生成输出字节
        byte[] outputBytes;
        var fileExtension = ".html";
        var contentType = "text/html";
        if (_pdfConverter != null)
        {
            outputBytes = await _pdfConverter.ConvertAsync(htmlContent, null, cancellationToken);
            fileExtension = _pdfConverter.FileExtension;
            contentType = _pdfConverter.ContentType;
        }
        else
        {
            outputBytes = Encoding.UTF8.GetBytes(htmlContent);
        }

        // 持久化生成的文件：优先走框架 Storage 模块（多实例/容器部署安全），否则回退本地文件系统
        var fileName = $"{invoice.InvoiceNo}{fileExtension}";

        if (_fileStorage != null)
        {
            using var stream = new MemoryStream(outputBytes);
            var saveResult = await _fileStorage.SaveWithReferenceAsync(
                fileName, stream, nameof(Invoice), invoice.Id, nameof(Invoice.PdfFileId));

            if (saveResult.Succeeded && saveResult.Data != null)
            {
                invoice.PdfFileId = saveResult.Data.Id;
                invoice.PdfFilePath = saveResult.Data.Path;
            }
            else
            {
                Logger.LogWarning("Invoice PDF storage save failed, falling back to local file. InvoiceNo: {InvoiceNo}, Error: {Error}",
                    invoice.InvoiceNo, saveResult.Message);
                invoice.PdfFilePath = await WriteLocalFallbackAsync(fileName, outputBytes, cancellationToken);
            }
        }
        else
        {
            invoice.PdfFilePath = await WriteLocalFallbackAsync(fileName, outputBytes, cancellationToken);
        }

        // ★ 这里此前写的是本端点自己的路径（"/api/invoices/{id}/pdf"），而那个端点返回的正是这个字符串 ——
        // 一条指向自己的链接，任何客户端顺着它走都只会拿到同一个字符串，产物永远下载不到。
        // 真正可下载的地址只有 Storage 给得出来；本地回退没有 HTTP 可达的地址，
        // 此时留空并由 /invoices/{id}/pdf 直接吐文件流（见 GetPdfContentAsync）。
        invoice.PdfFileUrl = invoice.PdfFileId.HasValue && _fileStorage != null
            ? (await _fileStorage.GetUrlAsync(invoice.PdfFileId.Value)) is { Succeeded: true } url ? url.Data : null
            : null;

        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        Logger.LogInformation("Invoice generated. InvoiceNo: {InvoiceNo}, Format: {Format}, StorageBacked: {StorageBacked}",
            invoice.InvoiceNo, contentType, invoice.PdfFileId != null);

        return Ok<string>(invoice.PdfFileUrl ?? string.Empty);
    }

    /// <summary>
    /// 本地文件系统回退（仅在未加载 Storage 模块时使用）
    /// </summary>
    private static async Task<string> WriteLocalFallbackAsync(string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var invoiceDir = Path.Combine(AppContext.BaseDirectory, "invoices");
        Directory.CreateDirectory(invoiceDir);
        var localFilePath = Path.Combine(invoiceDir, fileName);
        await File.WriteAllBytesAsync(localFilePath, content, cancellationToken);
        return localFilePath;
    }

    /// <summary>
    /// 使用模板系统渲染发票 HTML
    /// 优先使用数据库/文件系统中的自定义模板，fallback 到内置 HTML
    /// </summary>
    private async Task<string> RenderInvoiceHtmlAsync(Invoice invoice, InvoicePdfDto model, CancellationToken cancellationToken)
    {
        // 优先使用框架模板系统（支持用户在数据库或文件系统中覆盖模板）
        if (_templateRenderService != null)
        {
            var templateName = invoice.TemplateName ?? _invoiceOptions.Value.DefaultTemplate ?? "InvoiceDefault";
            var renderResult = await _templateRenderService.RenderByNameAsync(
                templateName, "Payment", model, "Invoice", null, cancellationToken);

            if (renderResult.Succeeded)
                return renderResult.Data!.Content;

            Logger.LogWarning("Invoice template rendering failed for '{TemplateName}', using fallback. Error: {Error}",
                templateName, renderResult.Message);
        }

        // Fallback: 内置简单 HTML（模板模块未加载或模板渲染失败时）
        return RenderFallbackHtml(model);
    }

    /// <summary>
    /// 内置 Fallback HTML 渲染（无模板模块依赖）。
    /// 所有取自数据的文本字段一律 HTML 编码：客户名/备注/明细描述等均可由外部录入，
    /// 直接插值会把标记注入进邮件正文与 /api/invoices/{id}/pdf 的返回内容。
    /// </summary>
    private static string RenderFallbackHtml(InvoicePdfDto model)
    {
        var inv = model.Invoice;
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'/>");
        sb.AppendLine("<style>body{font-family:sans-serif;margin:40px}table{width:100%;border-collapse:collapse}th,td{padding:8px;text-align:left;border-bottom:1px solid #ddd}th{background:#f5f5f5}.right{text-align:right}.total{font-weight:bold;font-size:1.2em}</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine($"<h1>{Escape(model.CompanyName)}</h1>");
        if (!string.IsNullOrEmpty(model.CompanyAddress))
            sb.AppendLine($"<p>{Escape(model.CompanyAddress)}</p>");
        sb.AppendLine($"<h2>Invoice #{Escape(inv.InvoiceNo)}</h2>");
        sb.AppendLine($"<p>Date: {inv.InvoiceDate:yyyy-MM-dd} | Status: {inv.Status} | Currency: {Escape(inv.Currency)}</p>");
        sb.AppendLine($"<p><strong>Bill To:</strong> {Escape(inv.CustomerName)}");
        if (!string.IsNullOrEmpty(inv.CustomerEmail))
            sb.Append($" ({Escape(inv.CustomerEmail)})");
        sb.AppendLine("</p>");
        sb.AppendLine("<table><thead><tr><th>Description</th><th class='right'>Qty</th><th class='right'>Unit Price</th><th class='right'>Amount</th></tr></thead><tbody>");
        foreach (var item in model.LineItems)
        {
            sb.AppendLine($"<tr><td>{Escape(item.Description)}</td><td class='right'>{item.Quantity:N2}</td><td class='right'>{item.UnitPrice:N2}</td><td class='right'>{item.Amount:N2}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");
        sb.AppendLine($"<p class='right'>Subtotal: {inv.Amount:N2}</p>");
        if (inv.DiscountAmount > 0)
            sb.AppendLine($"<p class='right'>Discount: -{inv.DiscountAmount:N2}</p>");
        if (inv.TaxAmount > 0)
            sb.AppendLine($"<p class='right'>Tax: {inv.TaxAmount:N2}</p>");
        sb.AppendLine($"<p class='right total'>Total Due: {Escape(inv.Currency)} {inv.DueAmount:N2}</p>");
        if (!string.IsNullOrEmpty(inv.Notes))
            sb.AppendLine($"<p><em>Notes: {Escape(inv.Notes)}</em></p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string Escape(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : WebUtility.HtmlEncode(value);

    public async Task<Result<string>> GetPdfUrlAsync(Guid invoiceId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(
            i => i.Id == invoiceId && (!ownerUserId.HasValue || i.UserId == ownerUserId.Value), cancellationToken);
        if (invoice == null)
            return Fail<string>(ErrorCodes.InvoiceNotFound, 404);

        if (string.IsNullOrEmpty(invoice.PdfFileUrl))
            return await GeneratePdfAsync(invoiceId, cancellationToken);

        return Ok<string>(invoice.PdfFileUrl);
    }

    /// <summary>
    /// 取发票产物的字节。产物不在就先生成一次。
    /// </summary>
    /// <remarks>
    /// 这是唯一在两种落地方式下都能用的下载路径：Storage 存的读回它的流，
    /// 本地回退读磁盘上那个文件。<c>GetPdfUrlAsync</c> 只在 Storage 给得出地址时有值，
    /// 而未加载 Storage 的宿主（本地回退）本来就没有任何 HTTP 可达的地址。
    /// </remarks>
    public async Task<Result<InvoiceDocumentDto>> GetPdfContentAsync(Guid invoiceId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(
            i => i.Id == invoiceId && (!ownerUserId.HasValue || i.UserId == ownerUserId.Value), cancellationToken);
        if (invoice == null)
            return Fail<InvoiceDocumentDto>(ErrorCodes.InvoiceNotFound, 404);

        if (invoice.PdfFileId == null && string.IsNullOrEmpty(invoice.PdfFilePath))
        {
            var generated = await GeneratePdfAsync(invoiceId, cancellationToken);
            if (!generated.Succeeded)
                return Fail<InvoiceDocumentDto>(generated.Message ?? ErrorCodes.InvoiceDocumentUnavailable, generated.Code ?? 400);

            invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
            if (invoice == null)
                return Fail<InvoiceDocumentDto>(ErrorCodes.InvoiceNotFound, 404);
        }

        var extension = _pdfConverter != null ? ".pdf" : ".html";
        var contentType = _pdfConverter?.ContentType ?? "text/html";

        if (invoice.PdfFileId.HasValue && _fileStorage != null)
        {
            var stored = await _fileStorage.GetAsync(invoice.PdfFileId.Value);
            if (stored is { Succeeded: true, Data: not null })
            {
                using var buffer = new MemoryStream();
                await stored.Data.CopyToAsync(buffer, cancellationToken);
                await stored.Data.DisposeAsync();

                return Ok(new InvoiceDocumentDto
                {
                    Content = buffer.ToArray(),
                    ContentType = contentType,
                    FileName = $"{invoice.InvoiceNo}{extension}"
                });
            }

            Logger.LogWarning("Invoice document is recorded in storage but could not be read. InvoiceNo: {InvoiceNo}, FileId: {FileId}, Error: {Error}",
                invoice.InvoiceNo, invoice.PdfFileId, stored.Message);
        }

        if (!string.IsNullOrEmpty(invoice.PdfFilePath) && File.Exists(invoice.PdfFilePath))
        {
            return Ok(new InvoiceDocumentDto
            {
                Content = await File.ReadAllBytesAsync(invoice.PdfFilePath, cancellationToken),
                ContentType = contentType,
                FileName = $"{invoice.InvoiceNo}{extension}"
            });
        }

        Logger.LogWarning("Invoice document is missing on disk and in storage. InvoiceNo: {InvoiceNo}", invoice.InvoiceNo);
        return Fail<InvoiceDocumentDto>(ErrorCodes.InvoiceDocumentUnavailable, 404);
    }

    public async Task<Result> MarkAsPaidAsync(Guid invoiceId, MarkInvoicePaidDto request, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice == null)
            return Fail(ErrorCodes.InvoiceNotFound, 404);

        if (invoice.Status == InvoiceStatus.Paid)
            return Fail(ErrorCodes.InvoiceAlreadyPaid, 400);

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAmount = request.PaidAmount;
        invoice.PaidDate = DateTime.UtcNow;

        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        // 发布发票支付事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new InvoicePaidEvent
            {
                InvoiceId = invoice.Id,
                InvoiceNo = invoice.InvoiceNo,
                PaidAmount = request.PaidAmount,
                PaidTime = invoice.PaidDate.Value,
                PaymentId = invoice.PaymentId
            });
        }

        Logger.LogInformation("Invoice marked as paid. InvoiceNo: {InvoiceNo}, Amount: {Amount}",
            invoice.InvoiceNo, request.PaidAmount);

        return Ok();
    }

    public async Task<Result> CancelAsync(Guid invoiceId, string? reason, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice == null)
            return Fail(ErrorCodes.InvoiceNotFound, 404);

        if (invoice.Status == InvoiceStatus.Paid)
            return Fail(ErrorCodes.InvoiceCannotCancel, 400);

        invoice.Status = InvoiceStatus.Cancelled;
        if (!string.IsNullOrEmpty(reason))
            invoice.InternalNotes = string.IsNullOrEmpty(invoice.InternalNotes) ? $"Cancelled: {reason}" : $"{invoice.InternalNotes}\nCancelled: {reason}";

        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        Logger.LogInformation("Invoice cancelled. InvoiceNo: {InvoiceNo}, Reason: {Reason}",
            invoice.InvoiceNo, reason);

        return Ok();
    }

    public async Task<Result<PaymentInvoiceDto>> GetAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(
            i => i.Id == id && (!ownerUserId.HasValue || i.UserId == ownerUserId.Value), cancellationToken);
        if (invoice == null)
            return Fail<PaymentInvoiceDto>(ErrorCodes.InvoiceNotFound, 404);

        return Ok(invoice.MapTo<PaymentInvoiceDto>());
    }

    public async Task<Result<IPagedList<PaymentInvoiceDto>>> GetListAsync(PaymentInvoiceQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var queryable = _invoiceRepository.AsNoTracking().Filter(query);

        if (ownerUserId.HasValue)
            queryable = queryable.Where(i => i.UserId == ownerUserId.Value);

        var pagedList = await queryable
            .OrderByDescending(i => i.InvoiceDate)
            .ProjectTo<Invoice, PaymentInvoiceDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    /// <summary>
    /// 「我的发票」。
    /// </summary>
    /// <remarks>
    /// 按 <c>UserId</c> 过滤而不是 <c>CreatorId</c>：自动开票发生在支付完成事件的处理器里
    /// （webhook 匿名，或后台扣款的裸 scope），那里根本没有当前用户，于是 <c>CreatorId</c> 恒为 null ——
    /// 按它过滤的结果是这个列表**永远是空的**，而接口 200、日志干净、发票一张不少地躺在库里。
    /// 同服务其余四个用户面方法一直按 <c>UserId</c> 过滤，只有这一处不是。
    /// </remarks>
    public async Task<Result<IPagedList<PaymentInvoiceDto>>> GetUserInvoicesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var invoices = await _invoiceRepository.AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.InvoiceDate)
            .ProjectTo<Invoice, PaymentInvoiceDto>()
            .CreateAsync(1, 100, cancellationToken);

        return Ok(invoices);
    }
}
