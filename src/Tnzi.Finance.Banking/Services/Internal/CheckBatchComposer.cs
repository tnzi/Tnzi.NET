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
    private readonly ILogger<CheckBatchComposer>? _logger;

    public CheckBatchComposer(
        IReadOnlyRepository<PaymentEntry, Guid> paymentRepository,
        IReadOnlyRepository<BankAccount, Guid> bankAccountRepository,
        IReadOnlyRepository<BankCheck, Guid> checkRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        CheckIssuerResolver issuerResolver,
        IFinanceDataProtector protector,
        IOptionsSnapshot<FinanceOptions> options,
        ILogger<CheckBatchComposer>? logger = null)
    {
        _paymentRepository = Check.NotNull(paymentRepository);
        _bankAccountRepository = Check.NotNull(bankAccountRepository);
        _checkRepository = Check.NotNull(checkRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _issuerResolver = Check.NotNull(issuerResolver);
        _protector = Check.NotNull(protector);
        _options = Check.NotNull(options).Value;
        _logger = logger;
    }

    /// <summary>
    /// 空白票纸打印前置校验：Blank 票纸须现打 MICR 行，故须有 scheme 有效的路由/transit + 可解密账号，
    /// 否则打出结构在但空路由（Transit 括号内空）/ 整条 MICR 被丢弃的不可流通票据（银行拒付/误路由）。
    /// 预印票纸（PrePrinted）MICR 已印在票纸上，跳过。
    /// </summary>
    public Result ValidateBlankStockPrintable(BankAccount bank)
    {
        Check.NotNull(bank);

        if (bank.CheckStockType != CheckStockType.Blank)
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

        return Result.Success();
    }

    /// <summary>
    /// 解析一批待开票付款单：存在性 / 队列资格 / 同一银行账户 / 空白票纸可打 / 未重复开票，
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

        // 校验队列资格：均为 Posted Outbound Check
        foreach (var p in payments)
        {
            if (p.Status != FinanceDocumentStatus.Posted || p.Direction != PaymentDirection.Outbound)
                return Result<CheckBatchContext>.Failure($"Payment '{p.Number ?? p.Id.ToString()}' is not a posted outbound payment.", 400);
            if (p.DepositToAccountId == null)
                return Result<CheckBatchContext>.Failure($"Payment '{p.Number ?? p.Id.ToString()}' has no funding account.", 400);
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
    public static CheckRenderItem BuildRenderItem(long checkNumber, PaymentEntry payment, CheckPayeeInfo? payee, DateTime issueDate)
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
            Reference = payment.Reference
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

    /// <summary>银行档案 + 各票数据 → 渲染请求（版式/偏移/MICR/出票方身份）。</summary>
    public CheckRenderRequest BuildRenderRequest(BankAccount bank, List<CheckRenderItem> items)
    {
        Check.NotNull(bank);

        var request = new CheckRenderRequest
        {
            Layout = bank.CheckLayout,
            StockType = bank.CheckStockType,
            OffsetXMm = bank.OffsetXMm,
            OffsetYMm = bank.OffsetYMm,
            Scheme = bank.Scheme,
            BankName = bank.BankName,
            AccountName = bank.Name,
            RoutingNumber = bank.RoutingNumber,
            InstitutionNumber = bank.InstitutionNumber,
            TransitNumber = bank.TransitNumber,
            MicrFontPath = _options.CheckMicrFontPath,
            TemplateName = bank.CheckTemplateName,
            Issuer = _issuerResolver.Resolve(),
            Checks = items
        };

        // 仅白纸打印需要账号明文拼装 MICR
        if (bank.CheckStockType == CheckStockType.Blank && !string.IsNullOrWhiteSpace(bank.AccountNumberEncrypted) && _protector.IsConfigured)
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
