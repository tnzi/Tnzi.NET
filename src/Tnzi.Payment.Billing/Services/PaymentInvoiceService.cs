namespace Tnzi.Payment.Billing.Services;

/// <summary>
/// 发票服务实现
/// </summary>
public partial class PaymentInvoiceService : ApplicationService, IPaymentInvoiceService
{
    private readonly IRepository<Invoice, Guid> _invoiceRepository;
    private readonly IRepository<InvoiceLineItem, Guid> _lineItemRepository;
    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly IHtmlToPdfConverter? _pdfConverter;
    private readonly ITemplateRenderService? _templateRenderService;
    private readonly INotificationService? _notificationService;
    private readonly IFileStorageService? _fileStorage;
    private readonly IFileReferenceService? _fileReferences;
    private readonly IOptionsSnapshot<InvoiceOptions> _invoiceOptions;
    private readonly IOptionsMonitor<PaymentOptions> _paymentOptions;
    private readonly IBillingAccountResolver _accountResolver;
    private readonly IReadOnlyList<IPaymentLineDescriber> _lineDescribers;
    private readonly IFileContentReader? _fileContentReader;
    private readonly IBillingAccountService? _billingAccounts;

    public PaymentInvoiceService(
        IRepository<Invoice, Guid> invoiceRepository,
        IRepository<InvoiceLineItem, Guid> lineItemRepository,
        IRepository<PaymentEntity, Guid> paymentRepository,
        IOptionsSnapshot<InvoiceOptions> invoiceOptions,
        IOptionsMonitor<PaymentOptions> paymentOptions,
        IBillingAccountResolver accountResolver,
        IServiceProvider serviceProvider,
        IHtmlToPdfConverter? pdfConverter = null,
        ITemplateRenderService? templateRenderService = null,
        INotificationService? notificationService = null,
        IFileStorageService? fileStorage = null,
        IFileReferenceService? fileReferences = null,
        IEnumerable<IPaymentLineDescriber>? lineDescribers = null,
        IFileContentReader? fileContentReader = null,
        IBillingAccountService? billingAccounts = null)
        : base(serviceProvider)
    {
        _invoiceRepository = Check.NotNull(invoiceRepository);
        _lineItemRepository = Check.NotNull(lineItemRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _invoiceOptions = Check.NotNull(invoiceOptions);
        _paymentOptions = Check.NotNull(paymentOptions);
        _accountResolver = Check.NotNull(accountResolver);
        _pdfConverter = pdfConverter;
        _templateRenderService = templateRenderService;
        _notificationService = notificationService;
        _fileStorage = fileStorage;
        _fileReferences = fileReferences;
        _lineDescribers = lineDescribers?.ToList() ?? [];
        _fileContentReader = fileContentReader;
        _billingAccounts = billingAccounts;
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

        var lineItemError = ValidateLineItems(request.LineItems);
        if (lineItemError != null)
            return Fail<PaymentInvoiceDto>(lineItemError, 400);

        // 客户名称与抬头取计费账户的账单联系人（账户资料 → 消费应用的资料 → 付款人的显示名）：付钱的是账户，
        // 支付上的名字快照只是建单那一刻的经办人，续费单上它一直是开通订阅那个人的登录名。
        // 邮箱优先取支付上的快照：自动开票发生在事件处理器/回调里，那里没有当前用户上下文，
        // 只依赖 CurrentUser 会得到空邮箱，发票永远发不出去。
        var contact = await ResolveCustomerAsync(payment.BillingAccountType, payment.BillingAccountId, payment.UserId, payment.CustomerName, cancellationToken);
        var customerName = Fit(FirstNonEmpty(request.CustomerName, contact?.Name, payment.CustomerName, CurrentUser?.UserName) ?? "Customer", Invoice.CustomerNameMaxLength);
        var customerEmail = FirstNonEmpty(request.CustomerEmail, payment.CustomerEmail, CurrentUser?.Email, contact?.PrimaryEmail);

        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            Logger.LogWarning(
                "Invoice for payment {TradeNo} has no customer email; it will be created but cannot be delivered.",
                payment.TradeNo);
        }

        var now = DateTime.UtcNow;
        var options = _invoiceOptions.Value;
        // 订阅收款开收据（配置打开、调用方没指定种类与模板时）：已付的服务费要的是「收到了多少、哪张卡、哪一期」
        var asReceipt = options.ReceiptsForSubscriptionPayments
            && payment.BusinessType == BusinessType.Subscription
            && request.Type == InvoiceType.Standard
            && request.TemplateName == null;
        var invoice = new Invoice
        {
            InvoiceNo = Invoice.GenerateInvoiceNo(),
            PaymentId = payment.Id,
            Type = asReceipt ? InvoiceType.Receipt : request.Type,
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
            BillingAccountType = payment.BillingAccountType,
            BillingAccountId = payment.BillingAccountId,
            CustomerName = customerName,
            CustomerEmail = customerEmail ?? string.Empty,
            CustomerCompany = Fit(FirstNonEmpty(request.CustomerCompany, contact?.Company), Invoice.CustomerCompanyMaxLength),
            CustomerTaxId = Fit(FirstNonEmpty(request.CustomerTaxId, contact?.TaxId), Invoice.CustomerTaxIdMaxLength),
            CustomerAddress = Fit(FirstNonEmpty(request.CustomerAddress, contact?.Address), Invoice.CustomerAddressMaxLength),
            BillingAddress = request.BillingAddress,
            InvoiceDate = now,
            DueDate = request.DueDate ?? payment.PaidTime ?? now,
            TemplateName = request.TemplateName ?? (asReceipt ? options.ReceiptTemplate : options.DefaultTemplate),
            Notes = request.Notes,
            InternalNotes = request.InternalNotes,
            SellerSnapshotJson = await BuildSellerSnapshotJsonAsync(cancellationToken),
            TaxLinesJson = payment.TaxLinesJson,
            PaymentMethodBrand = payment.PaymentMethodBrand,
            PaymentMethodLast4 = payment.PaymentMethodLast4
        };

        var describedLines = request.LineItems is { Count: > 0 } ? null : await DescribePaymentLinesAsync(payment, cancellationToken);

        // 使用事务保护发票和明细的创建
        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await _invoiceRepository.InsertAsync(invoice, ct);

            var lineItems = request.LineItems is { Count: > 0 }
                ? BuildLineItems(invoice.Id, request.LineItems)
                : describedLines is { Count: > 0 }
                    ? BuildDescribedLineItems(invoice.Id, describedLines)
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

            await PublishInvoiceGeneratedAsync(invoice);

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

        var lineItemError = ValidateLineItems(request.LineItems);
        if (lineItemError != null)
            return Fail<PaymentInvoiceDto>(lineItemError, 400);

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
            // 手工开票：指定了用户就记在他的个人账户上（单客户 / 租户账户的手工发票待管理端支持指定账户）
            BillingAccountType = request.UserId.HasValue ? BillingAccountTypes.User : null,
            BillingAccountId = request.UserId?.ToString(),
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
            InternalNotes = request.InternalNotes,
            SellerSnapshotJson = await BuildSellerSnapshotJsonAsync(cancellationToken)
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

            await PublishInvoiceGeneratedAsync(invoice);

            return Ok(invoice.MapTo<PaymentInvoiceDto>());
        }, cancellationToken);
    }

    /// <summary>
    /// 明细的金额约束：折扣不得超过本行金额，税率是 0-100 的百分数。
    /// </summary>
    /// <remarks>
    /// DTO 上的 <c>[Range]</c> 只在经 MVC 模型绑定进来时生效，而这个 DTO 也被代码直接构造调用；
    /// 折扣大于本行金额会让应付额变成负数，一张「客户倒欠商户」的发票没有任何一方能处理。
    /// </remarks>
    private static string? ValidateLineItems(List<InvoiceLineItemDto>? items)
    {
        if (items == null)
            return null;

        foreach (var item in items)
        {
            if (item.DiscountAmount < 0 || item.DiscountAmount > item.Quantity * item.UnitPrice)
                return "Line item discount must be between 0 and the line amount.";

            if (item.TaxRate is < 0 or > 100)
                return "Line item tax rate must be a percentage between 0 and 100.";
        }

        return null;
    }

    /// <summary>
    /// 发票落库即发布「已生成」事件（两条开票路径共用）。事件总线默认等事务提交后才真正投递。
    /// </summary>
    private async Task PublishInvoiceGeneratedAsync(Invoice invoice)
    {
        if (EventBus == null)
            return;

        await EventBus.PublishAsync(new InvoiceGeneratedEvent
        {
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            PaymentId = invoice.PaymentId,
            Amount = invoice.DueAmount,
            CustomerEmail = invoice.CustomerEmail
        });
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

    /// <summary>
    /// 截到列宽：抬头可能来自消费应用的资料或账户资料（账户地址允许 1000 字），自动开票里一次超长写入
    /// 等于这笔付款永远没有收据。
    /// </summary>
    [return: NotNullIfNotNull(nameof(value))]
    private static string? Fit(string? value, int maxLength)
        => value is { Length: var length } && length > maxLength ? value[..maxLength] : value;

    public async Task<Result> SendAsync(Guid invoiceId, string? recipientEmail, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var sent = await SendCoreAsync(invoiceId, recipientEmail, ownerUserId, firstSendOnly: false, cancellationToken);
        return sent.Succeeded
            ? Ok()
            : Result.Failure(sent.Message ?? ErrorCodes.InvoiceNotFound, sent.Code ?? 500, sent.ErrorCode, sent.ErrorDetails);
    }

    /// <inheritdoc />
    /// <remarks>
    /// ★ 首发名额与补发名额是同一个计数：认领首发就是在「从未发出过（<c>SendCount = 0</c> 且没有发送时间）」这个条件下
    /// 占下第一个名额。此前自动发送先看一眼计数、再调 <see cref="SendAsync"/>，而后者的名额占用只比上限不比首发：
    /// 同一个支付完成事件被并发处理两次（分布式总线至少投递一次）时两边都看到 0，各发一封。
    /// 现在只有一边占得到；发送失败把名额还回去（计数回到 0），之后的重投照样会再试。
    /// </remarks>
    public Task<Result<bool>> SendFirstAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        => SendCoreAsync(invoiceId, null, null, firstSendOnly: true, cancellationToken);

    /// <summary>
    /// 发一封发票。<paramref name="firstSendOnly"/> 为 true 时只在从未发出过时发（见 <see cref="SendFirstAsync"/>），
    /// 已经发过则成功返回 <c>false</c>；否则按补发上限占名额。<c>Data</c> 表示这次是否真的发出。
    /// </summary>
    private async Task<Result<bool>> SendCoreAsync(
        Guid invoiceId, string? recipientEmail, Guid? ownerUserId, bool firstSendOnly, CancellationToken cancellationToken)
    {
        // 501 而不是 500：这不是故障，是这台宿主没装通知模块 —— 500 会让监控与客户端
        // 一直重试一件永远不会好的事。指名要加载哪个模块，别让部署疏漏被读成偶发错误。
        if (_notificationService == null)
        {
            return Fail<bool>(
                "Sending an invoice requires the notification module. "
                + "Load Tnzi.Notification ([DependsOn(typeof(NotificationModule))]) "
                + "or register your own INotificationService.", 501);
        }

        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: false, cancellationToken);
        if (!owner.Succeeded)
            return Fail<bool>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var invoice = await OwnedBy(_invoiceRepository.AsQueryable(), owner.Data)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice == null)
            return Fail<bool>(ErrorCodes.InvoiceNotFound, 404);

        if (invoice.Status == InvoiceStatus.Cancelled)
            return Fail<bool>(ErrorCodes.InvoiceCancelled, 400);

        // 首发：发过（哪怕是管理员手工补发的）就不再自动发
        if (firstSendOnly && (invoice.SendCount > 0 || invoice.LastSentTime != null))
            return Ok(false);

        // 重发次数上限：客服反复补发是常态，但要有上限防止被当成群发通道
        if (invoice.SendCount >= PaymentConstants.MaxInvoiceSendCount)
            return Fail<bool>(ErrorCodes.InvoiceSendLimitReached, 400);

        // 发送邮件
        var email = FirstNonEmpty(recipientEmail, invoice.CustomerEmail);
        if (email == null)
        {
            // 开票时还没有收件人（账户没填账单联系人、付款人令牌不带邮箱声明）：现在再问一次。
            // 问到了就记到发票上，之后的补发与对账看到的是同一个地址。
            email = await ResolveRecipientAsync(invoice.BillingAccountType, invoice.BillingAccountId, invoice.UserId, cancellationToken);
            if (email == null)
                return Fail<bool>(ErrorCodes.InvoiceRecipientEmailRequired, 400);

            var resolved = email;
            await _invoiceRepository.AsQueryable()
                .Where(i => i.Id == invoiceId && (i.CustomerEmail == null || i.CustomerEmail == ""))
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.CustomerEmail, resolved), cancellationToken);
        }

        // ★ 先在库里原子地预留一个发送名额，再生成、再发。上面的检查读的是一份快照：两次并发重发都读到「还剩一个」，
        // 各自发出、各自把读到的计数加一整行写回，上限形同虚设，计数还少记一次。没发出去就把名额还回去。
        // 首发只在「从未发出过」时占得到名额（见 SendFirstAsync）。
        var slots = _invoiceRepository.AsQueryable()
            .Where(i => i.Id == invoiceId && i.Status != InvoiceStatus.Cancelled);
        slots = firstSendOnly
            ? slots.Where(i => i.SendCount == 0 && i.LastSentTime == null)
            : slots.Where(i => i.SendCount < PaymentConstants.MaxInvoiceSendCount);
        var reserved = await slots.ExecuteUpdateAsync(s => s.SetProperty(i => i.SendCount, i => i.SendCount + 1), cancellationToken);

        if (reserved == 0)
        {
            var current = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
            if (current == null)
                return Fail<bool>(ErrorCodes.InvoiceNotFound, 404);

            if (current.Status == InvoiceStatus.Cancelled)
                return Fail<bool>(ErrorCodes.InvoiceCancelled, 400);

            // 首发被另一次处理先占走（或刚刚发出）：不是错误，这一边什么也不做
            return firstSendOnly ? Ok(false) : Fail<bool>(ErrorCodes.InvoiceSendLimitReached, 400);
        }

        Result<string?> delivered;
        try
        {
            delivered = await DeliverAsync(invoiceId, email, cancellationToken);
        }
        catch
        {
            await ReleaseSendSlotAsync(invoiceId);
            throw;
        }

        if (!delivered.Succeeded)
        {
            await ReleaseSendSlotAsync(invoiceId);
            return Result.Failure<bool>(delivered.Message ?? ErrorCodes.InvoiceDocumentUnavailable, delivered.Code ?? 500, delivered.ErrorCode, delivered.ErrorDetails);
        }

        // ★ 只写发送时间与状态两列，且状态只把「还没发出」的推进为已发送：已支付的发票再补发一次不应被降级回 Sent，
        // 那会让一张已收款的发票在账面上重新变成未收款。此前这里把发送之前读的那份发票整行写回，
        // 产物生成与发信那几秒里落地的「标记已付」被写回成 Sent、PaidAmount = 0，而 InvoicePaidEvent 已经发出去了。
        var now = DateTime.UtcNow;
        await _invoiceRepository.AsQueryable()
            .Where(i => i.Id == invoiceId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.LastSentTime, now)
                .SetProperty(i => i.Status, i => i.Status == InvoiceStatus.Draft || i.Status == InvoiceStatus.Pending
                    ? InvoiceStatus.Sent
                    : i.Status)
                .SetProperty(i => i.LastModificationTime, now), cancellationToken);

        Logger.LogInformation("Invoice sent. InvoiceNo: {InvoiceNo}, Recipient: {Email}",
            invoice.InvoiceNo, email);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new InvoiceSentEvent
            {
                InvoiceId = invoice.Id,
                InvoiceNo = invoice.InvoiceNo,
                RecipientEmail = email,
                SentTime = now,
                PdfUrl = delivered.Data
            });
        }

        return Ok(true);
    }

    /// <summary>
    /// 把发票产物作为附件发给 <paramref name="email"/>，成功时带回产物的下载地址（没有时为 null）。
    /// 不改发送计数与状态，那由调用方按结果处理。
    /// </summary>
    /// <remarks>
    /// ★ 产物已在就直接附上，不再每发一次重新生成一份：每次重新生成都在 Storage 里新存一个文件，
    /// 此前旧的那份引用从不解除，重发几次就留下几份永远回收不了的副本。产物何时作废由改变它内容的那些操作负责
    /// （标记已付 / 作废，见 <see cref="ClearDocumentPointers"/>）。
    /// </remarks>
    private async Task<Result<string?>> DeliverAsync(Guid invoiceId, string email, CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice == null)
            return Fail<string?>(ErrorCodes.InvoiceNotFound, 404);

        if (!HasDocument(invoice) || !InCurrentFormat(invoice))
        {
            // 生成产物（落 Storage 或本地磁盘）
            var pdfResult = await GeneratePdfAsync(invoiceId, cancellationToken);
            if (!pdfResult.Succeeded)
                return Fail<string?>(pdfResult.Message ?? ErrorCodes.InvoiceDocumentUnavailable, pdfResult.Code ?? 500);

            // ★ 仓储查询不跟踪：上面读的那一份是产物生成**之前**的快照，PdfFileId / PdfFilePath 还是空的。
            //   附件必须用重读的这一份。
            invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
            if (invoice == null)
                return Fail<string?>(ErrorCodes.InvoiceNotFound, 404);
        }

        // ★ 附件就是刚生成的那份产物。此前这里一条附件都没挂：正文写着「请查收附件」，
        //   邮件却是空的，发票照样记为 Sent、SendCount 递增、日志「Invoice sent」。
        //   Storage 承载的挂 FileId（字节由通知模块经核心的 IFileContentReader 以系统身份读出；
        //   PdfFilePath 那时是存储相对键，对发送器没有意义，不挂），本地回退的挂绝对路径
        //   （通知模块按 Notification:Attachments:AllowedLocalRoots 放行，默认全拒 —— 拒绝会让这次发送失败，
        //   而不是发出一封没有附件的信）。
        var attachment = DescribeDocumentAttachment(invoice);
        if (attachment == null)
        {
            Logger.LogError("Invoice document was generated but neither a file id nor a local path was recorded. InvoiceNo: {InvoiceNo}", invoice.InvoiceNo);
            return Fail<string?>(ErrorCodes.InvoiceDocumentUnavailable, 500);
        }

        var message = await DescribeDocumentEmailAsync(invoice, cancellationToken);
        var sendResult = await _notificationService!.CreateAndSendAsync(
            new CreateNotificationRequest
            {
                Type = NotificationType.Email,
                // 事务性：账单是既有付费关系下必需的往来，不是营销。
                IsTransactional = true,
                // 模板（Templates/Notification/Email/）渲染不出来时退回这段纯文本，与引入模板之前的正文相同
                TemplateName = message.TemplateName,
                TemplateVariables = message.Variables,
                Subject = message.Subject,
                Content = $"<p>{WebUtility.HtmlEncode(message.FallbackBody)}</p>",
                IsHtml = true,
                SendImmediately = true,
                Recipients =
                [
                    new RecipientInput
                    {
                        Address = email,
                        Name = invoice.CustomerName
                    }
                ],
                Attachments = [attachment]
            },
            cancellationToken);

        return sendResult.Succeeded
            ? Ok<string?>(invoice.PdfFileUrl)
            : Fail<string?>(sendResult.Message ?? "Failed to send invoice email.");
    }

    /// <summary>
    /// 把 <see cref="SendAsync"/> 预留的发送名额还回去（这一次没发出去）。不随请求取消：名额漏还就永久少一次。
    /// </summary>
    private Task ReleaseSendSlotAsync(Guid invoiceId)
        => _invoiceRepository.AsQueryable()
            .Where(i => i.Id == invoiceId && i.SendCount > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.SendCount, i => i.SendCount - 1), CancellationToken.None);

    /// <summary>
    /// 票据收件人的兜底：账户的账单联系人，没有就是发起这笔付款的用户账号上的邮箱。都没有返回 null。
    /// </summary>
    private async Task<string?> ResolveRecipientAsync(string? accountType, string? accountId, Guid? userId, CancellationToken cancellationToken)
        => (await ResolveCustomerAsync(accountType, accountId, userId, null, cancellationToken))?.PrimaryEmail;

    /// <summary>
    /// 计费账户的账单联系人（见 <see cref="IBillingAccountService.ResolveContactAsync(BillingAccountRef?, string?, string?, Guid?, CancellationToken)"/>）；
    /// 没有加载支付核心的账户服务时返回 null。
    /// </summary>
    private async Task<BillingContact?> ResolveCustomerAsync(string? accountType, string? accountId, Guid? userId, string? fallbackName, CancellationToken cancellationToken)
    {
        if (_billingAccounts == null)
            return null;

        var account = string.IsNullOrWhiteSpace(accountType) || string.IsNullOrWhiteSpace(accountId)
            ? null
            : new BillingAccountRef(accountType, accountId);
        return await _billingAccounts.ResolveContactAsync(account, fallbackName, null, userId, cancellationToken);
    }

    public Task<Result<string>> GeneratePdfAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        => GenerateDocumentAsync(invoiceId, isRetry: false, cancellationToken);

    /// <summary>
    /// 生成产物并换上指针。<paramref name="isRetry"/> 为 true 表示这是状态变化让上一份没装上之后的那一次重来（只重来一次）。
    /// </summary>
    private async Task<Result<string>> GenerateDocumentAsync(Guid invoiceId, bool isRetry, CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice == null)
            return Fail<string>(ErrorCodes.InvoiceNotFound, 404);

        var lineItems = await _lineItemRepository.AsNoTracking()
            .Where(l => l.InvoiceId == invoiceId)
            .OrderBy(l => l.LineNumber)
            .ToListAsync(cancellationToken);

        var model = await BuildRenderModelAsync(invoice, lineItems, cancellationToken);

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
        var previousFileId = invoice.PdfFileId;
        Guid? pdfFileId = null;
        string? pdfFilePath;

        if (_fileStorage != null)
        {
            using var stream = new MemoryStream(outputBytes);
            var saveResult = await _fileStorage.SaveWithReferenceAsync(
                fileName, stream, nameof(Invoice), invoice.Id, nameof(Invoice.PdfFileId));

            if (saveResult.Succeeded && saveResult.Data != null)
            {
                pdfFileId = saveResult.Data.Id;
                pdfFilePath = saveResult.Data.Path;

                // ★ 先把文件记录与引用落库，再换指针。宿主开着请求级工作单元（EnableGlobalUnitOfWork）时，
                //   存储的插入停在变更跟踪器里，而下面换指针是裸 ExecuteUpdate：物理事务要到第一次保存才开启，
                //   于是指针在自动提交模式下先落了库，记录却还没有。同一请求里紧接着读这份产物，存储的读权限判据看到的是
                //   一条没盖上创建人、引用也还不在库里的记录，答「找不到」；请求以失败结束、工作单元回滚，
                //   记录与引用随之消失，发票却永远指着一个不存在的文件。flush 经工作单元开启物理事务，
                //   记录、引用与指针从此同进同退：要么一起提交，要么一起撤销（下一次下载再生成一份）。
                //   没有工作单元时插入本来就立即落库，这一步什么都不做。
                await FlushAsync(cancellationToken);
            }
            else
            {
                // 回退本地时 PdfFileId 必须一起清掉：此前它留着上一份存储产物的 Id，
                // 附件与下载都按 Id 优先，于是发出去、下载到的仍是旧的那一份。
                Logger.LogWarning("Invoice PDF storage save failed, falling back to local file. InvoiceNo: {InvoiceNo}, Error: {Error}",
                    invoice.InvoiceNo, saveResult.Message);
                pdfFilePath = await WriteLocalFallbackAsync(fileName, outputBytes, cancellationToken);
            }
        }
        else
        {
            pdfFilePath = await WriteLocalFallbackAsync(fileName, outputBytes, cancellationToken);
        }

        // ★ 这里此前写的是本端点自己的路径（"/api/invoices/{id}/pdf"），而那个端点返回的正是这个字符串 ——
        // 一条指向自己的链接，任何客户端顺着它走都只会拿到同一个字符串，产物永远下载不到。
        // 真正可下载的地址只有 Storage 给得出来；本地回退没有 HTTP 可达的地址，
        // 此时留空并由 /invoices/{id}/pdf 直接吐文件流（见 GetPdfContentAsync）。
        var pdfFileUrl = pdfFileId.HasValue && _fileStorage != null
            ? (await _fileStorage.GetUrlAsync(pdfFileId.Value)) is { Succeeded: true } url ? url.Data : null
            : null;

        // ★ 只写产物指针这三列。上面渲染、转换（无头浏览器，秒级）、存储期间发票照常可写：
        // 此前把开头读的那份快照整行写回，这期间落地的「标记已付 / 作废」被写回成未付 / 原状态，
        // 而已付事件早已发出。
        // 且只在指针仍是开头读到的那一个时才换：这期间另一次生成先落了地，或标记已付 / 作废让产物作废，
        // 这一份就不是该留下的那份。两份都写进同一列，后写的赢、先写的那份引用永远解除不了。
        // ★ 只比指针不够：开头读到的指针是空的（还没生成过，或本地回退只有路径）时，标记已付 / 作废清指针什么都没改变，
        //   这一份印着旧状态（「待付」、零已付）却照样装得上去，客户下载到的就是一张已付或已作废发票的待付账单。
        //   所以还要求发票没有在这期间进入 Paid / Cancelled（改变产物内容的只有这两次状态变化；发送把 Draft 推进为 Sent 不算）。
        var renderedStatus = invoice.Status;
        var swapped = await _invoiceRepository.AsQueryable()
            .Where(i => i.Id == invoiceId && i.PdfFileId == previousFileId)
            .Where(i => i.Status == renderedStatus
                || (i.Status != InvoiceStatus.Paid && i.Status != InvoiceStatus.Cancelled))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.PdfFileId, pdfFileId)
                .SetProperty(i => i.PdfFilePath, pdfFilePath)
                .SetProperty(i => i.PdfFileUrl, pdfFileUrl), cancellationToken);

        if (swapped == 0)
        {
            await ReleaseDocumentReferenceAsync(pdfFileId, invoiceId);

            var current = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
            if (current == null)
                return Fail<string>(ErrorCodes.InvoiceNotFound, 404);

            Logger.LogInformation("Invoice document changed while it was being generated; the newer state wins. InvoiceNo: {InvoiceNo}", invoice.InvoiceNo);

            if (HasDocument(current))
                return Ok<string>(current.PdfFileUrl ?? string.Empty);

            // 没装上是因为这期间进了 Paid / Cancelled，而还没有别人按新状态生成过：按新状态重来一次，
            // 调用方（下载、发送）拿到的是一份印着当前状态的产物，而不是一个 409。只重来一次，免得一直被并发写追着跑。
            return !isRetry && current.Status != renderedStatus
                ? await GenerateDocumentAsync(invoiceId, isRetry: true, cancellationToken)
                : Fail<string>(ErrorCodes.InvoiceDocumentUnavailable, 409);
        }

        // 被换下的旧产物不再被这张发票引用：解除引用，交给存储的孤儿回收。此前每生成一次就在存储里多留一份。
        if (previousFileId.HasValue && previousFileId != pdfFileId)
            await ReleaseDocumentReferenceAsync(previousFileId, invoiceId);

        Logger.LogInformation("Invoice generated. InvoiceNo: {InvoiceNo}, Format: {Format}, StorageBacked: {StorageBacked}",
            invoice.InvoiceNo, contentType, pdfFileId != null);

        return Ok<string>(pdfFileUrl ?? string.Empty);
    }

    /// <summary>
    /// 发票是否已有可用的产物：Storage 承载的按 Id 判，本地回退的看磁盘上那个文件还在不在。
    /// </summary>
    private static bool HasDocument(Invoice invoice)
        => invoice.PdfFileId.HasValue
           || (!string.IsNullOrEmpty(invoice.PdfFilePath) && File.Exists(invoice.PdfFilePath));

    /// <summary>
    /// 已有的产物是不是这台宿主现在出的格式（按存下的路径扩展名比对转换器的扩展名；没有路径时无从判断，按是处理）。
    /// </summary>
    /// <remarks>
    /// ★ 宿主后来装上了真正的 PDF 转换器时，此前生成的产物仍是 HTML 文件；内容类型与文件名却按当前转换器给出，
    /// 不重新生成的话下载与附件会把 HTML 字节标成 <c>application/pdf</c>。换格式即视为没有产物，下一次用到时按新格式重出。
    /// </remarks>
    private bool InCurrentFormat(Invoice invoice)
        => string.IsNullOrEmpty(invoice.PdfFilePath)
           || string.Equals(Path.GetExtension(invoice.PdfFilePath), DocumentFormat().Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 解除这张发票对某份产物文件的引用（引用计数归零后由存储的孤儿回收收走）。未加载存储模块时没有引用可解。
    /// </summary>
    private async Task ReleaseDocumentReferenceAsync(Guid? fileId, Guid invoiceId)
    {
        if (fileId == null || _fileReferences == null)
            return;

        var released = await _fileReferences.UpdateReferenceAsync(fileId, null, nameof(Invoice), invoiceId, nameof(Invoice.PdfFileId));
        if (!released.Succeeded)
        {
            Logger.LogWarning("Failed to release a replaced invoice document. InvoiceId: {InvoiceId}, FileId: {FileId}, Error: {Error}",
                invoiceId, fileId, released.Message);
        }
    }

    /// <summary>
    /// 产物的文件扩展名与 MIME 类型：有 PDF 转换器时按转换器说的算，否则是渲染出的 HTML 本身。
    /// </summary>
    private (string Extension, string ContentType) DocumentFormat()
        => _pdfConverter != null ? (_pdfConverter.FileExtension, _pdfConverter.ContentType) : (".html", "text/html");

    /// <summary>
    /// 把发票产物描述成通知附件：Storage 承载的按 FileId，本地回退的按绝对路径；两者都没有时为 null。
    /// </summary>
    private FileInfoDto? DescribeDocumentAttachment(Invoice invoice)
    {
        var (extension, contentType) = DocumentFormat();
        var fileName = $"{invoice.InvoiceNo}{extension}";

        if (invoice.PdfFileId.HasValue)
        {
            return new FileInfoDto
            {
                FileId = invoice.PdfFileId,
                FileName = fileName,
                FilePath = string.Empty,
                ContentType = contentType
            };
        }

        if (!string.IsNullOrEmpty(invoice.PdfFilePath))
        {
            return new FileInfoDto
            {
                FileName = fileName,
                FilePath = invoice.PdfFilePath,
                ContentType = contentType
            };
        }

        return null;
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
                templateName, "Payment", model, "Invoice", null, TemplateOutputKind.Html, cancellationToken);

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
        if (!string.IsNullOrEmpty(model.Seller.LogoDataUri))
            sb.AppendLine($"<img src='{Escape(model.Seller.LogoDataUri)}' alt='' style='max-height:64px'/>");
        sb.AppendLine($"<h1>{Escape(model.CompanyName)}</h1>");
        foreach (var line in model.Seller.AddressLines)
            sb.AppendLine($"<p>{Escape(line)}</p>");
        foreach (var registration in model.Seller.TaxRegistrations)
            sb.AppendLine($"<p>{Escape(registration.Label)}: {Escape(registration.Number)}</p>");
        sb.AppendLine($"<h2>{Escape(model.DocumentTitle)} #{Escape(inv.InvoiceNo)}</h2>");
        if (!string.IsNullOrEmpty(model.OriginalInvoiceNo))
            sb.AppendLine($"<p>Credits invoice #{Escape(model.OriginalInvoiceNo)}</p>");
        sb.AppendLine($"<p>Date: {inv.InvoiceDate:yyyy-MM-dd} | Status: {inv.Status} | Currency: {Escape(inv.Currency)}</p>");
        sb.AppendLine($"<p><strong>Bill To:</strong> {Escape(inv.CustomerName)}");
        if (!string.IsNullOrEmpty(inv.CustomerEmail))
            sb.Append($" ({Escape(inv.CustomerEmail)})");
        sb.AppendLine("</p>");
        sb.AppendLine("<table><thead><tr><th>Description</th><th class='right'>Qty</th><th class='right'>Unit Price</th><th class='right'>Amount</th></tr></thead><tbody>");
        foreach (var item in model.LineItems)
        {
            var period = item.PeriodStart.HasValue && item.PeriodEnd.HasValue
                ? $"<br/><small>{item.PeriodStart.Value:yyyy-MM-dd} to {item.PeriodEnd.Value:yyyy-MM-dd}</small>"
                : string.Empty;
            sb.AppendLine($"<tr><td>{Escape(item.Description)}{period}</td><td class='right'>{item.Quantity:N2}</td><td class='right'>{item.UnitPrice:N2}</td><td class='right'>{item.Amount:N2}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");
        sb.AppendLine($"<p class='right'>Subtotal: {inv.Amount:N2}</p>");
        if (inv.DiscountAmount > 0)
            sb.AppendLine($"<p class='right'>Discount: -{inv.DiscountAmount:N2}</p>");
        if (model.TaxLines.Count > 0)
        {
            foreach (var tax in model.TaxLines)
            {
                var registration = string.IsNullOrEmpty(tax.RegistrationNumber) ? string.Empty : $" ({Escape(tax.RegistrationNumber)})";
                sb.AppendLine($"<p class='right'>{Escape(tax.Name)} {tax.Rate:0.##}%{registration}: {tax.Amount:N2}</p>");
            }
        }
        else if (inv.TaxAmount > 0)
        {
            sb.AppendLine($"<p class='right'>Tax: {inv.TaxAmount:N2}</p>");
        }
        if (!string.IsNullOrEmpty(inv.PaymentMethodLast4))
            sb.AppendLine($"<p>Paid with {Escape(inv.PaymentMethodBrand ?? "card")} ending in {Escape(inv.PaymentMethodLast4)}</p>");
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
        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: false, cancellationToken);
        if (!owner.Succeeded)
            return Fail<string>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var invoice = await OwnedBy(_invoiceRepository.AsQueryable(), owner.Data)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice == null)
            return Fail<string>(ErrorCodes.InvoiceNotFound, 404);

        // 产物是旧格式（宿主后来才装上 PDF 转换器）：按现在的格式重出，地址随之换成新产物的
        if (HasDocument(invoice) && !InCurrentFormat(invoice))
            return await GeneratePdfAsync(invoiceId, cancellationToken);

        if (!string.IsNullOrEmpty(invoice.PdfFileUrl))
            return Ok<string>(invoice.PdfFileUrl);

        // ★ 地址只有 Storage 给得出来。未加载 Storage 时这里恒答空串，此前却每次都整套重新生成一遍
        //   （模板渲染 + 无头浏览器转换 + 写盘 + 写库）再答空串：任何登录用户轮询自己的发票就能把 CPU 放大。
        if (_fileStorage == null)
            return Ok<string>(string.Empty);

        // 产物在、只是地址没取到（例如在没有用户上下文的后台里生成）：现取地址，不重新生成
        if (invoice.PdfFileId.HasValue)
        {
            var url = await _fileStorage.GetUrlAsync(invoice.PdfFileId.Value);
            if (url is not { Succeeded: true, Data: not null })
                return Ok<string>(string.Empty);

            var fileId = invoice.PdfFileId;
            var address = url.Data;
            await _invoiceRepository.AsQueryable()
                .Where(i => i.Id == invoiceId && i.PdfFileId == fileId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.PdfFileUrl, address), cancellationToken);

            return Ok<string>(address);
        }

        // 存储保存失败时回退到了本地磁盘：那份同样没有 HTTP 可达的地址，不为它反复重新生成
        if (HasDocument(invoice))
            return Ok<string>(string.Empty);

        return await GeneratePdfAsync(invoiceId, cancellationToken);
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
        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: false, cancellationToken);
        if (!owner.Succeeded)
            return Fail<InvoiceDocumentDto>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var invoice = await OwnedBy(_invoiceRepository.AsQueryable(), owner.Data)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice == null)
            return Fail<InvoiceDocumentDto>(ErrorCodes.InvoiceNotFound, 404);

        if ((invoice.PdfFileId == null && string.IsNullOrEmpty(invoice.PdfFilePath)) || !InCurrentFormat(invoice))
        {
            var generated = await GeneratePdfAsync(invoiceId, cancellationToken);
            if (!generated.Succeeded)
                return Fail<InvoiceDocumentDto>(generated.Message ?? ErrorCodes.InvoiceDocumentUnavailable, generated.Code ?? 400);

            invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
            if (invoice == null)
                return Fail<InvoiceDocumentDto>(ErrorCodes.InvoiceNotFound, 404);
        }

        var document = await ReadDocumentAsync(invoice, cancellationToken);
        if (document != null)
            return Ok(document);

        // ★ 发票记着一份产物、那份产物却读不到（记录不在存储里、本地文件也不在）：产物是从发票自己的快照渲染的，
        //   重新生成得到的是同一张单据。此前这里直接答 404，一张存在的收据就此再也下载不到；指针就是那样悬空的
        //   （见 GenerateDocumentAsync 里换指针之前的 flush），存量数据里的这种行由这一步自愈。只重来一次。
        var staleFileId = invoice.PdfFileId;
        var stalePath = invoice.PdfFilePath;
        await _invoiceRepository.AsQueryable()
            .Where(i => i.Id == invoiceId && i.PdfFileId == staleFileId && i.PdfFilePath == stalePath)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.PdfFileId, (Guid?)null)
                .SetProperty(i => i.PdfFilePath, (string?)null)
                .SetProperty(i => i.PdfFileUrl, (string?)null), cancellationToken);
        await ReleaseDocumentReferenceAsync(staleFileId, invoiceId);

        Logger.LogWarning("Invoice document could not be read; generating it again. InvoiceNo: {InvoiceNo}, FileId: {FileId}", invoice.InvoiceNo, staleFileId);

        var regenerated = await GeneratePdfAsync(invoiceId, cancellationToken);
        if (!regenerated.Succeeded)
            return Fail<InvoiceDocumentDto>(regenerated.Message ?? ErrorCodes.InvoiceDocumentUnavailable, regenerated.Code ?? 404);

        invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        document = invoice == null ? null : await ReadDocumentAsync(invoice, cancellationToken);
        if (document != null)
            return Ok(document);

        Logger.LogWarning("Invoice document is missing on disk and in storage. InvoiceNo: {InvoiceNo}", invoice?.InvoiceNo);
        return Fail<InvoiceDocumentDto>(ErrorCodes.InvoiceDocumentUnavailable, 404);
    }

    /// <summary>
    /// 读出发票当前记着的那份产物（存储优先，其次本地回退）；读不到返回 null。
    /// </summary>
    private async Task<InvoiceDocumentDto?> ReadDocumentAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var (extension, contentType) = DocumentFormat();

        if (invoice.PdfFileId.HasValue && _fileStorage != null)
        {
            var stored = await _fileStorage.GetAsync(invoice.PdfFileId.Value);
            if (stored is { Succeeded: true, Data: not null })
            {
                using var buffer = new MemoryStream();
                await stored.Data.CopyToAsync(buffer, cancellationToken);
                await stored.Data.DisposeAsync();

                return new InvoiceDocumentDto
                {
                    Content = buffer.ToArray(),
                    ContentType = contentType,
                    FileName = $"{invoice.InvoiceNo}{extension}"
                };
            }

            Logger.LogWarning("Invoice document is recorded in storage but could not be read. InvoiceNo: {InvoiceNo}, FileId: {FileId}, Error: {Error}",
                invoice.InvoiceNo, invoice.PdfFileId, stored.Message);
        }

        if (!string.IsNullOrEmpty(invoice.PdfFilePath) && File.Exists(invoice.PdfFilePath))
        {
            return new InvoiceDocumentDto
            {
                Content = await File.ReadAllBytesAsync(invoice.PdfFilePath, cancellationToken),
                ContentType = contentType,
                FileName = $"{invoice.InvoiceNo}{extension}"
            };
        }

        return null;
    }

    public async Task<Result> MarkAsPaidAsync(Guid invoiceId, MarkInvoicePaidDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        // 条件更新（WHERE 还没付、也没作废）而不是读出来再整行写回：两次并发的「标记已付」只有一次生效、只发一次已付事件；
        // 也不会把读与写之间落地的产物指针或别的字段写回旧值。
        // ★ 作废的发票不能再标记已付（与作废拒绝已付对称）：此前只拒 Paid，作废的票号经 API 以「已付」重现
        //   并发出 InvoicePaidEvent，对账里多出一笔谁也解释不了的收入。
        // ★ 操作员填的备注追加进 InternalNotes（与作废原因同一处）：此前收下备注、答 200、一个字都没存。
        // ★ 同一条语句里让产物作废：上面印着的是旧状态与零已付，下次取用时按新状态重新生成。
        var note = string.IsNullOrWhiteSpace(request.Remark) ? null : $"Paid: {request.Remark.Trim()}";
        var paidDate = DateTime.UtcNow;
        var change = await ChangeStatusAsync(
            invoiceId,
            i => i.Status != InvoiceStatus.Paid && i.Status != InvoiceStatus.Cancelled,
            note,
            s => s.SetProperty(i => i.Status, InvoiceStatus.Paid)
                .SetProperty(i => i.PaidAmount, request.PaidAmount)
                .SetProperty(i => i.PaidDate, paidDate)
                .SetProperty(i => i.LastModificationTime, paidDate),
            cancellationToken);

        var invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice == null || change.Outcome == StatusChangeOutcome.NotFound)
            return Fail(ErrorCodes.InvoiceNotFound, 404);

        if (change.Outcome == StatusChangeOutcome.Contended)
            return Fail(InvoiceChangedConcurrentlyMessage, 409);

        if (change.Outcome != StatusChangeOutcome.Applied)
        {
            return invoice.Status == InvoiceStatus.Cancelled
                ? Fail(ErrorCodes.InvoiceCancelled, 400)
                : Fail(ErrorCodes.InvoiceAlreadyPaid, 400);
        }

        await ReleaseDocumentReferenceAsync(change.ClearedFileId, invoiceId);

        // 发布发票支付事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new InvoicePaidEvent
            {
                InvoiceId = invoice.Id,
                InvoiceNo = invoice.InvoiceNo,
                PaidAmount = request.PaidAmount,
                PaidTime = paidDate,
                PaymentId = invoice.PaymentId
            });
        }

        Logger.LogInformation("Invoice marked as paid. InvoiceNo: {InvoiceNo}, Amount: {Amount}",
            invoice.InvoiceNo, request.PaidAmount);

        return Ok();
    }

    public async Task<Result> CancelAsync(Guid invoiceId, string? reason, CancellationToken cancellationToken = default)
    {
        // 条件更新（WHERE 还没付）：读出来再判断、再整行写回，读与写之间被标记已付的发票照样会被作废，
        // 一张已收款的发票在账面上凭空少一笔收入。备注追加在读到的那一版上，写入时核对它没变（见 ChangeStatusAsync）。
        // ★ 同一条语句里让产物作废：此前作废的发票照样下载成一张待付账单。
        // 原因去掉首尾空白再记：末尾的空格在 SQL Server 上不计入 LEN，会让按长度判断的拼接越过列宽。
        var trimmedReason = reason?.Trim();
        var note = string.IsNullOrEmpty(trimmedReason) ? null : $"Cancelled: {trimmedReason}";
        var now = DateTime.UtcNow;
        var change = await ChangeStatusAsync(
            invoiceId,
            i => i.Status != InvoiceStatus.Paid,
            note,
            s => s.SetProperty(i => i.Status, InvoiceStatus.Cancelled)
                .SetProperty(i => i.LastModificationTime, now),
            cancellationToken);

        var invoice = await _invoiceRepository.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice == null || change.Outcome == StatusChangeOutcome.NotFound)
            return Fail(ErrorCodes.InvoiceNotFound, 404);

        if (change.Outcome == StatusChangeOutcome.Contended)
            return Fail(InvoiceChangedConcurrentlyMessage, 409);

        if (change.Outcome != StatusChangeOutcome.Applied)
            return Fail(ErrorCodes.InvoiceCannotCancel, 400);

        await ReleaseDocumentReferenceAsync(change.ClearedFileId, invoiceId);

        Logger.LogInformation("Invoice cancelled. InvoiceNo: {InvoiceNo}, Reason: {Reason}",
            invoice.InvoiceNo, trimmedReason);

        return Ok();
    }

    /// <summary>读写之间发票一直在变、几次重读都没能写进去时的答复。</summary>
    private const string InvoiceChangedConcurrentlyMessage = "The invoice changed while it was being updated. Reload and retry.";

    /// <summary>改状态的条件写最多重读几次（产物指针或备注在读与写之间被别人换了）。</summary>
    private const int MaxStatusChangeAttempts = 5;

    /// <summary><see cref="ChangeStatusAsync"/> 的结局。</summary>
    private enum StatusChangeOutcome
    {
        /// <summary>写进去了。</summary>
        Applied,

        /// <summary>发票不存在。</summary>
        NotFound,

        /// <summary>发票此刻的状态不允许这次改动（已付 / 已作废）。</summary>
        NotEligible,

        /// <summary>读与写之间发票一再被改，重读几次都没写进去。</summary>
        Contended
    }

    /// <summary>
    /// 标记已付 / 作废共用的那一条条件写：改状态、让产物作废、可选追加一条内部备注。
    /// 返回结局，以及被这次写清掉的产物文件 Id（由调用方解除引用）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 清掉的是哪一份产物，必须就是写入时库里的那一份。此前先读产物 Id、再在一条只按状态过滤的语句里清指针：
    /// 两步之间落地的一次产物生成写进了新的 <c>PdfFileId</c>，语句把它清掉，解除引用的却是先前读到的旧 Id，
    /// 新文件的引用永远没人解除，成了回收不掉的孤儿文件。现在把读到的产物 Id 一起写进条件里（读到 null 时就要求仍是 null），
    /// 不一致就重读再来。
    /// </para>
    /// <para>
    /// ★ 备注在内存里按精确长度拼好再写（<see cref="AppendInternalNote"/>），写入时核对备注仍是读到的那一版。
    /// 此前在 SQL 里按 <c>InternalNotes.Length</c> 判断放不放得下：SQL Server 的 <c>LEN</c> 不计末尾空格，
    /// 原备注以空格结尾、又接近列宽时被判成放得下，拼出来超出列宽，标记已付或作废答 500。
    /// </para>
    /// </remarks>
    private async Task<(StatusChangeOutcome Outcome, Guid? ClearedFileId)> ChangeStatusAsync(
        Guid invoiceId,
        Expression<Func<Invoice, bool>> eligible,
        string? note,
        Action<UpdateSettersBuilder<Invoice>> setStatus,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxStatusChangeAttempts; attempt++)
        {
            var current = await _invoiceRepository.AsNoTracking()
                .Where(i => i.Id == invoiceId)
                .Select(i => new { i.PdfFileId, i.InternalNotes })
                .FirstOrDefaultAsync(cancellationToken);
            if (current == null)
                return (StatusChangeOutcome.NotFound, null);

            var fileId = current.PdfFileId;
            var notes = current.InternalNotes;
            var target = _invoiceRepository.AsQueryable()
                .Where(eligible)
                .Where(i => i.Id == invoiceId && i.PdfFileId == fileId);

            string? nextNotes = null;
            if (note != null)
            {
                nextNotes = AppendInternalNote(notes, note);
                target = target.Where(i => i.InternalNotes == notes);
            }

            var applied = await target.ExecuteUpdateAsync(s =>
            {
                setStatus(s);
                if (nextNotes != null)
                    s.SetProperty(i => i.InternalNotes, nextNotes);
                ClearDocumentPointers(s);
            }, cancellationToken);

            if (applied > 0)
                return (StatusChangeOutcome.Applied, fileId);

            // 没写进去：要么状态已不允许（交给调用方按现状回答），要么产物指针 / 备注在读与写之间被换了（重读再来）
            var stillEligible = await _invoiceRepository.AsNoTracking()
                .Where(i => i.Id == invoiceId)
                .Where(eligible)
                .AnyAsync(cancellationToken);
            if (!stillEligible)
                return (StatusChangeOutcome.NotEligible, null);
        }

        Logger.LogWarning("Invoice {InvoiceId} kept changing while its status was being updated; giving up after {Attempts} attempts.",
            invoiceId, MaxStatusChangeAttempts);
        return (StatusChangeOutcome.Contended, null);
    }

    /// <summary><see cref="Invoice.InternalNotes"/> 的列宽（见 <c>InvoiceConfiguration</c>）。</summary>
    private const int InternalNotesMaxLength = 1000;

    /// <summary>
    /// 往内部备注后面追加一条：拼完放不下列宽时从最早的内容截掉，新追加的这一条完整留下。
    /// </summary>
    /// <remarks>
    /// 开票时的内部备注本身就能写满列宽：此前直接拼接，在会校验列宽的数据库上（SQL Server / PostgreSQL / MySQL 严格模式）
    /// 那就是一次写库失败，操作员的「标记已付」或「作废」拿到 500、发票原样未动。
    /// 长度在这里按字符精确计算（含末尾空格），不交给数据库的长度函数。
    /// ★ 截断点不落在代理对中间（emoji 等补充平面字符占两个 UTF-16 单元）：留下的半个字符不是合法文本，
    /// PostgreSQL 的驱动按严格 UTF-8 编码会直接抛异常（标记已付 / 作废答 500），别的库则存成乱码。
    /// 此前在 SQL 里截取，数据库按码点算，不会切开一个字符；挪到内存里之后要自己守住这一点。
    /// </remarks>
    private static string AppendInternalNote(string? current, string note)
    {
        var entry = note;
        if (entry.Length > InternalNotesMaxLength)
        {
            var end = InternalNotesMaxLength;
            if (char.IsHighSurrogate(entry[end - 1]))
                end--;
            entry = entry[..end];
        }

        // 留给旧备注的长度：分隔符占一位
        var room = InternalNotesMaxLength - entry.Length - 1;
        if (room <= 0 || string.IsNullOrEmpty(current))
            return entry;

        if (current.Length <= room)
            return current + "\n" + entry;

        var start = current.Length - room;
        if (char.IsLowSurrogate(current[start]))
            start++;
        return current[start..] + "\n" + entry;
    }

    /// <summary>
    /// 产物指针三列置空：发票的状态或金额变了，已生成的产物上印着的是旧内容，下次取用时按新状态重新生成。
    /// 与改状态写在同一条语句里，不留「状态已变、产物还是旧的」的窗口。
    /// </summary>
    private static void ClearDocumentPointers(UpdateSettersBuilder<Invoice> setters)
        => setters
            .SetProperty(i => i.PdfFileId, (Guid?)null)
            .SetProperty(i => i.PdfFilePath, (string?)null)
            .SetProperty(i => i.PdfFileUrl, (string?)null);

    /// <inheritdoc />
    /// <remarks>
    /// 与列表同一个投影（含明细）。此前这里读出实体再映射，而仓储查询不带明细导航，
    /// 详情端点于是恒答 <c>lineItems: []</c>，同一张发票在列表里却带着完整明细。
    /// </remarks>
    public async Task<Result<PaymentInvoiceDto>> GetAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: false, cancellationToken);
        if (!owner.Succeeded)
            return Fail<PaymentInvoiceDto>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var invoice = await OwnedBy(_invoiceRepository.AsNoTracking(), owner.Data)
            .Where(i => i.Id == id)
            .ProjectTo<Invoice, PaymentInvoiceDto>()
            .FirstOrDefaultAsync(cancellationToken);
        if (invoice == null)
            return Fail<PaymentInvoiceDto>(ErrorCodes.InvoiceNotFound, 404);

        return Ok(invoice);
    }

    public async Task<Result<IPagedList<PaymentInvoiceDto>>> GetListAsync(PaymentInvoiceQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: false, cancellationToken);
        if (!owner.Succeeded)
            return Fail<IPagedList<PaymentInvoiceDto>>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var queryable = OwnedBy(_invoiceRepository.AsNoTracking().Filter(query), owner.Data);

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
    /// 按计费账户过滤（默认的 User 范围下就是 <c>UserId</c>）而不是 <c>CreatorId</c>：自动开票发生在支付完成事件的处理器里
    /// （webhook 匿名，或后台扣款的裸 scope），那里根本没有当前用户，于是 <c>CreatorId</c> 恒为 null ——
    /// 按它过滤的结果是这个列表**永远是空的**，而接口 200、日志干净、发票一张不少地躺在库里。
    /// 同服务其余四个用户面方法一直按 <c>UserId</c> 过滤，只有这一处不是。
    /// </remarks>
    public async Task<Result<IPagedList<PaymentInvoiceDto>>> GetUserInvoicesAsync(Guid userId, PagedQueryDto? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new OwnerListQueryDto();

        var owner = await _accountResolver.ResolveScopeAsync(userId, manage: false, cancellationToken);
        if (!owner.Succeeded)
            return Fail<IPagedList<PaymentInvoiceDto>>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var invoices = await OwnedBy(_invoiceRepository.AsNoTracking(), owner.Data)
            .OrderByDescending(i => i.InvoiceDate)
            .ProjectTo<Invoice, PaymentInvoiceDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(invoices);
    }

    /// <summary>只留记在 <paramref name="owner"/> 账户上的发票；为 null 时不限定（管理端 / 系统调用）。</summary>
    private static IQueryable<Invoice> OwnedBy(IQueryable<Invoice> query, BillingAccountRef? owner)
        => owner == null
            ? query
            : query.Where(i => i.BillingAccountType == owner.Type && i.BillingAccountId == owner.Id);
}
