namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 支票打印与登记服务
/// </summary>
public class CheckService : ApplicationService, ICheckService
{
    private readonly IRepository<BankCheck, Guid> _checkRepository;
    private readonly IRepository<BankAccount, Guid> _bankAccountRepository;
    /// <summary>
    /// 付款单仓储：读用于队列/批次解析（一律 <c>AsNoTracking</c>），写仅用于把支票号回写
    /// <see cref="PaymentEntry.Reference"/>（该字段的框架语义即"外部参考号(支票号/交易号)"）。
    /// </summary>
    private readonly IRepository<PaymentEntry, Guid> _paymentRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly CheckNumberAllocator _allocator;
    private readonly CheckIssuerResolver _issuerResolver;
    private readonly CheckBatchComposer _composer;
    private readonly ICheckDocumentRenderer? _renderer;
    private readonly IFinanceDataProtector _protector;
    private readonly FinanceOptions _options;

    /// <summary>未加载渲染子模块时，print/preview/reprint/calibration 的 501 引导。</summary>
    private const string RendererMissingMessage =
        "Check rendering requires an ICheckDocumentRenderer implementation. Load the Tnzi.Finance.Documents module (or register your own ICheckDocumentRenderer) to enable check printing.";

    public CheckService(
        IServiceProvider serviceProvider,
        IRepository<BankCheck, Guid> checkRepository,
        IRepository<BankAccount, Guid> bankAccountRepository,
        IRepository<PaymentEntry, Guid> paymentRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        CheckNumberAllocator allocator,
        IFinanceDataProtector protector,
        IOptionsSnapshot<FinanceOptions> options,
        CheckIssuerResolver issuerResolver,
        CheckBatchComposer composer,
        ICheckDocumentRenderer? renderer = null)
        : base(serviceProvider)
    {
        _checkRepository = Check.NotNull(checkRepository);
        _bankAccountRepository = Check.NotNull(bankAccountRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _allocator = Check.NotNull(allocator);
        _protector = Check.NotNull(protector);
        _options = Check.NotNull(options).Value;
        _issuerResolver = Check.NotNull(issuerResolver);
        _composer = Check.NotNull(composer);
        // 可选注入：未加载 Tnzi.Finance.Documents 时为 null，渲染端点返回 501（同 IReceiptExtractor 兜底）。
        _renderer = renderer;
    }

    public async Task<Result<List<CheckQueueItemDto>>> GetQueueAsync(Guid? bankAccountId = null, CancellationToken cancellationToken = default)
    {
        // 银行档案（可选过滤）：建立 付款科目 → 档案 的映射
        var bankAccounts = await _bankAccountRepository.AsNoTracking()
            .Where(b => bankAccountId == null || b.Id == bankAccountId.Value)
            .Select(b => new { b.Id, b.AccountId, b.Name })
            .ToListAsync(cancellationToken);
        if (bankAccounts.Count == 0)
            return Ok(new List<CheckQueueItemDto>());

        var byLedger = bankAccounts.ToDictionary(b => b.AccountId, b => b);
        var ledgerIds = bankAccounts.Select(b => b.AccountId).ToList();

        var payments = await _paymentRepository.AsNoTracking()
            .Where(p => p.Status == FinanceDocumentStatus.Posted
                && p.Direction == PaymentDirection.Outbound
                && p.PaymentMethod != null && p.PaymentMethod.ToLower() == "check"
                && p.DepositToAccountId != null && ledgerIds.Contains(p.DepositToAccountId.Value))
            .OrderBy(p => p.Number)
            .ToListAsync(cancellationToken);
        if (payments.Count == 0)
            return Ok(new List<CheckQueueItemDto>());

        var paymentIds = payments.Select(p => p.Id).ToList();
        var issuedFor = (await _checkRepository.AsNoTracking()
            .Where(c => c.Status == CheckStatus.Issued && c.PaymentEntryId != null && paymentIds.Contains(c.PaymentEntryId.Value))
            .Select(c => c.PaymentEntryId!.Value)
            .ToListAsync(cancellationToken)).ToHashSet();

        var pending = payments.Where(p => !issuedFor.Contains(p.Id)).ToList();
        var payees = await _composer.LoadPayeesAsync(pending.Select(p => p.PartyId), cancellationToken);

        var items = pending.Select(p =>
        {
            var bank = byLedger[p.DepositToAccountId!.Value];
            return new CheckQueueItemDto
            {
                PaymentEntryId = p.Id,
                PaymentNumber = p.Number,
                BankAccountId = bank.Id,
                BankAccountName = bank.Name,
                PayeeName = payees.GetValueOrDefault(p.PartyId)?.Name,
                DocDate = p.DocDate,
                Currency = p.Currency,
                Amount = p.Amount,
                Memo = p.Memo,
                Reference = p.Reference
            };
        }).ToList();

        return Ok(items);
    }

    public async Task<Result<IPagedList<BankCheckDto>>> GetPagedAsync(CheckQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _checkRepository.AsNoTracking();
        if (query.BankAccountId.HasValue)
            queryable = queryable.Where(c => c.BankAccountId == query.BankAccountId.Value);
        if (query.Status.HasValue)
            queryable = queryable.Where(c => c.Status == query.Status.Value);
        // 不叠加状态过滤时返回该付款单名下全部票（Issued + 被重打作废的 Void），
        // 既有的 CheckNumber 倒序排序在单账户场景下天然就是重打链的时间序。
        if (query.PaymentEntryId.HasValue)
            queryable = queryable.Where(c => c.PaymentEntryId == query.PaymentEntryId.Value);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(c =>
                (c.PayeeName != null && c.PayeeName.ToLower().Contains(keyword)) ||
                (c.VoidReason != null && c.VoidReason.ToLower().Contains(keyword)));
        }

        var pagedList = await queryable
            .OrderByDescending(c => c.BankAccountId)
            .ThenByDescending(c => c.CheckNumber)
            .ProjectTo<BankCheck, BankCheckDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        await FillNamesAsync(pagedList.Items, cancellationToken);
        return Ok(pagedList);
    }

