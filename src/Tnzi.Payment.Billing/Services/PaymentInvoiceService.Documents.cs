namespace Tnzi.Payment.Billing.Services;

/// <summary>
/// 发票服务（partial）：票据的内容：卖方快照、明细行、收据与贷项通知单、渲染模型与邮件。
/// </summary>
/// <remarks>
/// <para>
/// ★ 卖方资料与税额拆分在<b>开票那一刻</b>快照到票据上。票据产物会被重新生成（标记已付、作废、补发时缺产物），
/// 若渲染时才读配置，一次改名或换地址就会让三年前的发票印上今天的卖方：那份文件已经不再是当初开出去的那一张。
/// 没有快照的是引入快照之前开的票，按当前配置渲染，与此前相同。
/// </para>
/// <para>
/// ★ 贷项通知单是一张<b>新票据</b>，不改原发票：原发票是「当初收过这笔钱」的凭据，作废它或改它的金额都会丢掉这件事实。
/// 它与原发票不共用 <c>PaymentId</c>（那一列一对一），经 <see cref="Invoice.OriginalInvoiceId"/> 与 <see cref="Invoice.RefundId"/> 关联，
/// 按退款幂等。
/// </para>
/// </remarks>
public partial class PaymentInvoiceService
{
    private static readonly Dictionary<string, string> LogoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".webp"] = "image/webp"
    };

    public async Task<Result<PaymentInvoiceDto>> CreateCreditNoteAsync(CreateCreditNoteDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (request.Amount <= 0)
            return Fail<PaymentInvoiceDto>("The refunded amount must be positive.", 400);

        var existing = await _invoiceRepository.FirstOrDefaultAsync(i => i.RefundId == request.RefundId, cancellationToken);
        if (existing != null)
            return Ok(existing.MapTo<PaymentInvoiceDto>());

        // 冲减的是这笔支付开出的那张票据；没开过票就没有可冲减的东西（不另造一张凭空的贷项）
        var original = await _invoiceRepository.FirstOrDefaultAsync(
            i => i.PaymentId == request.PaymentId && i.Type != InvoiceType.CreditNote, cancellationToken);
        if (original == null)
            return Fail<PaymentInvoiceDto>(ErrorCodes.InvoiceNotFound, 404);

        var now = DateTime.UtcNow;
        var currency = string.IsNullOrWhiteSpace(request.Currency) ? original.Currency : request.Currency.ToUpperInvariant();

        // 税按原票据的含税总额等比例冲回：退了多少钱，就退了其中多少税
        var ratio = original.DueAmount > 0 ? Math.Min(1m, request.Amount / original.DueAmount) : 0m;
        var originalTaxLines = PaymentService.ParseTaxLines(original.TaxLinesJson);
        var creditedTaxLines = originalTaxLines
            .Select(line => new TaxLineDto
            {
                Name = line.Name,
                Rate = line.Rate,
                Amount = CurrencyInfo.Round(line.Amount * ratio, currency),
                RegistrationNumber = line.RegistrationNumber
            })
            .ToList();
        var taxAmount = creditedTaxLines.Count > 0
            ? creditedTaxLines.Sum(l => l.Amount)
            : CurrencyInfo.Round(original.TaxAmount * ratio, currency);
        var netAmount = request.Amount - taxAmount;

        var creditNote = new Invoice
        {
            InvoiceNo = Invoice.GenerateInvoiceNo(),
            Type = InvoiceType.CreditNote,
            // 钱已经退回去了：这是一张已结清的凭据，不是一张等对方处理的单据
            Status = InvoiceStatus.Paid,
            Amount = netAmount,
            Currency = currency,
            TaxAmount = taxAmount,
            DiscountAmount = 0,
            DueAmount = request.Amount,
            PaidAmount = request.Amount,
            PaidDate = request.RefundTime == default ? now : request.RefundTime,
            UserId = original.UserId,
            BillingAccountType = original.BillingAccountType,
            BillingAccountId = original.BillingAccountId,
            CustomerName = original.CustomerName,
            CustomerEmail = original.CustomerEmail,
            CustomerCompany = original.CustomerCompany,
            CustomerTaxId = original.CustomerTaxId,
            CustomerAddress = original.CustomerAddress,
            BillingAddress = original.BillingAddress,
            InvoiceDate = now,
            DueDate = now,
            TemplateName = _invoiceOptions.Value.CreditNoteTemplate,
            Notes = $"Refund {request.RefundNo} against invoice {original.InvoiceNo}.",
            SellerSnapshotJson = await BuildSellerSnapshotJsonAsync(cancellationToken),
            TaxLinesJson = creditedTaxLines.Count > 0 ? JsonSerializer.Serialize(creditedTaxLines, TnziJsonDefaults.Options) : null,
            PaymentMethodBrand = original.PaymentMethodBrand,
            PaymentMethodLast4 = original.PaymentMethodLast4,
            OriginalInvoiceId = original.Id,
            RefundId = request.RefundId
        };

        try
        {
            return await ExecuteInUnitOfWorkAsync(async ct =>
            {
                await _invoiceRepository.InsertAsync(creditNote, ct);
                await _lineItemRepository.InsertAsync(new InvoiceLineItem
                {
                    InvoiceId = creditNote.Id,
                    LineNumber = 1,
                    Description = $"Refund of invoice {original.InvoiceNo}",
                    Quantity = 1,
                    UnitPrice = netAmount,
                    Amount = netAmount,
                    TaxRate = originalTaxLines.Count == 1 ? originalTaxLines[0].Rate : 0,
                    TaxAmount = taxAmount,
                    ProductCode = request.RefundNo
                }, ct);
                await FlushAsync(ct);

                Logger.LogInformation("Credit note {InvoiceNo} issued for refund {RefundNo} against invoice {OriginalInvoiceNo}.",
                    creditNote.InvoiceNo, request.RefundNo, original.InvoiceNo);

                await PublishInvoiceGeneratedAsync(creditNote);
                return Ok(creditNote.MapTo<PaymentInvoiceDto>());
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 同一个退款事件被并发处理两次：另一边先开出了，答它那一张
            var issued = await _invoiceRepository.FirstOrDefaultAsync(i => i.RefundId == request.RefundId, cancellationToken);
            return issued != null
                ? Ok(issued.MapTo<PaymentInvoiceDto>())
                : Fail<PaymentInvoiceDto>(ErrorCodes.InvoiceNotFound, 409);
        }
    }

    /// <summary>业务模块给的明细行（订阅：计划名 + 服务期）；都答不上来时为 null。</summary>
    private async Task<IReadOnlyList<PaymentLineDescription>?> DescribePaymentLinesAsync(PaymentEntity payment, CancellationToken cancellationToken)
    {
        if (_lineDescribers.Count == 0)
            return null;

        var context = new PaymentLineContext
        {
            PaymentId = payment.Id,
            BusinessType = payment.BusinessType,
            BusinessOrderNo = payment.BusinessOrderNo,
            Description = payment.Description,
            ExtraData = payment.ExtraData,
            OriginalAmount = payment.OriginalAmount,
            DiscountAmount = payment.DiscountAmount,
            TaxAmount = payment.TaxAmount,
            Currency = payment.Currency
        };

        foreach (var describer in _lineDescribers)
        {
            var lines = await describer.DescribeAsync(context, cancellationToken);
            if (lines is { Count: > 0 })
                return lines;
        }

        return null;
    }

    private static List<InvoiceLineItem> BuildDescribedLineItems(Guid invoiceId, IReadOnlyList<PaymentLineDescription> lines)
        => lines.Select((line, index) => new InvoiceLineItem
        {
            InvoiceId = invoiceId,
            LineNumber = index + 1,
            Description = line.Description,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            Amount = line.Amount,
            DiscountAmount = line.DiscountAmount,
            TaxRate = 0,
            TaxAmount = line.TaxAmount,
            ProductCode = line.ProductCode,
            PeriodStart = line.PeriodStart,
            PeriodEnd = line.PeriodEnd,
            Kind = line.Kind
        }).ToList();

    /// <summary>此刻的卖方资料，序列化成快照存到票据上。</summary>
    private async Task<string> BuildSellerSnapshotJsonAsync(CancellationToken cancellationToken)
        => JsonSerializer.Serialize(await BuildSellerAsync(cancellationToken), TnziJsonDefaults.Options);

    /// <summary>
    /// 按配置构造卖方资料。没有配置 <c>Seller.Name</c> 时退回四个公司字段（与引入卖方资料之前印的内容相同）。
    /// </summary>
    private async Task<InvoiceSellerDto> BuildSellerAsync(CancellationToken cancellationToken)
    {
        var options = _invoiceOptions.Value;
        var seller = options.Seller ?? new InvoiceSellerOptions();

        if (string.IsNullOrWhiteSpace(seller.Name))
        {
            return new InvoiceSellerDto
            {
                Name = options.CompanyName ?? string.Empty,
                AddressLines = SplitLines(options.CompanyAddress),
                Email = options.CompanyEmail,
                TaxRegistrations = string.IsNullOrWhiteSpace(options.TaxId)
                    ? []
                    : [new InvoiceTaxRegistrationDto { Label = "Tax ID", Number = options.TaxId }],
                LogoDataUri = await LoadLogoAsync(seller, cancellationToken),
                AccentColor = AccentColorOf(seller)
            };
        }

        return new InvoiceSellerDto
        {
            Name = seller.Name.Trim(),
            AddressLines = seller.AddressLines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList(),
            Phone = seller.Phone,
            Email = seller.Email ?? options.CompanyEmail,
            Website = seller.Website,
            TaxRegistrations = seller.TaxRegistrations
                .Where(r => !string.IsNullOrWhiteSpace(r.Number))
                .Select(r => new InvoiceTaxRegistrationDto { Label = r.Label.Trim(), Number = r.Number.Trim() })
                .ToList(),
            LogoDataUri = await LoadLogoAsync(seller, cancellationToken),
            AccentColor = AccentColorOf(seller)
        };
    }

    private static string? AccentColorOf(InvoiceSellerOptions seller)
    {
        var color = seller.AccentColor?.Trim();
        return InvoiceSellerOptions.IsHexColor(color) ? color : null;
    }

    private static List<string> SplitLines(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    /// <summary>
    /// 把标志读成 data URI：快照里嵌的是图本身而不是指向它的路径，换掉标志文件不会改写已开出的票据；
    /// 无头浏览器渲染 PDF 时也不必再去访问一个文件路径。读不到、太大、格式不认识时不嵌，记一条告警。
    /// </summary>
    private async Task<string?> LoadLogoAsync(InvoiceSellerOptions seller, CancellationToken cancellationToken)
    {
        byte[]? bytes = null;
        string? extension = null;

        try
        {
            if (seller.LogoFileId.HasValue)
            {
                if (_fileContentReader == null)
                {
                    Logger.LogWarning("Payment:Invoice:Seller:LogoFileId is set but no file content reader is registered (load the Storage module); the logo is left out.");
                    return null;
                }

                await using var stream = await _fileContentReader.OpenReadAsync(seller.LogoFileId.Value, cancellationToken);
                if (stream == null)
                {
                    Logger.LogWarning("Invoice logo file {FileId} was not found; the logo is left out.", seller.LogoFileId);
                    return null;
                }

                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                bytes = buffer.ToArray();
                extension = DetectImageExtension(bytes);
            }
            else if (!string.IsNullOrWhiteSpace(seller.LogoPath))
            {
                var path = Path.IsPathRooted(seller.LogoPath) ? seller.LogoPath : Path.Combine(AppContext.BaseDirectory, seller.LogoPath);
                if (!File.Exists(path))
                {
                    Logger.LogWarning("Invoice logo {Path} does not exist; the logo is left out.", path);
                    return null;
                }

                bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                extension = Path.GetExtension(path);
            }
        }
        catch (IOException ex)
        {
            Logger.LogWarning(ex, "Invoice logo could not be read; the logo is left out.");
            return null;
        }

        if (bytes == null)
            return null;

        if (bytes.Length > InvoiceSellerOptions.MaxLogoBytes)
        {
            Logger.LogWarning("Invoice logo is {Size} bytes, above the {Limit}-byte limit; the logo is left out.", bytes.Length, InvoiceSellerOptions.MaxLogoBytes);
            return null;
        }

        if (extension == null || !LogoContentTypes.TryGetValue(extension, out var contentType))
        {
            Logger.LogWarning("Invoice logo format {Extension} is not supported (png, jpg, gif, svg, webp); the logo is left out.", extension);
            return null;
        }

        return $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
    }

    /// <summary>Storage 里的标志没有扩展名可看：按文件头认格式。</summary>
    private static string? DetectImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ".png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ".jpg";
        if (bytes.Length >= 6 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F')
            return ".gif";
        if (bytes.Length >= 12 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[8] == (byte)'W' && bytes[9] == (byte)'E')
            return ".webp";
        var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 256)).TrimStart();
        return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
            ? ".svg"
            : null;
    }

    /// <summary>渲染模型：卖方取快照（没有快照的旧票据按当前配置），税额拆分取票据自己的。</summary>
    private async Task<InvoicePdfDto> BuildRenderModelAsync(Invoice invoice, List<InvoiceLineItem> lineItems, CancellationToken cancellationToken)
    {
        var seller = ParseSeller(invoice.SellerSnapshotJson) ?? await BuildSellerAsync(cancellationToken);

        string? originalInvoiceNo = null;
        if (invoice.OriginalInvoiceId.HasValue)
        {
            originalInvoiceNo = await _invoiceRepository.AsNoTracking()
                .Where(i => i.Id == invoice.OriginalInvoiceId.Value)
                .Select(i => i.InvoiceNo)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new InvoicePdfDto
        {
            Invoice = invoice.MapTo<PaymentInvoiceDto>(),
            LineItems = lineItems.MapToList<InvoiceLineItemDto>(),
            Seller = seller,
            // 留给既有模板的四个字段，由卖方资料派生
            CompanyName = seller.Name,
            CompanyAddress = string.Join(", ", seller.AddressLines),
            CompanyEmail = seller.Email ?? string.Empty,
            TaxId = seller.TaxRegistrations.Count > 0 ? seller.TaxRegistrations[0].Number : string.Empty,
            TaxLines = PaymentService.ParseTaxLines(invoice.TaxLinesJson),
            OriginalInvoiceNo = originalInvoiceNo,
            DocumentTitle = DocumentTitleOf(invoice.Type),
            // 快照来自数据库：写进样式表之前再验一次形态
            AccentColor = InvoiceSellerOptions.IsHexColor(seller.AccentColor) ? seller.AccentColor! : InvoiceSellerOptions.DefaultAccentColor
        };
    }

    private static InvoiceSellerDto? ParseSeller(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<InvoiceSellerDto>(json, TnziJsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DocumentTitleOf(InvoiceType type) => type switch
    {
        InvoiceType.Receipt => "Receipt",
        InvoiceType.CreditNote => "Credit Note",
        _ => "Invoice"
    };

    /// <summary>
    /// 这张票据的邮件：模板名、主题、模板渲染不出来时的纯文本、模板变量。发票的主题与正文与引入模板之前相同。
    /// </summary>
    private async Task<DocumentEmail> DescribeDocumentEmailAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var title = DocumentTitleOf(invoice.Type);
        var seller = ParseSeller(invoice.SellerSnapshotJson) ?? await BuildSellerAsync(cancellationToken);
        var amount = invoice.DueAmount.ToString($"N{CurrencyInfo.GetDecimalPlaces(invoice.Currency)}", CultureInfo.InvariantCulture);

        string? originalInvoiceNo = null;
        if (invoice.OriginalInvoiceId.HasValue)
        {
            originalInvoiceNo = await _invoiceRepository.AsNoTracking()
                .Where(i => i.Id == invoice.OriginalInvoiceId.Value)
                .Select(i => i.InvoiceNo)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var (templateName, subject, fallback) = invoice.Type switch
        {
            InvoiceType.Receipt => (InvoiceEmailTemplates.Receipt, $"Receipt {invoice.InvoiceNo}", $"Thank you for your payment. Your receipt {invoice.InvoiceNo} is attached."),
            InvoiceType.CreditNote => (InvoiceEmailTemplates.CreditNote, $"Credit note {invoice.InvoiceNo}", $"Please find attached credit note {invoice.InvoiceNo}."),
            _ => (InvoiceEmailTemplates.Invoice, $"Invoice {invoice.InvoiceNo}", $"Please find attached invoice {invoice.InvoiceNo}.")
        };

        var variables = new Dictionary<string, object>
        {
            ["CustomerName"] = invoice.CustomerName,
            ["DocumentTitle"] = title,
            ["DocumentNo"] = invoice.InvoiceNo,
            ["Amount"] = amount,
            ["Currency"] = invoice.Currency,
            ["SellerName"] = seller.Name,
            ["PaidDate"] = (invoice.PaidDate ?? invoice.InvoiceDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["CardBrand"] = invoice.PaymentMethodBrand ?? string.Empty,
            ["CardLast4"] = invoice.PaymentMethodLast4 ?? string.Empty,
            ["OriginalInvoiceNo"] = originalInvoiceNo ?? string.Empty
        };

        return new DocumentEmail(templateName, subject, fallback, variables);
    }

    /// <summary>一封票据邮件。</summary>
    private sealed record DocumentEmail(string TemplateName, string Subject, string FallbackBody, Dictionary<string, object> Variables);
}
