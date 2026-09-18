namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>收款人（供应商）票面信息</summary>
public sealed record CheckPayeeInfo(string Name, string? Address);

/// <summary>一批待开票付款单的解析结果（银行档案 + 稳定排序的付款单 + 收款人档案）</summary>
public sealed record CheckBatchContext(BankAccount Bank, List<PaymentEntry> Payments, Dictionary<Guid, CheckPayeeInfo> Payees);

/// <summary>
/// 把「一组付款单」组装成「可以送去渲染的一批支票」。
/// </summary>
/// <remarks>
/// 从 <see cref="CheckService"/> 拆出：支票的**生命周期**（分配号、开票、作废、毁票、重打）
/// 与「票面上印什么」是两件事，后者是纯粹的解析 + 构造，不碰登记簿也不动账。<br/>
/// <c>print</c> 与 <c>preview</c> 共用本类的同一口径，这正是预览能保证「所见即将打」的原因 ——
/// 两条路径各自解析一遍批次，迟早会漂移。<br/>
/// public 因为经 DI 注入 public 服务的构造函数（沿 <c>CheckIssuerResolver</c>/<c>LedgerPostingEngine</c>
/// 先例；MS.DI 只解析 public 构造函数，参数与返回类型必须至少同等可访问）。
/// </remarks>
public class CheckBatchComposer
{
    private static readonly string[] AddressLineSeparators = { "\r\n", "\r", "\n" };

    private readonly IReadOnlyRepository<PaymentEntry, Guid> _paymentRepository;
    private readonly IReadOnlyRepository<BankAccount, Guid> _bankAccountRepository;
    private readonly IReadOnlyRepository<BankCheck, Guid> _checkRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly CheckIssuerResolver _issuerResolver;
    private readonly IFinanceDataProtector _protector;
    private readonly FinanceOptions _options;
    private readonly FinanceCheckOptions _checkOptions;
    private readonly ICheckStubLineProvider? _stubLineProvider;
    private readonly ILogger<CheckBatchComposer>? _logger;

    public CheckBatchComposer(
        IReadOnlyRepository<PaymentEntry, Guid> paymentRepository,
        IReadOnlyRepository<BankAccount, Guid> bankAccountRepository,
        IReadOnlyRepository<BankCheck, Guid> checkRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        CheckIssuerResolver issuerResolver,
        IFinanceDataProtector protector,
        IOptionsSnapshot<FinanceOptions> options,
        IOptionsSnapshot<FinanceCheckOptions> checkOptions,
        ICheckStubLineProvider? stubLineProvider = null,
        ILogger<CheckBatchComposer>? logger = null)
    {
        _paymentRepository = Check.NotNull(paymentRepository);
        _bankAccountRepository = Check.NotNull(bankAccountRepository);
        _checkRepository = Check.NotNull(checkRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _issuerResolver = Check.NotNull(issuerResolver);
        _protector = Check.NotNull(protector);
        _options = Check.NotNull(options).Value;
        _checkOptions = Check.NotNull(checkOptions).Value;
        // 可选注入：消费应用不实现即没有存根附加行，存根按出厂样子排（与本机制引入前逐字相同）。
        _stubLineProvider = stubLineProvider;
        _logger = logger;
    }

    /// <summary>
    /// 空白票纸打印前置校验：Blank 票纸须现打 MICR 行，故须有 scheme 有效的路由/transit + 可解密账号，
    /// 否则打出结构在但空路由（Transit 括号内空）/ 整条 MICR 被丢弃的不可流通票据（银行拒付/误路由）。
    /// 预印票纸（PrePrinted）MICR 已印在票纸上，跳过。
    /// </summary>
    /// <param name="bank">出款银行账户档案（路由号与账号密文的来源）。</param>
    /// <param name="stockType">
    /// 本次生效的票纸类型；null = 用档案当前值。重新渲染一张已开支票时传<b>快照</b>里的票纸 ——
    /// 否则档案改成预印之后，一张当初印在白纸上的票会跳过这道守卫，最终打出没有磁码的纸。
    /// </param>
    public Result ValidateBlankStockPrintable(BankAccount bank, CheckStockType? stockType = null)
    {
        Check.NotNull(bank);

        if ((stockType ?? bank.CheckStockType) != CheckStockType.Blank)
            return Result.Success();

        var hasRouting = bank.Scheme switch
        {
            BankNumberScheme.UsAba => !string.IsNullOrWhiteSpace(bank.RoutingNumber),
            BankNumberScheme.CaEft => !string.IsNullOrWhiteSpace(bank.InstitutionNumber) && !string.IsNullOrWhiteSpace(bank.TransitNumber),
            _ => false
        };
        var routingValid = BankNumberHelper.ValidateRouting(bank.Scheme, bank.RoutingNumber, bank.InstitutionNumber, bank.TransitNumber);
        if (!hasRouting || !routingValid.Succeeded)
            return Result.Failure("Blank check stock requires a scheme-valid routing/transit number on the bank account before printing the MICR line.", 400);

        if (string.IsNullOrWhiteSpace(bank.AccountNumberEncrypted) || !_protector.IsConfigured)
            return Result.Failure("Blank check stock requires a stored, decryptable account number on the bank account before printing.", 400);

        // ★ 必须真解一次，而不是只确认密文在、加密已配置。
        //
        // 密钥轮换后（或把库恢复到另一把密钥的环境后）上面两个条件**都仍然成立**，而解密会失败。
        // BuildRenderRequest 把那次失败吞成一条 LogWarning 并让 AccountNumberPlain 留 null，
        // 渲染器于是丢掉整条 MICR 行 —— 渲染本身**成功**，所以 CheckService 的工作单元照常提交：
        // 支票号已分配、BankCheck 已 Issued、付款单参考号已回写，打出来的却正是本方法注释开头
        // 说要防的那种不可流通票据。守卫检查「有没有」而不是「解不解得开」，等于放行了它唯一要拦的场景。
        try
        {
            _protector.Unprotect(bank.AccountNumberEncrypted!, FinanceProtectionAad.ForBankAccount(bank.AccountId));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "Bank account {BankAccountId} has a stored account number that cannot be decrypted; blocking blank-stock printing before any check number is allocated.",
                bank.Id);
            return Result.Failure(
                "The stored account number on this bank account cannot be decrypted (the encryption key may have changed). "
                + "Re-enter the account number before printing on blank check stock.", 400);
        }

        return Result.Success();
    }