    public async Task<Result<string>> ExportPositivePayAsync(Guid bankAccountId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (to.Date < from.Date)
            return Fail<string>("The 'to' date must not be earlier than the 'from' date.", 400);

        var fromDate = from.ToUtcDate();
        var toExclusive = to.ToUtcDate().AddDays(1);

        var checks = await _checkRepository.AsNoTracking()
            .Where(c => c.BankAccountId == bankAccountId && c.IssueDate >= fromDate && c.IssueDate < toExclusive)
            .OrderBy(c => c.CheckNumber)
            .ToListAsync(cancellationToken);

        // 通用 positive-pay CSV：银行按此比对提示付款的支票；作废/毁票行标 Void（银行止付）。
        var csv = new CsvBuilder("yyyy-MM-dd");
        csv.AppendRow("CheckNumber", "Amount", "IssueDate", "Payee", "Status");
        foreach (var c in checks)
        {
            var indicator = c.Status == CheckStatus.Issued ? "Issued" : "Void";
            csv.AppendRow(c.CheckNumber, c.Amount ?? 0m, c.IssueDate, c.PayeeName, indicator);
        }

        return Ok<string>(csv.ToString());
    }

    public async Task<Result<CheckFileDto>> PrintAsync(PrintChecksDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (_renderer == null)
            return Fail<CheckFileDto>(RendererMissingMessage, 501);
        ICheckDocumentRenderer renderer = _renderer; // 上面已排除 null，捕获非空局部供 UoW 闭包使用

        var batch = await _composer.ResolveBatchAsync(input.PaymentEntryIds, "print", cancellationToken);
        if (!batch.Succeeded)
            return Fail<CheckFileDto>(batch.Message!, batch.Code ?? 400);
        var bank = batch.Data!.Bank;

        var created = new List<BankCheck>();
        CheckFileDto? file = null;

        try
        {
            var result = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var items = new List<CheckRenderItem>();
                foreach (var p in batch.Data.Payments)
                {
                    var checkNumber = await _allocator.AllocateAsync(bank.Id, ct);
                    var payee = batch.Data.Payees.GetValueOrDefault(p.PartyId);
                    var check = new BankCheck
                    {
                        BankAccountId = bank.Id,
                        CheckNumber = checkNumber,
                        Status = CheckStatus.Issued,
                        PaymentEntryId = p.Id,
                        PayeeName = payee?.Name,
                        Amount = p.Amount,
                        Currency = p.Currency,
                        IssueDate = input.IssueDate?.ToUtcDate() ?? p.DocDate,
                        PrintedTime = DateTime.UtcNow,
                        IsManual = false,
                        TenantId = p.TenantId
                    };
                    await _checkRepository.InsertAsync(check, ct);
                    created.Add(check);

                    // 队列口径已限定 PaymentMethod == "check"，故无条件回写支票号到付款单参考号
                    await StampPaymentReferenceAsync(p.Id, checkNumber, ct);

                    items.Add(CheckBatchComposer.BuildRenderItem(checkNumber, p, payee, check.IssueDate));
                }

                var renderRequest = _composer.BuildRenderRequest(bank, items);
                var renderResult = await renderer.RenderAsync(renderRequest, ct);
                if (!renderResult.Succeeded)
                    throw new UnitOfWorkAbortException(Result.Failure(renderResult.Message ?? "Check rendering failed.", renderResult.Code ?? 500));

                file = CheckBatchComposer.BuildFile(renderer, $"checks_{bank.Name}_{DateTime.UtcNow:yyyyMMddHHmmss}", renderResult.Data!);
                return Result.Success();
            }, cancellationToken);

