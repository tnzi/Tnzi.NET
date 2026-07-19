namespace Tnzi.Finance.Services;

/// <summary>
/// 支票打印与登记服务
/// </summary>
public class CheckService : ApplicationService, ICheckService
{
    private readonly IRepository<BankCheck, Guid> _checkRepository;
    private readonly IRepository<BankAccount, Guid> _bankAccountRepository;
    private readonly IReadOnlyRepository<PaymentEntry, Guid> _paymentRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly CheckNumberAllocator _allocator;
    private readonly ICheckDocumentRenderer? _renderer;
    private readonly IFinanceDataProtector _protector;
    private readonly FinanceOptions _options;

    /// <summary>未加载渲染子模块时，print/reprint/calibration 的 501 引导。</summary>
    private const string RendererMissingMessage =
        "Check rendering requires an ICheckDocumentRenderer implementation. Load the Tnzi.Finance.Documents module (or register your own ICheckDocumentRenderer) to enable check printing.";

    public CheckService(
        IServiceProvider serviceProvider,
        IRepository<BankCheck, Guid> checkRepository,
        IRepository<BankAccount, Guid> bankAccountRepository,
        IReadOnlyRepository<PaymentEntry, Guid> paymentRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        CheckNumberAllocator allocator,
        IFinanceDataProtector protector,
        IOptionsSnapshot<FinanceOptions> options,
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
        var vendorNames = await LoadVendorNamesAsync(pending.Select(p => p.PartyId), cancellationToken);

        var items = pending.Select(p =>
        {
            var bank = byLedger[p.DepositToAccountId!.Value];
            return new CheckQueueItemDto
            {
                PaymentEntryId = p.Id,
                PaymentNumber = p.Number,
                BankAccountId = bank.Id,
                BankAccountName = bank.Name,
                PayeeName = vendorNames.GetValueOrDefault(p.PartyId),
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
        if (input.PaymentEntryIds == null || input.PaymentEntryIds.Count == 0)
            return Fail<CheckFileDto>("Select at least one payment to print.", 400);

        var ids = input.PaymentEntryIds.Distinct().ToList();
        var payments = await _paymentRepository.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);
        if (payments.Count != ids.Count)
            return Fail<CheckFileDto>("One or more payments were not found.", 404);

        // 校验队列资格：均为 Posted Outbound Check
        foreach (var p in payments)
        {
            if (p.Status != FinanceDocumentStatus.Posted || p.Direction != PaymentDirection.Outbound)
                return Fail<CheckFileDto>($"Payment '{p.Number ?? p.Id.ToString()}' is not a posted outbound payment.", 400);
            if (p.DepositToAccountId == null)
                return Fail<CheckFileDto>($"Payment '{p.Number ?? p.Id.ToString()}' has no funding account.", 400);
        }

        // 均须解析到同一银行账户档案（单份 PDF 共享版式/偏移/MICR）
        var ledgerIds = payments.Select(p => p.DepositToAccountId!.Value).Distinct().ToList();
        var bankAccounts = await _bankAccountRepository.AsNoTracking()
            .Where(b => ledgerIds.Contains(b.AccountId))
            .ToListAsync(cancellationToken);
        var byLedger = bankAccounts.ToDictionary(b => b.AccountId);
        if (payments.Any(p => !byLedger.ContainsKey(p.DepositToAccountId!.Value)))
            return Fail<CheckFileDto>("A payment's funding account has no bank account profile. Configure one before printing.", 400);
        var distinctBanks = payments.Select(p => byLedger[p.DepositToAccountId!.Value].Id).Distinct().ToList();
        if (distinctBanks.Count != 1)
            return Fail<CheckFileDto>("All selected payments must draw on the same bank account.", 400);

        var bank = byLedger[payments[0].DepositToAccountId!.Value];

        var blankStockCheck = ValidateBlankStockPrintable(bank);
        if (!blankStockCheck.Succeeded)
            return Fail<CheckFileDto>(blankStockCheck.Message!, blankStockCheck.Code ?? 400);

        // 已开票的付款不能重复打印（重打走 ReprintAsync）
        var alreadyIssued = await _checkRepository.AnyAsync(
            c => c.Status == CheckStatus.Issued && c.PaymentEntryId != null && ids.Contains(c.PaymentEntryId.Value), cancellationToken);
        if (alreadyIssued)
            return Fail<CheckFileDto>("One or more payments already have an issued check. Use reprint instead.", 409);

        var vendorNames = await LoadVendorNamesAsync(payments.Select(p => p.PartyId), cancellationToken);
        var ordered = payments.OrderBy(p => p.Number).ThenBy(p => p.Id).ToList();

        var created = new List<BankCheck>();
        CheckFileDto? file = null;

        try
        {
            var result = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var items = new List<CheckRenderItem>();
                foreach (var p in ordered)
                {
                    var checkNumber = await _allocator.AllocateAsync(bank.Id, ct);
                    var payee = vendorNames.GetValueOrDefault(p.PartyId);
                    var check = new BankCheck
                    {
                        BankAccountId = bank.Id,
                        CheckNumber = checkNumber,
                        Status = CheckStatus.Issued,
                        PaymentEntryId = p.Id,
                        PayeeName = payee,
                        Amount = p.Amount,
                        Currency = p.Currency,
                        IssueDate = input.IssueDate?.ToUtcDate() ?? p.DocDate,
                        PrintedTime = DateTime.UtcNow,
                        IsManual = false,
                        TenantId = p.TenantId
                    };
                    await _checkRepository.InsertAsync(check, ct);
                    created.Add(check);

                    items.Add(new CheckRenderItem
                    {
                        CheckNumber = checkNumber,
                        PayeeName = payee,
                        Amount = p.Amount,
                        Currency = p.Currency,
                        AmountInWords = CheckAmountInWords.Convert(p.Amount, p.Currency),
                        IssueDate = check.IssueDate,
                        Memo = p.Memo
                    });
                }

                var renderRequest = BuildRenderRequest(bank, items);
                var renderResult = renderer.Render(renderRequest);
                if (!renderResult.Succeeded)
                    throw new UnitOfWorkAbortException(Result.Failure(renderResult.Message ?? "Check rendering failed.", renderResult.Code ?? 500));

                file = new CheckFileDto { FileName = $"checks_{bank.Name}_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf", Content = renderResult.Data! };
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

        var blankStockCheck = ValidateBlankStockPrintable(bank);
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
                var renderResult = renderer.Render(BuildRenderRequest(bank, new List<CheckRenderItem> { item }));
                if (!renderResult.Succeeded)
                    throw new UnitOfWorkAbortException(Result.Failure(renderResult.Message ?? "Check rendering failed.", renderResult.Code ?? 500));

                file = new CheckFileDto { FileName = $"check_{bank.Name}_{checkNumber}.pdf", Content = renderResult.Data! };
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

    public async Task<Result<CheckFileDto>> GetCalibrationPdfAsync(Guid bankAccountId, CancellationToken cancellationToken = default)
    {
        if (_renderer == null)
            return Fail<CheckFileDto>(RendererMissingMessage, 501);
        ICheckDocumentRenderer renderer = _renderer; // 上面已排除 null，捕获非空局部（await 后字段 null-state 会重置）

        var bank = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bankAccountId, cancellationToken);
        if (bank == null)
            return Fail<CheckFileDto>("Bank account not found.", 404);

        var renderResult = renderer.RenderCalibration(BuildRenderRequest(bank, new List<CheckRenderItem>()));
        if (!renderResult.Succeeded)
            return Fail<CheckFileDto>(renderResult.Message!, renderResult.Code ?? 500);

        return Ok(new CheckFileDto { FileName = $"calibration_{bank.Name}.pdf", Content = renderResult.Data! });
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

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Result.Failure($"Check number {check.CheckNumber} already exists for this bank account.", 409);
        }
    }

    /// <summary>
    /// 空白票纸打印前置校验：Blank 票纸须现打 MICR 行，故须有 scheme 有效的路由/transit + 可解密账号，
    /// 否则打出结构在但空路由（Transit 括号内空）/ 整条 MICR 被丢弃的不可流通票据（银行拒付/误路由）。
    /// 预印票纸（PrePrinted）MICR 已印在票纸上，跳过。
    /// </summary>
    private Result ValidateBlankStockPrintable(BankAccount bank)
    {
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

    private CheckRenderRequest BuildRenderRequest(BankAccount bank, List<CheckRenderItem> items)
    {
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
                Logger.LogWarning(ex, "Failed to decrypt bank account number for MICR rendering; MICR line will be omitted.");
            }
        }

        return request;
    }

    private async Task<Dictionary<Guid, string>> LoadVendorNamesAsync(IEnumerable<Guid> partyIds, CancellationToken cancellationToken)
    {
        var ids = partyIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        return await _vendorRepository.AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Name })
            .ToDictionaryAsync(v => v.Id, v => v.Name, cancellationToken);
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