    /// <summary>
    /// 一笔付款单能不能挂上一张支票：Posted + Outbound + <c>PaymentMethod == Check</c> + 有出款科目。
    /// </summary>
    /// <remarks>
    /// ★ 付款方式必须在写路径上自己判，不能靠「队列只列 Check 付款」：print / preview / registerManual
    /// 的入参都是请求体里的付款单 id，从不经过队列。少了这条，一笔 BankTransfer 付款（可能已装进
    /// EFT 批次并交给银行）会被开成或登记成一张可流通支票 —— 同一笔钱付两次，而两侧登记各自看起来
    /// 完全正常；且随后的参考号回写会把付款单的电汇确认号覆盖成支票号。
    /// 与 <c>EftService.CreateBatchAsync</c> 拒非 BankTransfer 付款对称。<br/>
    /// 三个开票入口只允许有这一份判据：各写一份的话，下一个新入口照样会漏（2026-09-12 手工登记正是这样漏的）。
    /// </remarks>
    public static Result ValidateCheckEligibility(PaymentEntry payment)
    {
        Check.NotNull(payment);
        var label = payment.Number ?? payment.Id.ToString();
        if (payment.Status != FinanceDocumentStatus.Posted || payment.Direction != PaymentDirection.Outbound)
            return Result.Failure($"Payment '{label}' is not a posted outbound payment.", 400);
        if (string.IsNullOrWhiteSpace(payment.PaymentMethod) || !string.Equals(payment.PaymentMethod, PaymentMethods.Check, StringComparison.OrdinalIgnoreCase))
            return Result.Failure($"Payment '{label}' is not a check payment.", 400);
        if (payment.DepositToAccountId == null)
            return Result.Failure($"Payment '{label}' has no funding account.", 400);
        return Result.Success();
    }

    /// <summary>
    /// 手工登记一张挂在付款单上的票之前的解析：付款单存在（404）/ 开票资格（400）/
    /// 出款科目就是所选银行档案挂的科目（400）/ 尚无 Issued 票（409）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ResolveBatchAsync"/> 是同一组判据的单笔形态；登记簿一侧的写入
    /// （占号 + 参考号回写）由 <c>CheckService</c> 在同一个 UoW 内完成，这里只回答「能不能」。
    /// 「已有 Issued 票」答 409 而不是 400：与打印路径同一个码，换票的正路是 Reprint。
    /// </remarks>
    public async Task<Result> ResolveManualCheckPaymentAsync(Guid paymentEntryId, BankAccount bank, CancellationToken cancellationToken = default)
    {
        Check.NotNull(bank);
        var payment = await _paymentRepository.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentEntryId, cancellationToken);
        if (payment == null)
            return Result.Failure("Payment not found.", 404);

        var eligible = ValidateCheckEligibility(payment);
        if (!eligible.Succeeded)
            return eligible;

