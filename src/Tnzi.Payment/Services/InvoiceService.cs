namespace Tnzi.Payment.Services;

/// <summary>
/// 发票服务实现
/// </summary>
public class InvoiceService : ApplicationService, IInvoiceService
{
    private readonly IRepository<Invoice, Guid> _invoiceRepository;
    private readonly IRepository<InvoiceLineItem, Guid> _lineItemRepository;
    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly ITemplateEngine? _templateEngine;
    private readonly INotificationService? _notificationService;
    private readonly IOptions<InvoiceOptions> _invoiceOptions;

    public InvoiceService(
        IRepository<Invoice, Guid> invoiceRepository,
        IRepository<InvoiceLineItem, Guid> lineItemRepository,
        IRepository<PaymentEntity, Guid> paymentRepository,
        IOptions<InvoiceOptions> invoiceOptions,
        IServiceProvider serviceProvider,
        ITemplateEngine? templateEngine = null,
        INotificationService? notificationService = null)
        : base(serviceProvider)
    {
        _invoiceRepository = Check.NotNull(invoiceRepository);
        _lineItemRepository = Check.NotNull(lineItemRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _invoiceOptions = Check.NotNull(invoiceOptions);
        _templateEngine = templateEngine;
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

    public async Task<Result> SendAsync(Guid invoiceId, string? recipientEmail, CancellationToken cancellationToken = default)
    {
        if (_notificationService == null)
            return Fail("Notification module is not loaded. Cannot send invoice.", 500);

        var invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
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
        if (_templateEngine == null)
            return Fail<string>("Template module is not loaded. Cannot generate PDF.", 500);

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

        await _templateEngine.RenderAsync(invoice.TemplateName ?? "Standard", model);

        // TODO: Convert HTML to PDF using a library like QuestPDF or Puppeteer
        var pdfPath = $"/invoices/{invoice.InvoiceNo}.pdf";
        invoice.PdfFilePath = pdfPath;
        invoice.PdfFileUrl = $"/api/files{invoice.PdfFilePath}";

        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        Logger.LogInformation("Invoice PDF generated. InvoiceNo: {InvoiceNo}", invoice.InvoiceNo);

        return Ok<string>(invoice.PdfFileUrl ?? string.Empty);
    }

    public async Task<Result<string>> GetPdfUrlAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
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

    public async Task<Result<InvoiceDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice == null)
            return Fail<InvoiceDto>(ErrorCodes.InvoiceNotFound, 404);

        return Ok(invoice.MapTo<InvoiceDto>());
    }

    public async Task<Result<IPagedList<InvoiceDto>>> GetListAsync(InvoiceQueryDto query, CancellationToken cancellationToken = default)
    {
        var pagedList = await _invoiceRepository.AsNoTracking()
            .Filter(query)
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