            if (!result.Succeeded)
                return Fail<CheckFileDto>(result.Message ?? "Printing failed.", result.Code ?? 400);
        }
        catch (UnitOfWorkAbortException ex)
        {
            return Fail<CheckFileDto>(ex.Result.Message ?? "Printing failed.", ex.Result.Code ?? 500);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<CheckFileDto>("A check number collided with an existing record. Reload and retry.", 409);
        }

        foreach (var check in created)
            await PublishEventAsync(new CheckIssuedEvent
            {
                CheckId = check.Id,
                BankAccountId = check.BankAccountId,
                CheckNumber = check.CheckNumber,
                PaymentEntryId = check.PaymentEntryId,
                TenantId = check.TenantId
            }, cancellationToken);

        return Ok(file!);
    }

    public async Task<Result<CheckFileDto>> PreviewAsync(PreviewChecksDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (_renderer == null)
            return Fail<CheckFileDto>(RendererMissingMessage, 501);

        var batch = await _composer.ResolveBatchAsync(input.PaymentEntryIds, "preview", cancellationToken);
        if (!batch.Succeeded)
            return Fail<CheckFileDto>(batch.Message!, batch.Code ?? 400);
        var bank = batch.Data!.Bank;

        // 支票号只 peek 不 consume：从档案当前 NextCheckNumber 起连号推演，
        // 真正的原子分配（含跳号/竞态处理）留给 PrintAsync。
        var previewNumber = bank.NextCheckNumber;
        var items = new List<CheckRenderItem>();
        foreach (var p in batch.Data.Payments)
        {
            var payee = batch.Data.Payees.GetValueOrDefault(p.PartyId);
            items.Add(CheckBatchComposer.BuildRenderItem(previewNumber++, p, payee, input.IssueDate?.ToUtcDate() ?? p.DocDate));
        }

        var request = _composer.BuildRenderRequest(bank, items);
        request.IsPreview = true;

        var renderResult = await _renderer.RenderAsync(request, cancellationToken);
        if (!renderResult.Succeeded)
            return Fail<CheckFileDto>(renderResult.Message ?? "Check rendering failed.", renderResult.Code ?? 500);

        return Ok(CheckBatchComposer.BuildFile(_renderer, $"checks_preview_{bank.Name}", renderResult.Data!));
    }

    public async Task<Result<CheckFileDto>> PreviewAdHocAsync(AdHocCheckPreviewDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (_renderer == null)
            return Fail<CheckFileDto>(RendererMissingMessage, 501);
        if (input.Items == null || input.Items.Count == 0)
            return Fail<CheckFileDto>("Select at least one cheque to preview.", 400);

        // 从出账（资金）科目解析银行账户档案（版式/偏移/抬头/支票号序列）。无付款单可引用，
        // 故直接按科目定位——与 PrintAsync 里 DepositToAccountId→档案 的映射同源。
        var bank = await _bankAccountRepository.AsNoTracking()
            .FirstOrDefaultAsync(b => b.AccountId == input.FundsAccountId, cancellationToken);
        if (bank == null)
            return Fail<CheckFileDto>("The funding account has no bank account profile. Configure one before previewing.", 400);

        var blankStockCheck = _composer.ValidateBlankStockPrintable(bank);
        if (!blankStockCheck.Succeeded)
            return Fail<CheckFileDto>(blankStockCheck.Message!, blankStockCheck.Code ?? 400);

        // 收款人按框架 Vendor 解析——与 PrintAsync 落库时同源，保证"预览==开票"。
        var payees = await _composer.LoadPayeesAsync(input.Items.Select(i => i.PayeeVendorId), cancellationToken);

        // 支票号只 peek 不 consume：从档案当前 NextCheckNumber 起连号推演，真正分配留给 PrintAsync。
        var issueDate = input.IssueDate?.ToUtcDate() ?? DateTime.UtcNow.ToUtcDate();
        var previewNumber = bank.NextCheckNumber;
        var items = input.Items.Select(it =>
        {
            var payee = payees.GetValueOrDefault(it.PayeeVendorId);
            // 币种缺省取本位币（框架不预设某国货币）；票面币种字样与金额大写须用同一个值。
            var currency = string.IsNullOrWhiteSpace(it.Currency) ? _options.BaseCurrency : it.Currency;
            return new CheckRenderItem
            {
                CheckNumber = previewNumber++,
                PayeeName = payee?.Name,
                PayeeAddressLines = CheckBatchComposer.SplitAddressLines(payee?.Address),
                Amount = it.Amount,
                Currency = currency,
                AmountInWords = CheckAmountInWords.Convert(it.Amount, currency),
                IssueDate = issueDate,
                Memo = it.Memo
            };
        }).ToList();

        var request = _composer.BuildRenderRequest(bank, items);
        request.IsPreview = true;

        var renderResult = await _renderer.RenderAsync(request, cancellationToken);
        if (!renderResult.Succeeded)
            return Fail<CheckFileDto>(renderResult.Message ?? "Check rendering failed.", renderResult.Code ?? 500);

        return Ok(CheckBatchComposer.BuildFile(_renderer, $"checks_preview_{bank.Name}", renderResult.Data!));
    }

    public async Task<Result<BankCheckDto>> RegisterManualAsync(RegisterManualCheckDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (input.CheckNumber < 1)
            return Fail<BankCheckDto>("The check number must be at least 1.", 400);

        var bank = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == input.BankAccountId, cancellationToken);
        if (bank == null)
            return Fail<BankCheckDto>("Bank account not found.", 404);

        var check = new BankCheck
        {
            BankAccountId = input.BankAccountId,
            CheckNumber = input.CheckNumber,
            Status = CheckStatus.Issued,
            PaymentEntryId = input.PaymentEntryId,
            PayeeName = input.PayeeName,
            Amount = input.Amount,
            Currency = string.IsNullOrWhiteSpace(input.Currency) ? bank.Currency : input.Currency.Trim().ToUpperInvariant(),
            IssueDate = input.IssueDate.ToUtcDate(),
            IsManual = true
        };

        var persisted = await InsertOccupyingCheckAsync(check, cancellationToken);
        if (!persisted.Succeeded)
            return Fail<BankCheckDto>(persisted.Message!, persisted.Code ?? 400);

        await PublishEventAsync(new CheckIssuedEvent
        {
            CheckId = check.Id,
            BankAccountId = check.BankAccountId,
            CheckNumber = check.CheckNumber,
            PaymentEntryId = check.PaymentEntryId,
            TenantId = check.TenantId
        }, cancellationToken);

        return await GetDtoAsync(check.Id, cancellationToken);
    }

    public async Task<Result<BankCheckDto>> VoidAsync(Guid id, VoidCheckDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var check = await _checkRepository.AsQueryable(true).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (check == null)
            return Fail<BankCheckDto>("Check not found.", 404);
        if (check.Status != CheckStatus.Issued)
            return Fail<BankCheckDto>("Only issued checks can be voided.", 409);

        check.Status = CheckStatus.Void;
        check.VoidReason = input.Reason;
        try
        {
            await _checkRepository.UpdateAsync(check, cancellationToken);
            await _checkRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<BankCheckDto>("The check was modified by another operation. Reload and retry.", 409);
        }

        await PublishEventAsync(new CheckVoidedEvent
        {
            CheckId = check.Id,
            BankAccountId = check.BankAccountId,
            CheckNumber = check.CheckNumber,
            Reason = input.Reason,
            TenantId = check.TenantId
        }, cancellationToken);

        return await GetDtoAsync(check.Id, cancellationToken);
    }

    public async Task<Result<BankCheckDto>> SpoilAsync(SpoilCheckDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (input.CheckNumber < 1)
            return Fail<BankCheckDto>("The check number must be at least 1.", 400);

        var bank = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == input.BankAccountId, cancellationToken);
        if (bank == null)
            return Fail<BankCheckDto>("Bank account not found.", 404);

        var check = new BankCheck
        {
            BankAccountId = input.BankAccountId,
            CheckNumber = input.CheckNumber,
            Status = CheckStatus.Spoiled,
            PaymentEntryId = null,
            IssueDate = DateTime.UtcNow.ToUtcDate(),
            IsManual = true,
            VoidReason = input.Reason
        };

        var persisted = await InsertOccupyingCheckAsync(check, cancellationToken);
        if (!persisted.Succeeded)
            return Fail<BankCheckDto>(persisted.Message!, persisted.Code ?? 400);

        return await GetDtoAsync(check.Id, cancellationToken);
    }

    public async Task<Result<CheckFileDto>> ReprintAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_renderer == null)
            return Fail<CheckFileDto>(RendererMissingMessage, 501);
        ICheckDocumentRenderer renderer = _renderer; // 上面已排除 null，捕获非空局部供 UoW 闭包使用

        var original = await _checkRepository.AsQueryable(true).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (original == null)
            return Fail<CheckFileDto>("Check not found.", 404);
        if (original.Status != CheckStatus.Issued)
            return Fail<CheckFileDto>("Only an issued check can be reprinted.", 409);
        if (original.PaymentEntryId == null)
            return Fail<CheckFileDto>("Only a check tied to a payment can be reprinted.", 400);

        var bank = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == original.BankAccountId, cancellationToken);
        if (bank == null)
            return Fail<CheckFileDto>("Bank account not found.", 404);

        var blankStockCheck = _composer.ValidateBlankStockPrintable(bank);
        if (!blankStockCheck.Succeeded)
            return Fail<CheckFileDto>(blankStockCheck.Message!, blankStockCheck.Code ?? 400);

        BankCheck? replacement = null;
        CheckFileDto? file = null;

        try
        {
            var result = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var checkNumber = await _allocator.AllocateAsync(bank.Id, ct);
                replacement = new BankCheck
                {
                    BankAccountId = bank.Id,
                    CheckNumber = checkNumber,
                    Status = CheckStatus.Issued,
                    PaymentEntryId = original.PaymentEntryId,
                    PayeeName = original.PayeeName,
                    Amount = original.Amount,
                    Currency = original.Currency,
                    IssueDate = DateTime.UtcNow.ToUtcDate(),
                    PrintedTime = DateTime.UtcNow,
                    IsManual = false,
                    TenantId = original.TenantId
                };
                await _checkRepository.InsertAsync(replacement, ct);
                await _checkRepository.SaveChangesAsync(ct);

                original.Status = CheckStatus.Void;
                original.VoidReason = "Reprinted";
                original.ReplacedByCheckId = replacement.Id;
                await _checkRepository.UpdateAsync(original, ct);

                // 参考号跟到新号：旧纸已止付，付款单对外呈现的支票号应当是仍然有效的那一张
                await StampPaymentReferenceAsync(original.PaymentEntryId!.Value, checkNumber, ct);

                var item = new CheckRenderItem
                {
                    CheckNumber = checkNumber,
                    PayeeName = original.PayeeName,
                    Amount = original.Amount ?? 0m,
                    Currency = original.Currency ?? _options.BaseCurrency,
                    AmountInWords = CheckAmountInWords.Convert(original.Amount ?? 0m, original.Currency),
                    IssueDate = replacement.IssueDate,
                    Memo = null
                };
                var renderResult = await renderer.RenderAsync(_composer.BuildRenderRequest(bank, new List<CheckRenderItem> { item }), ct);
                if (!renderResult.Succeeded)
                    throw new UnitOfWorkAbortException(Result.Failure(renderResult.Message ?? "Check rendering failed.", renderResult.Code ?? 500));

                file = CheckBatchComposer.BuildFile(renderer, $"check_{bank.Name}_{checkNumber}", renderResult.Data!);
                return Result.Success();
            }, cancellationToken);

            if (!result.Succeeded)
                return Fail<CheckFileDto>(result.Message ?? "Reprint failed.", result.Code ?? 400);
        }
        catch (UnitOfWorkAbortException ex)
        {
            return Fail<CheckFileDto>(ex.Result.Message ?? "Reprint failed.", ex.Result.Code ?? 500);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<CheckFileDto>("The check was modified by another operation. Reload and retry.", 409);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<CheckFileDto>("A check number collided with an existing record. Reload and retry.", 409);
        }

        await PublishEventAsync(new CheckVoidedEvent
        {
            CheckId = original.Id,
            BankAccountId = original.BankAccountId,
            CheckNumber = original.CheckNumber,
            Reason = "Reprinted",
            TenantId = original.TenantId
        }, cancellationToken);
        await PublishEventAsync(new CheckIssuedEvent
        {
            CheckId = replacement!.Id,
            BankAccountId = replacement.BankAccountId,
            CheckNumber = replacement.CheckNumber,
            PaymentEntryId = replacement.PaymentEntryId,
            TenantId = replacement.TenantId
        }, cancellationToken);

        return Ok(file!);
    }

    public async Task<Result<CheckFileDto>> RenderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_renderer == null)
            return Fail<CheckFileDto>(RendererMissingMessage, 501);
        ICheckDocumentRenderer renderer = _renderer; // 上面已排除 null，捕获非空局部（await 后字段 null-state 会重置）

        // 全程只读：登记簿快照即票面内容，不分配号、不建新票、不改状态、不动 PrintedTime。
        var check = await _checkRepository.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (check == null)
            return Fail<CheckFileDto>("Check not found.", 404);
        if (check.Status != CheckStatus.Issued)
            return Fail<CheckFileDto>(
                "Only an issued check can be re-rendered. A voided or spoiled check must never produce another negotiable sheet: print the replacement check on its reprint chain, or use reprint to void this one and issue a new number.",
                409);

        var bank = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == check.BankAccountId, cancellationToken);
        if (bank == null)
            return Fail<CheckFileDto>("Bank account not found.", 404);

        var blankStockCheck = _composer.ValidateBlankStockPrintable(bank);
        if (!blankStockCheck.Succeeded)
            return Fail<CheckFileDto>(blankStockCheck.Message!, blankStockCheck.Code ?? 400);

        // 摘要与首次打印同源（BuildRenderItem 取的就是付款单 Memo），保证"重打 == 首打"逐字一致
        string? memo = null;
        if (check.PaymentEntryId != null)
            memo = await _paymentRepository.AsNoTracking()
                .Where(p => p.Id == check.PaymentEntryId.Value)
                .Select(p => p.Memo)
                .FirstOrDefaultAsync(cancellationToken);

        // 票面币种字样与金额大写须用同一个值；登记簿未记币种时取本位币（框架不预设某国货币）
        var currency = string.IsNullOrWhiteSpace(check.Currency) ? _options.BaseCurrency : check.Currency;
        var amount = check.Amount ?? 0m;
        var item = new CheckRenderItem
        {
            CheckNumber = check.CheckNumber,
            PayeeName = check.PayeeName,
            Amount = amount,
            Currency = currency,
            AmountInWords = CheckAmountInWords.Convert(amount, currency),
            IssueDate = check.IssueDate,
            Memo = memo
        };

        // IsPreview 不设：这是真票，不该打不可流通水印
        var renderResult = await renderer.RenderAsync(_composer.BuildRenderRequest(bank, new List<CheckRenderItem> { item }), cancellationToken);
        if (!renderResult.Succeeded)
            return Fail<CheckFileDto>(renderResult.Message ?? "Check rendering failed.", renderResult.Code ?? 500);

        return Ok(CheckBatchComposer.BuildFile(renderer, $"check_{bank.Name}_{check.CheckNumber}", renderResult.Data!));
    }

    public async Task<Result<CheckFileDto>> GetCalibrationPdfAsync(Guid bankAccountId, CancellationToken cancellationToken = default)
    {
        if (_renderer == null)
            return Fail<CheckFileDto>(RendererMissingMessage, 501);
        ICheckDocumentRenderer renderer = _renderer; // 上面已排除 null，捕获非空局部（await 后字段 null-state 会重置）

        var bank = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bankAccountId, cancellationToken);
        if (bank == null)
            return Fail<CheckFileDto>("Bank account not found.", 404);

        var renderResult = await renderer.RenderCalibrationAsync(_composer.BuildRenderRequest(bank, new List<CheckRenderItem>()), cancellationToken);
        if (!renderResult.Succeeded)
            return Fail<CheckFileDto>(renderResult.Message!, renderResult.Code ?? 500);

        return Ok(CheckBatchComposer.BuildFile(renderer, $"calibration_{bank.Name}", renderResult.Data!));
    }

    public async Task<Result> VoidByPaymentAsync(Guid paymentEntryId, string reason, CancellationToken cancellationToken = default)
    {
        var checks = await _checkRepository.AsQueryable(true)
            .Where(c => c.PaymentEntryId == paymentEntryId && c.Status == CheckStatus.Issued)
            .ToListAsync(cancellationToken);
        if (checks.Count == 0)
            return Ok();

        foreach (var check in checks)
        {
            check.Status = CheckStatus.Void;
            check.VoidReason = reason;
            await _checkRepository.UpdateAsync(check, cancellationToken);
        }
        await _checkRepository.SaveChangesAsync(cancellationToken);

        foreach (var check in checks)
            await PublishEventAsync(new CheckVoidedEvent
            {
                CheckId = check.Id,
                BankAccountId = check.BankAccountId,
                CheckNumber = check.CheckNumber,
                Reason = reason,
                TenantId = check.TenantId
            }, cancellationToken);

        return Ok();
    }

    /// <summary>
    /// 把支票号回写到付款单的外部参考号（<see cref="PaymentEntry.Reference"/> 的框架语义即"支票号/交易号"），
    /// 使框架自身的付款列表 / 对账候选行 / 总账摘要都能显示出这笔付款是哪张支票付的。
    /// </summary>
    /// <remarks>
    /// MUST 在与支票插入<b>同一个 UoW</b> 内调用：渲染失败整体回滚时，支票号与参考号一并回滚。
    /// 付款单已被并发删除时静默跳过——支票本身已登记，不因参考号写不进去而否掉整笔开票。
    /// 作废支票（<see cref="VoidAsync"/>）<b>不</b>动参考号：付款单仍然是被那个号付的，历史事实不改写，
    /// 票据是否有效由登记簿状态回答。
    /// </remarks>
    private async Task StampPaymentReferenceAsync(Guid paymentEntryId, long checkNumber, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.AsQueryable(true).FirstOrDefaultAsync(p => p.Id == paymentEntryId, cancellationToken);
        if (payment == null)
            return;

        payment.Reference = checkNumber.ToString(CultureInfo.InvariantCulture);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
    }

    /// <summary>插入一张占号支票（Issued 手工 / Spoiled），并在其号 ≥ NextCheckNumber 时推进；撞号翻译 409。</summary>
    private async Task<Result> InsertOccupyingCheckAsync(BankCheck check, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                await _checkRepository.InsertAsync(check, ct);
                await _checkRepository.SaveChangesAsync(ct);

                // 号 ≥ 当前 NextCheckNumber 则推进（避免后续自动分配预期撞号）
                await _bankAccountRepository.AsQueryable(true)
                    .Where(b => b.Id == check.BankAccountId && b.NextCheckNumber <= check.CheckNumber)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextCheckNumber, check.CheckNumber + 1), ct);

                // 手写票也是支票号；毁票（PaymentEntryId 恒为 null）天然跳过
                if (check.PaymentEntryId != null)
                    await StampPaymentReferenceAsync(check.PaymentEntryId.Value, check.CheckNumber, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Result.Failure($"Check number {check.CheckNumber} already exists for this bank account.", 409);
        }
    }

    private async Task<Result<BankCheckDto>> GetDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        var check = await _checkRepository.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (check == null)
            return Fail<BankCheckDto>("Check not found.", 404);

        var dto = check.MapTo<BankCheckDto>();
        await FillNamesAsync(new List<BankCheckDto> { dto }, cancellationToken);
        return Ok(dto);
    }

    private async Task FillNamesAsync(IList<BankCheckDto> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        var bankIds = items.Select(c => c.BankAccountId).Distinct().ToList();
        var bankNames = await _bankAccountRepository.AsNoTracking()
            .Where(b => bankIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var paymentIds = items.Where(c => c.PaymentEntryId != null).Select(c => c.PaymentEntryId!.Value).Distinct().ToList();
        var paymentNumbers = paymentIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await _paymentRepository.AsNoTracking()
                .Where(p => paymentIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Number })
                .ToDictionaryAsync(p => p.Id, p => p.Number, cancellationToken);

        foreach (var dto in items)
        {
            dto.BankAccountName = bankNames.GetValueOrDefault(dto.BankAccountId);
            if (dto.PaymentEntryId != null)
                dto.PaymentNumber = paymentNumbers.GetValueOrDefault(dto.PaymentEntryId.Value);
        }
    }
}
