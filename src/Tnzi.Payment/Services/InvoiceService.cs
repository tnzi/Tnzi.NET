namespace Tnzi.Payment.Services;

/// <summary>
/// 发票服务实现
/// </summary>
public class InvoiceService : ApplicationService, IInvoiceService
{
    private readonly IRepository<Invoice, Guid> _invoiceRepository;
    private readonly IRepository<InvoiceLineItem, Guid> _lineItemRepository;
    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly IHtmlToPdfConverter? _pdfConverter;
    private readonly ITemplateRenderService? _templateRenderService;
    private readonly INotificationService? _notificationService;
    private readonly IOptions<InvoiceOptions> _invoiceOptions;

    public InvoiceService(
        IRepository<Invoice, Guid> invoiceRepository,
        IRepository<InvoiceLineItem, Guid> lineItemRepository,
        IRepository<PaymentEntity, Guid> paymentRepository,
        IOptions<InvoiceOptions> invoiceOptions,
        IServiceProvider serviceProvider,
        IHtmlToPdfConverter? pdfConverter = null,
        ITemplateRenderService? templateRenderService = null,
        INotificationService? notificationService = null)
        : base(serviceProvider)
    {
        _invoiceRepository = Check.NotNull(invoiceRepository);
        _lineItemRepository = Check.NotNull(lineItemRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _invoiceOptions = Check.NotNull(invoiceOptions);
        _pdfConverter = pdfConverter;
        _templateRenderService = templateRenderService;
        _notificationService = notificationService;
    }

    public async Task<Result<InvoiceDto>> CreateFromPaymentAsync(Guid paymentId, CreateInvoiceDto? request, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment == null)
            return Fail<InvoiceDto>(ErrorCodes.PaymentNotFound, 404);

        if (payment.Status != PaymentStatus.Succeeded)
            return Fail<InvoiceDto>(ErrorCodes.InvoicePaymentNotSucceeded, 400);

        request ??= new CreateInvoiceDto();

        var invoice = new Invoice
        {
            InvoiceNo = Invoice.GenerateInvoiceNo(),
            PaymentId = payment.Id,
            Type = request.Type,
            Status = InvoiceStatus.Draft,
            Amount = payment.PaidAmount,
            Currency = payment.Currency,
            TaxAmount = 0,
            DiscountAmount = payment.DiscountAmount,
            DueAmount = payment.PaidAmount - payment.DiscountAmount,
            PaidAmount = 0,
            CustomerName = CurrentUser?.UserName ?? "Customer",
            CustomerEmail = "",
            InvoiceDate = DateTime.UtcNow,
            DueDate = request.DueDate ?? DateTime.UtcNow.AddDays(30),
            TemplateName = request.TemplateName ?? _invoiceOptions.Value.DefaultTemplate,
            Notes = request.Notes,
            InternalNotes = request.InternalNotes
        };

        // 使用事务保护发票和明细的创建
        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await _invoiceRepository.InsertAsync(invoice, ct);

            if (request.LineItems is { Count: > 0 })
            {
                var lineItems = request.LineItems.Select((item, index) => new InvoiceLineItem
                {
                    InvoiceId = invoice.Id,
                    LineNumber = index + 1,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    Amount = item.Quantity * item.UnitPrice,
                    DiscountAmount = item.DiscountAmount,
                    TaxRate = item.TaxRate,
                    TaxAmount = (item.Quantity * item.UnitPrice - item.DiscountAmount) * item.TaxRate / 100,
                    ProductCode = item.ProductCode
                }).ToList();

                await _lineItemRepository.InsertManyAsync(lineItems, ct);

                // 更新发票汇总信息
                invoice.TaxAmount = lineItems.Sum(l => l.TaxAmount);
                invoice.Amount = lineItems.Sum(l => l.Amount);
                invoice.DiscountAmount = lineItems.Sum(l => l.DiscountAmount) + payment.DiscountAmount;
                invoice.DueAmount = invoice.Amount - invoice.DiscountAmount + invoice.TaxAmount;

                await _invoiceRepository.UpdateAsync(invoice, ct);
            }

            Logger.LogInformation("Invoice created from payment. PaymentId: {PaymentId}, InvoiceNo: {InvoiceNo}",
                paymentId, invoice.InvoiceNo);

            return Ok(invoice.MapTo<InvoiceDto>());
        }, cancellationToken);
    }

    public async Task<Result<InvoiceDto>> CreateManualAsync(CreateInvoiceDto request, CancellationToken cancellationToken = default)
    {
        var invoice = new Invoice
        {
            InvoiceNo = Invoice.GenerateInvoiceNo(),
            Type = request.Type,
            Status = InvoiceStatus.Draft,
            Amount = request.LineItems.Sum(l => l.Quantity * l.UnitPrice),
            Currency = "USD",
            TaxAmount = 0,
            DiscountAmount = request.LineItems.Sum(l => l.DiscountAmount),
            DueAmount = 0,
            PaidAmount = 0,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            CustomerCompany = request.CustomerCompany,
            CustomerTaxId = request.CustomerTaxId,
            CustomerAddress = request.CustomerAddress,
            BillingAddress = request.BillingAddress,
            InvoiceDate = request.InvoiceDate,
            DueDate = request.DueDate,
            TemplateName = request.TemplateName ?? _invoiceOptions.Value.DefaultTemplate,
            Notes = request.Notes,
            InternalNotes = request.InternalNotes
        };

        // 使用事务保护发票和明细的创建
        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await _invoiceRepository.InsertAsync(invoice, ct);

            var lineItems = request.LineItems.Select((item, index) => new InvoiceLineItem
            {
                InvoiceId = invoice.Id,
                LineNumber = index + 1,
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Amount = item.Quantity * item.UnitPrice,
                DiscountAmount = item.DiscountAmount,
                TaxRate = item.TaxRate,
                TaxAmount = (item.Quantity * item.UnitPrice - item.DiscountAmount) * item.TaxRate / 100,
                ProductCode = item.ProductCode
            }).ToList();

            await _lineItemRepository.InsertManyAsync(lineItems, ct);

            // 更新发票汇总信息
            invoice.TaxAmount = lineItems.Sum(l => l.TaxAmount);
            invoice.DiscountAmount = lineItems.Sum(l => l.DiscountAmount);
            invoice.DueAmount = invoice.Amount - invoice.DiscountAmount + invoice.TaxAmount;

            await _invoiceRepository.UpdateAsync(invoice, ct);

            Logger.LogInformation("Manual invoice created. InvoiceNo: {InvoiceNo}", invoice.InvoiceNo);

            return Ok(invoice.MapTo<InvoiceDto>());
        }, cancellationToken);
    }

    public async Task<Result> SendAsync(Guid invoiceId, string? recipientEmail, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        if (_notificationService == null)
            return Fail("Notification module is not loaded. Cannot send invoice.", 500);

        var invoice = await _invoiceRepository.FirstOrDefaultAsync(
            i => i.Id == invoiceId && (!ownerUserId.HasValue || i.CreatorId == ownerUserId.Value), cancellationToken);
        if (invoice == null)
            return Fail(ErrorCodes.InvoiceNotFound, 404);

        if (invoice.Status == InvoiceStatus.Sent)
            return Fail(ErrorCodes.InvoiceAlreadySent, 400);

        // 生成PDF
        var pdfResult = await GeneratePdfAsync(invoiceId, cancellationToken);
        if (!pdfResult.Succeeded)
            return Fail(pdfResult.Message ?? ErrorCodes.InvoiceNotFound);

        // 发送邮件
        var email = recipientEmail ?? invoice.CustomerEmail;
        if (string.IsNullOrEmpty(email))
            return Fail(ErrorCodes.InvoiceRecipientEmailRequired, 400);

        var sendResult = await _notificationService.CreateAndSendAsync(
            new CreateNotificationRequest
            {
                Type = NotificationType.Email,
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

        // 更新发票状态
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
            Invoice = invoice.MapTo<InvoiceDto>(),
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

        // 持久化生成的文件
        var fileName = $"{invoice.InvoiceNo}{fileExtension}";
        var invoiceDir = Path.Combine(AppContext.BaseDirectory, "invoices");
        Directory.CreateDirectory(invoiceDir);
        var localFilePath = Path.Combine(invoiceDir, fileName);
        await File.WriteAllBytesAsync(localFilePath, outputBytes, cancellationToken);

        invoice.PdfFilePath = localFilePath;
        invoice.PdfFileUrl = $"/api/invoices/{invoice.Id}/pdf";

        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        Logger.LogInformation("Invoice generated. InvoiceNo: {InvoiceNo}, Format: {Format}",
            invoice.InvoiceNo, contentType);

        return Ok<string>(invoice.PdfFileUrl ?? string.Empty);
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
    /// 内置 Fallback HTML 渲染（无模板模块依赖）
    /// </summary>
    private static string RenderFallbackHtml(InvoicePdfDto model)
    {
        var inv = model.Invoice;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'/>");
        sb.AppendLine("<style>body{font-family:sans-serif;margin:40px}table{width:100%;border-collapse:collapse}th,td{padding:8px;text-align:left;border-bottom:1px solid #ddd}th{background:#f5f5f5}.right{text-align:right}.total{font-weight:bold;font-size:1.2em}</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine($"<h1>{model.CompanyName}</h1>");
        if (!string.IsNullOrEmpty(model.CompanyAddress))
            sb.AppendLine($"<p>{model.CompanyAddress}</p>");
        sb.AppendLine($"<h2>Invoice #{inv.InvoiceNo}</h2>");
        sb.AppendLine($"<p>Date: {inv.InvoiceDate:yyyy-MM-dd} | Status: {inv.Status} | Currency: {inv.Currency}</p>");
        sb.AppendLine($"<p><strong>Bill To:</strong> {inv.CustomerName}");
        if (!string.IsNullOrEmpty(inv.CustomerEmail))
            sb.Append($" ({inv.CustomerEmail})");
        sb.AppendLine("</p>");
        sb.AppendLine("<table><thead><tr><th>Description</th><th class='right'>Qty</th><th class='right'>Unit Price</th><th class='right'>Amount</th></tr></thead><tbody>");
        foreach (var item in model.LineItems)
        {
            sb.AppendLine($"<tr><td>{item.Description}</td><td class='right'>{item.Quantity:N2}</td><td class='right'>{item.UnitPrice:N2}</td><td class='right'>{item.Amount:N2}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");
        sb.AppendLine($"<p class='right'>Subtotal: {inv.Amount:N2}</p>");
        if (inv.DiscountAmount > 0)
            sb.AppendLine($"<p class='right'>Discount: -{inv.DiscountAmount:N2}</p>");
        if (inv.TaxAmount > 0)
            sb.AppendLine($"<p class='right'>Tax: {inv.TaxAmount:N2}</p>");
        sb.AppendLine($"<p class='right total'>Total Due: {inv.Currency} {inv.DueAmount:N2}</p>");
        if (!string.IsNullOrEmpty(inv.Notes))
            sb.AppendLine($"<p><em>Notes: {inv.Notes}</em></p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    public async Task<Result<string>> GetPdfUrlAsync(Guid invoiceId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(
            i => i.Id == invoiceId && (!ownerUserId.HasValue || i.CreatorId == ownerUserId.Value), cancellationToken);
        if (invoice == null)
            return Fail<string>(ErrorCodes.InvoiceNotFound, 404);

        if (string.IsNullOrEmpty(invoice.PdfFileUrl))
            return await GeneratePdfAsync(invoiceId, cancellationToken);

        return Ok<string>(invoice.PdfFileUrl);
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

    public async Task<Result<InvoiceDto>> GetAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(
            i => i.Id == id && (!ownerUserId.HasValue || i.CreatorId == ownerUserId.Value), cancellationToken);
        if (invoice == null)
            return Fail<InvoiceDto>(ErrorCodes.InvoiceNotFound, 404);

        return Ok(invoice.MapTo<InvoiceDto>());
    }

    public async Task<Result<IPagedList<InvoiceDto>>> GetListAsync(InvoiceQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var queryable = _invoiceRepository.AsNoTracking().Filter(query);

        if (ownerUserId.HasValue)
            queryable = queryable.Where(i => i.CreatorId == ownerUserId.Value);

        var pagedList = await queryable
            .OrderByDescending(i => i.InvoiceDate)
            .ProjectTo<Invoice, InvoiceDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<IPagedList<InvoiceDto>>> GetUserInvoicesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var invoices = await _invoiceRepository.AsNoTracking()
            .Where(i => i.CreatorId == userId)
            .OrderByDescending(i => i.InvoiceDate)
            .ProjectTo<Invoice, InvoiceDto>()
            .CreateAsync(1, 100, cancellationToken);

        return Ok(invoices);
    }
}