        if (payment.DepositToAccountId != bank.AccountId)
            return Result.Failure($"Payment '{payment.Number ?? payment.Id.ToString()}' is not funded from the selected bank account.", 400);

        var alreadyIssued = await _checkRepository.AsNoTracking().AnyAsync(
            c => c.Status == CheckStatus.Issued && c.PaymentEntryId == paymentEntryId, cancellationToken);
        if (alreadyIssued)
            return Result.Failure("This payment already has an issued check. Use reprint instead.", 409);

        return Result.Success();
    }

    /// <summary>
    /// 解析一批待开票付款单：存在性 / 队列资格（Posted Outbound Check）/ 同一银行账户 / 空白票纸可打 / 未重复开票，
    /// 并带出银行档案、稳定排序后的付款单与收款人档案。
    /// </summary>
    public async Task<Result<CheckBatchContext>> ResolveBatchAsync(List<Guid>? paymentEntryIds, string operation, CancellationToken cancellationToken = default)
    {
        if (paymentEntryIds == null || paymentEntryIds.Count == 0)
            return Result<CheckBatchContext>.Failure($"Select at least one payment to {operation}.", 400);

        var ids = paymentEntryIds.Distinct().ToList();
        var payments = await _paymentRepository.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);
        if (payments.Count != ids.Count)
            return Result<CheckBatchContext>.Failure("One or more payments were not found.", 404);

        // 校验队列资格：均为 Posted Outbound Check（判据本身在 ValidateCheckEligibility，与手工登记共用）。
        foreach (var p in payments)
        {
            var eligible = ValidateCheckEligibility(p);
            if (!eligible.Succeeded)
                return Result<CheckBatchContext>.Failure(eligible.Message!, eligible.Code ?? 400);
        }

        // 均须解析到同一银行账户档案（单份文档共享版式/偏移/MICR）
        var ledgerIds = payments.Select(p => p.DepositToAccountId!.Value).Distinct().ToList();
        var bankAccounts = await _bankAccountRepository.AsNoTracking()
            .Where(b => ledgerIds.Contains(b.AccountId))
            .ToListAsync(cancellationToken);
        var byLedger = bankAccounts.ToDictionary(b => b.AccountId);
        if (payments.Any(p => !byLedger.ContainsKey(p.DepositToAccountId!.Value)))
            return Result<CheckBatchContext>.Failure($"A payment's funding account has no bank account profile. Configure one before {operation}ing.", 400);
        var distinctBanks = payments.Select(p => byLedger[p.DepositToAccountId!.Value].Id).Distinct().ToList();
        if (distinctBanks.Count != 1)
            return Result<CheckBatchContext>.Failure("All selected payments must draw on the same bank account.", 400);

        var bank = byLedger[payments[0].DepositToAccountId!.Value];

        var blankStockCheck = ValidateBlankStockPrintable(bank);
        if (!blankStockCheck.Succeeded)
            return Result<CheckBatchContext>.Failure(blankStockCheck.Message!, blankStockCheck.Code ?? 400);

        // 已开票的付款不能重复打印（重打走 ReprintAsync）
        var alreadyIssued = await _checkRepository.AsNoTracking().AnyAsync(
            c => c.Status == CheckStatus.Issued && c.PaymentEntryId != null && ids.Contains(c.PaymentEntryId.Value), cancellationToken);
        if (alreadyIssued)
            return Result<CheckBatchContext>.Failure("One or more payments already have an issued check. Use reprint instead.", 409);

        var payees = await LoadPayeesAsync(payments.Select(p => p.PartyId), cancellationToken);
        var ordered = payments.OrderBy(p => p.Number).ThenBy(p => p.Id).ToList();

        return Result<CheckBatchContext>.Success(new CheckBatchContext(bank, ordered, payees));
    }

    /// <summary>付款单 + 收款人档案 → 单张支票的渲染数据（打印与预览共用，保证票面一致）。</summary>
    /// <remarks>
    /// <c>stubLines</c> 是消费应用附加到本张存根的行（<see cref="LoadStubLinesAsync"/> 取得，
    /// 已归一化）；省略 = 没有附加行，存根按出厂样子排。
    /// </remarks>
    public static CheckRenderItem BuildRenderItem(
        long checkNumber, PaymentEntry payment, CheckPayeeInfo? payee, DateTime issueDate,
        IReadOnlyList<CheckStubLine>? stubLines = null)
        => new()
        {
            CheckNumber = checkNumber,
            PayeeName = payee?.Name,
            PayeeAddressLines = SplitAddressLines(payee?.Address),
            Amount = payment.Amount,
            Currency = payment.Currency,
            AmountInWords = CheckAmountInWords.Convert(payment.Amount, payment.Currency),
            IssueDate = issueDate,
            Memo = payment.Memo,
            PaymentNumber = payment.Number,
            Reference = payment.Reference,
            StubLines = stubLines == null ? [] : [.. stubLines]
        };

    /// <summary>按渲染器自报的内容类型/扩展名落地文件（HTML 或 PDF）。</summary>
    public static CheckFileDto BuildFile(ICheckDocumentRenderer renderer, string baseName, byte[] content)
        => new()
        {
            FileName = $"{baseName}{renderer.FileExtension}",
            ContentType = renderer.ContentType,
            Content = content
        };

    public static List<string> SplitAddressLines(string? address)
        => string.IsNullOrWhiteSpace(address)
            ? new List<string>()
            : address.Split(AddressLineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>银行档案 + 生效的打印设置 + 各票数据 → 渲染请求（版式/偏移/MICR/出票方身份）。</summary>
    /// <param name="bank">出款银行账户档案（银行标识、路由号、账号密文的来源）。</param>
    /// <param name="items">本次要画的每一张票。</param>
    /// <param name="settings">
    /// 本次生效的模板 / 版式 / 票纸 / 偏移；null = 档案当前值（打印、预览、重打、校准页的口径）。
    /// 重新渲染一张已开支票时传开票时刻的快照，见 <see cref="CheckPrintSettings.ForIssuedCheck"/>。
    /// </param>
    /// <param name="useStoredAccountNumber">
    /// 是否解密档案里的账号来拼 MICR。<b>样张必须传 false</b> —— 它会被下载被打印，
    /// 而磁码行在白纸票纸下是真的印出来的，水印挡人眼挡不住读票机。
    /// </param>
    public CheckRenderRequest BuildRenderRequest(
        BankAccount bank, List<CheckRenderItem> items, CheckPrintSettings? settings = null,
        bool useStoredAccountNumber = true)
    {
        Check.NotNull(bank);

        var effective = settings ?? CheckPrintSettings.FromBank(bank);

        var request = new CheckRenderRequest
        {
            Layout = effective.Layout,
            StockType = effective.StockType,
            OffsetXMm = effective.OffsetXMm,
            OffsetYMm = effective.OffsetYMm,
            Scheme = bank.Scheme,
            BankName = bank.BankName,
            AccountName = bank.Name,
            RoutingNumber = bank.RoutingNumber,
            InstitutionNumber = bank.InstitutionNumber,
            TransitNumber = bank.TransitNumber,
            MicrFontPath = _options.CheckMicrFontPath,
            CheckNumberDigits = _checkOptions.CheckNumberDigits,
            TemplateName = effective.TemplateName,
            Issuer = _issuerResolver.Resolve(),
            Checks = items
        };

        // 仅白纸打印需要账号明文拼装 MICR
        if (useStoredAccountNumber && effective.StockType == CheckStockType.Blank
            && !string.IsNullOrWhiteSpace(bank.AccountNumberEncrypted) && _protector.IsConfigured)
        {
            try
            {
                // AAD 绑定到该银行档案的资金科目（v1 存量密文自动忽略 AAD）。
                request.AccountNumberPlain = _protector.Unprotect(bank.AccountNumberEncrypted!, FinanceProtectionAad.ForBankAccount(bank.AccountId));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to decrypt bank account number for MICR rendering; MICR line will be omitted.");
            }
        }

        return request;
    }

    /// <summary>
    /// 版式样张的渲染请求：占位数据 + 中性（或所选档案的）银行标识，<b>零副作用</b>。
    /// </summary>
    /// <remarks>
    /// ★ 走的是与打印<b>同一个</b> <see cref="CheckRenderRequest"/> → 同一个渲染器 → 同一个模型工厂。
    /// 样张若另画一套，它就不再是「所见即所印」，这个功能反而有害。
    /// <para>
    /// ★ 绑定档案时用它的真实银行名 / 路由号 / 档案名（样张才有参考价值），但
    /// <b>永不取账号</b>：路由号本来就印在每一张寄出去的支票上，账号则是磁码行的实质内容。
    /// 白纸票纸下改用全 0 的占位账号，磁码带画得出来，而那不是一份可流通的编码。
    /// </para>
    /// </remarks>
    /// <param name="bank">要借用标识的银行档案；null = 用中性占位（未绑定任何档案）。</param>
    /// <param name="settings">样张的模板 / 版式 / 票纸 / 偏移。</param>
    /// <param name="items">占位支票（见 <see cref="CheckSpecimenSample.BuildItems"/>）。</param>
    /// <param name="scheme">未绑定档案时用哪种路由号方案画银行区（绑定时以档案为准）。</param>
    public CheckRenderRequest BuildSpecimenRequest(
        BankAccount? bank, CheckPrintSettings settings, List<CheckRenderItem> items, BankNumberScheme scheme)
    {
        Check.NotNull(settings);

        var request = bank != null
            ? BuildRenderRequest(bank, items, settings, useStoredAccountNumber: false)
            : new CheckRenderRequest
            {
                Layout = settings.Layout,
                StockType = settings.StockType,
                OffsetXMm = settings.OffsetXMm,
                OffsetYMm = settings.OffsetYMm,
                Scheme = scheme,
                BankName = CheckSpecimenSample.BankName,
                AccountName = CheckSpecimenSample.AccountName,
                RoutingNumber = CheckSpecimenSample.RoutingNumber,
                InstitutionNumber = CheckSpecimenSample.InstitutionNumber,
                TransitNumber = CheckSpecimenSample.TransitNumber,
                MicrFontPath = _options.CheckMicrFontPath,
                CheckNumberDigits = _checkOptions.CheckNumberDigits,
                TemplateName = settings.TemplateName,
                // 出票方抬头/签名来自 System General + FinanceOptions，与银行档案无关：
                // 即使不绑定档案，样张上的抬头也已经是这家公司自己的。
                Issuer = _issuerResolver.Resolve(),
                Checks = items
            };

        request.IsPreview = true;
        request.PreviewLabel = CheckSpecimenSample.Label;
        // 样张比预览多要一件事：在屏幕上把「票纸自带的」与「打印机现打的」分开。
        // 预印元素的 noprint 只在 @media print 里生效，屏幕上照常显示 ——
        // 不给这个标志，两种票纸的样张在缩略图尺寸下几乎一模一样。
        request.IsSpecimen = true;

        // 白纸票纸：磁码行由打印机现打，样张要让人看见那条带子占了哪一段，
        // 故给占位账号——绑定档案时也是这个，绝不是它的真账号。
        if (settings.StockType == CheckStockType.Blank)
            request.AccountNumberPlain = CheckSpecimenSample.AccountNumber;

        return request;
    }

    /// <summary>
    /// 向消费应用批量索取存根附加行（未注册 <see cref="ICheckStubLineProvider"/> 时返回空）。
    /// </summary>
    /// <returns>付款单 id → 已归一化的附加行；查不到的 id 不出现在字典里。</returns>
    /// <remarks>
    /// ★ <b>提供者失败不得让打印失败</b>：调用点上支票号已经分配、登记簿行已经写下，
    /// 而附加行是装饰性的（付款正确性完全不依赖它）。让一个消费应用的查询错误
    /// 把整批付款回滚掉，代价方向是错的 —— 记一条 Warning 并按「没有附加行」继续。
    /// 契约文档同时要求实现自己不要拿异常表达「没有数据」，这里是兜底不是许可。
    /// </remarks>
    public async Task<Dictionary<Guid, IReadOnlyList<CheckStubLine>>> LoadStubLinesAsync(
        IEnumerable<Guid> paymentEntryIds, CancellationToken cancellationToken = default)
    {
        var empty = new Dictionary<Guid, IReadOnlyList<CheckStubLine>>();
        if (_stubLineProvider == null)
            return empty;

        var ids = paymentEntryIds.Distinct().ToList();
        if (ids.Count == 0)
            return empty;

        IReadOnlyDictionary<Guid, IReadOnlyList<CheckStubLine>> supplied;
        try
        {
            supplied = await _stubLineProvider.GetStubLinesAsync(ids, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "The registered ICheckStubLineProvider failed for {PaymentCount} payment(s); printing continues without stub lines.",
                ids.Count);
            return empty;
        }

        if (supplied == null)
            return empty;

        var normalized = new Dictionary<Guid, IReadOnlyList<CheckStubLine>>();
        foreach (var (paymentEntryId, lines) in supplied)
        {
            var clean = CheckStubLineLimits.Normalize(lines);
            if (clean.Count > 0)
                normalized[paymentEntryId] = clean;
        }
        return normalized;
    }

    public async Task<Dictionary<Guid, CheckPayeeInfo>> LoadPayeesAsync(IEnumerable<Guid> partyIds, CancellationToken cancellationToken = default)
    {
        var ids = partyIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, CheckPayeeInfo>();

        return await _vendorRepository.AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Name, v.Address })
            .ToDictionaryAsync(v => v.Id, v => new CheckPayeeInfo(v.Name, v.Address), cancellationToken);
    }
}
