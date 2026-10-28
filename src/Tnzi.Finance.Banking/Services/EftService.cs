namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// EFT 批次服务
/// </summary>
public class EftService : ApplicationService, IEftService
{
    private readonly IRepository<EftBatch, Guid> _batchRepository;
    private readonly IRepository<EftBatchLine, Guid> _lineRepository;
    private readonly IRepository<BankAccount, Guid> _bankAccountRepository;
    private readonly IReadOnlyRepository<PaymentEntry, Guid> _paymentRepository;
    private readonly IReadOnlyRepository<PartyBankAccount, Guid> _partyBankRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly IDocumentNumberService _numberService;
    private readonly IEftFileComposer _composer;
    private readonly IFinanceDataProtector _protector;
    private readonly FinanceOptions _options;

    public EftService(
        IServiceProvider serviceProvider,
        IRepository<EftBatch, Guid> batchRepository,
        IRepository<EftBatchLine, Guid> lineRepository,
        IRepository<BankAccount, Guid> bankAccountRepository,
        IReadOnlyRepository<PaymentEntry, Guid> paymentRepository,
        IReadOnlyRepository<PartyBankAccount, Guid> partyBankRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        IDocumentNumberService numberService,
        IEftFileComposer composer,
        IFinanceDataProtector protector,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _batchRepository = Check.NotNull(batchRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _bankAccountRepository = Check.NotNull(bankAccountRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _partyBankRepository = Check.NotNull(partyBankRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _numberService = Check.NotNull(numberService);
        _composer = Check.NotNull(composer);
        _protector = Check.NotNull(protector);
        _options = Check.NotNull(options).Value;
    }

    /// <summary>待付队列的付款方式判据。<c>PaymentMethods.BankTransfer</c> 的小写形式 ——
    /// EF 要在 SQL 里比较，拿不到 <c>StringComparison</c>，只能比小写；写字面量的话，
    /// 常量改名后这个队列会静默变空，而装批那一侧（用常量）照常工作。</summary>
    private static readonly string BankTransferMethodLower = PaymentMethods.BankTransfer.ToLowerInvariant();

    private static string CurrencyForFormat(EftFileFormat format) => format == EftFileFormat.Nacha ? "USD" : "CAD";
    private static BankNumberScheme SchemeForFormat(EftFileFormat format) => format == EftFileFormat.Nacha ? BankNumberScheme.UsAba : BankNumberScheme.CaEft;

    public async Task<Result<List<EftQueueItemDto>>> GetQueueAsync(CancellationToken cancellationToken = default)
    {
        // ★ 「已装进某个批次」的排除在 SQL 里做（反连接），只取队列要显示的列：先把历年全部转账付款
        // 整行读出、再把它们的 Id 当 IN 列表去查批次行，代价随历史增长而不是随待付的款增长。
        var batchLines = _lineRepository.AsNoTracking();
        var candidates = await _paymentRepository.AsNoTracking()
            .Where(p => p.Status == FinanceDocumentStatus.Posted
                && p.Direction == PaymentDirection.Outbound
                && p.PaymentMethod != null && p.PaymentMethod.ToLower() == BankTransferMethodLower
                && !batchLines.Any(l => l.PaymentEntryId == p.Id))
            .OrderBy(p => p.Number)
            .Select(p => new { p.Id, p.Number, p.DepositToAccountId, p.PartyType, p.PartyId, p.DocDate, p.Currency, p.Amount })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
            return Ok(new List<EftQueueItemDto>());

        var partyIds = candidates.Select(p => p.PartyId).Distinct().ToList();
        var defaults = await _partyBankRepository.AsNoTracking()
            .Where(a => a.IsDefault && a.IsActive && partyIds.Contains(a.PartyId))
            .ToListAsync(cancellationToken);
        var defaultByParty = defaults
            .GroupBy(a => (a.PartyType, a.PartyId))
            .ToDictionary(g => g.Key, g => g.First());

        var vendorNames = await LoadVendorNamesAsync(candidates.Select(p => p.PartyId), cancellationToken);

        var items = new List<EftQueueItemDto>();
        foreach (var p in candidates)
        {
            if (!defaultByParty.TryGetValue((p.PartyType, p.PartyId), out var bank))
                continue;
            items.Add(new EftQueueItemDto
            {
                PaymentEntryId = p.Id,
                PaymentNumber = p.Number,
                FundingAccountId = p.DepositToAccountId,
                PartyType = p.PartyType,
                PartyId = p.PartyId,
                PayeeName = vendorNames.GetValueOrDefault(p.PartyId),
                DocDate = p.DocDate,
                Currency = p.Currency,
                Amount = p.Amount,
                PartyBankAccountId = bank.Id,
                PartyBankAccountMasked = bank.AccountNumberMasked,
                PartyScheme = bank.Scheme
            });
        }

        return Ok(items);
    }

    public async Task<Result<IPagedList<EftBatchDto>>> GetPagedAsync(EftBatchQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _batchRepository.AsNoTracking();
        if (query.BankAccountId.HasValue)
            queryable = queryable.Where(b => b.BankAccountId == query.BankAccountId.Value);
        if (query.Status.HasValue)
            queryable = queryable.Where(b => b.Status == query.Status.Value);
        if (query.Format.HasValue)
            queryable = queryable.Where(b => b.Format == query.Format.Value);

        var pagedList = await queryable
            .OrderByDescending(b => b.CreationTime)
            .ProjectTo<EftBatch, EftBatchDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        await FillBankNamesAsync(pagedList.Items, cancellationToken);
        return Ok(pagedList);
    }

    public async Task<Result<EftBatchDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var batch = await _batchRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch == null)
            return Fail<EftBatchDto>("EFT batch not found.", 404);

        return Ok(await ToDtoAsync(batch, cancellationToken));
    }

    public async Task<Result<EftBatchDto>> CreateBatchAsync(CreateEftBatchDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (input.PaymentEntryIds == null || input.PaymentEntryIds.Count == 0)
            return Fail<EftBatchDto>("Select at least one payment for the batch.", 400);

        var bank = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == input.BankAccountId, cancellationToken);
        if (bank == null)
            return Fail<EftBatchDto>("Bank account not found.", 404);

        var scheme = SchemeForFormat(input.Format);
        if (bank.Scheme != scheme)
            return Fail<EftBatchDto>($"The {input.Format} format requires a {scheme} bank account.", 400);

        // The effective (settlement) date must not be in the past: an ODFI rejects
        // a file whose effective date has already passed. Banking-day/holiday checks
        // are out of scope (they need a per-region calendar); this catches the common
        // "dated last week" mistake up front instead of at bank submission.
        if (IsEffectiveDatePast(input.EffectiveDate))
            return Fail<EftBatchDto>("The effective date cannot be in the past.", 400);

        var currency = CurrencyForFormat(input.Format);

        var ids = input.PaymentEntryIds.Distinct().ToList();
        var payments = await _paymentRepository.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        if (payments.Count != ids.Count)
            return Fail<EftBatchDto>("One or more payments were not found.", 404);

        var partyIds = payments.Select(p => p.PartyId).Distinct().ToList();
        var defaults = await _partyBankRepository.AsNoTracking()
            .Where(a => a.IsDefault && a.IsActive && partyIds.Contains(a.PartyId))
            .ToListAsync(cancellationToken);
        var defaultByParty = defaults
            .GroupBy(a => (a.PartyType, a.PartyId))
            .ToDictionary(g => g.Key, g => g.First());

        var vendorNames = await LoadVendorNamesAsync(payments.Select(p => p.PartyId), cancellationToken);

        var lines = new List<EftBatchLine>();
        decimal total = 0m;
        foreach (var p in payments)
        {
            if (p.Status != FinanceDocumentStatus.Posted || p.Direction != PaymentDirection.Outbound)
                return Fail<EftBatchDto>($"Payment '{p.Number ?? p.Id.ToString()}' is not a posted outbound payment.", 400);
            if (string.IsNullOrWhiteSpace(p.PaymentMethod) || !string.Equals(p.PaymentMethod, PaymentMethods.BankTransfer, StringComparison.OrdinalIgnoreCase))
                return Fail<EftBatchDto>($"Payment '{p.Number ?? p.Id.ToString()}' is not a bank transfer.", 400);
            // 防御性：本实现的两种格式都只出 credit，金额字段没有符号位。核心的付款路径
            // （草稿校验与过账校验）都要求金额为正，所以这条目前不可达 —— 但一旦某天可达，
            // 静默翻正的后果是「账上收进一笔、银行付出同一笔」，两边金额相同、方向相反。
            if (p.Amount <= 0)
                return Fail<EftBatchDto>($"Payment '{p.Number ?? p.Id.ToString()}' has a non-positive amount and cannot be sent as a credit.", 400);
            if (!string.Equals(p.Currency, currency, StringComparison.OrdinalIgnoreCase))
                return Fail<EftBatchDto>($"Payment '{p.Number ?? p.Id.ToString()}' currency {p.Currency} does not match the {input.Format} currency {currency}.", 400);
            // 付款过账时贷的是 DepositToAccountId 这个资金科目；文件让银行从所选档案的账户出钱。
            // 两者不是同一个账户时，钱从 B 户付出、账上却记 A 户减少，两边的银行对账都对不平，
            // 而每一步都返回成功。与支票路径（CheckBatchComposer）同一条不变量、同一句报错。
            if (p.DepositToAccountId != bank.AccountId)
                return Fail<EftBatchDto>($"Payment '{p.Number ?? p.Id.ToString()}' is not funded from the selected bank account.", 400);

            if (!defaultByParty.TryGetValue((p.PartyType, p.PartyId), out var partyBank))
                return Fail<EftBatchDto>($"Payee for payment '{p.Number ?? p.Id.ToString()}' has no default bank account on file.", 400);
            if (partyBank.Scheme != scheme)
                return Fail<EftBatchDto>($"Payee bank account for payment '{p.Number ?? p.Id.ToString()}' does not match the {input.Format} scheme.", 400);
            if (string.IsNullOrWhiteSpace(partyBank.AccountNumberEncrypted))
                return Fail<EftBatchDto>($"Payee bank account for payment '{p.Number ?? p.Id.ToString()}' has no account number on file.", 400);
            // 档案允许只登记名称/账号（路由留空合法），但这样的档案装不进 EFT 文件 ——
            // 定宽写入器会把空路由补成全零。在装批时就点名是哪一笔，比生成时才报错有用得多。
            if (!BankNumberHelper.HasTransferRouting(partyBank.Scheme, partyBank.RoutingNumber, partyBank.InstitutionNumber, partyBank.TransitNumber))
                return Fail<EftBatchDto>($"Payee bank account for payment '{p.Number ?? p.Id.ToString()}' has no complete routing number on file.", 400);

            lines.Add(new EftBatchLine
            {
                PaymentEntryId = p.Id,
                PartyBankAccountId = partyBank.Id,
                Amount = p.Amount,
                PayeeName = vendorNames.GetValueOrDefault(p.PartyId),
                TenantId = p.TenantId
            });
            total += p.Amount;
        }

        var batch = new EftBatch
        {
            BankAccountId = input.BankAccountId,
            Format = input.Format,
            Currency = currency,
            EffectiveDate = input.EffectiveDate.ToUtcDate(),
            Status = EftBatchStatus.Draft,
            TotalCount = lines.Count,
            TotalAmount = total
        };

        try
        {
            var result = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                await _batchRepository.InsertAsync(batch, ct);
                foreach (var line in lines)
                {
                    line.EftBatchId = batch.Id;
                }
                await _lineRepository.InsertManyAsync(lines, ct);
                await _lineRepository.SaveChangesAsync(ct);
                return Result.Success();
            }, cancellationToken);
            if (!result.Succeeded)
                return Fail<EftBatchDto>(result.Message ?? "Failed to create batch.", result.Code ?? 400);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<EftBatchDto>("One or more payments are already in another batch. Reload and retry.", 409);
        }

        return await GetAsync(batch.Id, cancellationToken);
    }

    public async Task<Result<EftBatchDto>> GenerateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var batch = await _batchRepository.AsQueryable(true).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch == null)
            return Fail<EftBatchDto>("EFT batch not found.", 404);
        if (batch.Status != EftBatchStatus.Draft)
            return Fail<EftBatchDto>("Only a draft batch can be generated. Void and rebuild to change a generated batch.", 409);

        // ★ 装批时判过一次不够：草稿批次可以搁置任意天，几天后生成的文件会带着一个已经过去的生效日。
        // 生效日在批次上不可改（改了就是另一个批次），所以出路是作废重建；在分配任何号之前拒绝。
        if (IsEffectiveDatePast(batch.EffectiveDate))
        {
            return Fail<EftBatchDto>(
                $"The batch's effective date {batch.EffectiveDate:yyyy-MM-dd} has passed. Void the batch and build a new one with a current effective date.", 409);
        }

        if (!_protector.IsConfigured)
            return Fail<EftBatchDto>("Configure Finance:Encryption:EncryptionKey before generating EFT files.", 400);

        var bank = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batch.BankAccountId, cancellationToken);
        if (bank == null)
            return Fail<EftBatchDto>("Bank account not found.", 404);
        if (string.IsNullOrWhiteSpace(bank.EftOriginatorId) || string.IsNullOrWhiteSpace(bank.EftOriginatorName))
            return Fail<EftBatchDto>("Configure the bank account's EFT originator id and name before generating a file.", 400);

        var lines = await _lineRepository.AsNoTracking().Where(l => l.EftBatchId == batch.Id).ToListAsync(cancellationToken);
        if (lines.Count == 0)
            return Fail<EftBatchDto>("The batch has no lines to generate.", 400);

        var partyBankIds = lines.Select(l => l.PartyBankAccountId).Distinct().ToList();
        var partyBanks = await _partyBankRepository.AsNoTracking()
            .Where(a => partyBankIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        // ★ 存储的账号逐条解密、解不开就点名拒绝（400），而不是让异常一路冒成 500：
        // 密钥更换或把库恢复到另一把密钥的环境之后，操作员需要知道是哪一个档案要重新录入账号。
        // 本方账号只有 CPA-005 写进文件（NACHA 没有这个字段），所以只在 CPA-005 时解它，
        // 否则一个用不上的字段会让 NACHA 批次一起生成不了。全部发生在分配批次号与文件序号之前。
        string? originatorAccount = null;
        if (batch.Format == EftFileFormat.Cpa005)
        {
            originatorAccount = TryDecrypt(bank.AccountNumberEncrypted, FinanceProtectionAad.ForBankAccount(bank.AccountId), "bank account", bank.Id);
            if (originatorAccount == null)
            {
                return Fail<EftBatchDto>(
                    $"The stored account number on bank account '{bank.Name}' cannot be decrypted (the encryption key may have changed). "
                    + "Re-enter the account number before generating the file.", 400);
            }
        }

        // 明文账号仅在内存栈拼装文件
        var request = new EftComposeRequest
        {
            Format = batch.Format,
            Currency = batch.Currency,
            EffectiveDate = batch.EffectiveDate,
            CreationTime = DateTime.UtcNow,
            OriginatorId = bank.EftOriginatorId,
            OriginatorName = bank.EftOriginatorName,
            BankName = bank.BankName,
            OriginatorRoutingNumber = bank.RoutingNumber,
            OriginatorInstitutionNumber = bank.InstitutionNumber,
            OriginatorTransitNumber = bank.TransitNumber,
            OriginatorAccountNumber = originatorAccount
        };

        foreach (var line in lines)
        {
            if (!partyBanks.TryGetValue(line.PartyBankAccountId, out var partyBank))
                return Fail<EftBatchDto>("A payee bank account referenced by the batch no longer exists.", 400);
            var payeeAccount = TryDecrypt(partyBank.AccountNumberEncrypted,
                FinanceProtectionAad.ForPartyBankAccount(partyBank.PartyType, partyBank.PartyId), "payee bank account", partyBank.Id);
            if (payeeAccount == null)
            {
                return Fail<EftBatchDto>(
                    $"The stored account number for payee '{line.PayeeName ?? partyBank.Label ?? partyBank.Id.ToString()}' cannot be decrypted (the encryption key may have changed). "
                    + "Re-enter the payee's account number before generating the file.", 400);
            }
            request.Entries.Add(new EftComposeEntry
            {
                PayeeName = line.PayeeName ?? string.Empty,
                RoutingNumber = partyBank.RoutingNumber,
                InstitutionNumber = partyBank.InstitutionNumber,
                TransitNumber = partyBank.TransitNumber,
                AccountNumber = payeeAccount,
                AccountType = partyBank.AccountType,
                Amount = line.Amount,
                Reference = null
            });
        }

        var total = lines.Sum(l => l.Amount);

        try
        {
            var result = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var number = await _numberService.NextFormattedAsync(nameof(EftBatch), _options.EftNumberPrefix, _options.JournalNumberPadding, ct);
                var fileCreationNumber = await AllocateFileCreationNumberAsync(bank.Id, ct);
                request.FileCreationNumber = fileCreationNumber;

                var composed = _composer.Compose(request);
                if (!composed.Succeeded)
                    throw new UnitOfWorkAbortException(Result.Failure(composed.Message ?? "EFT composition failed.", composed.Code ?? 400));

                batch.Number = number;
                batch.FileCreationNumber = fileCreationNumber;
                batch.Status = EftBatchStatus.Generated;
                batch.GeneratedTime = DateTime.UtcNow;
                batch.TotalCount = lines.Count;
                batch.TotalAmount = total;
                batch.FileName = $"{number}.{composed.Data!.FileExtension}";
                batch.FileContentEncrypted = _protector.Protect(composed.Data.Content);
                await _batchRepository.UpdateAsync(batch, ct);
                return Result.Success();
            }, cancellationToken);
            if (!result.Succeeded)
                return Fail<EftBatchDto>(result.Message ?? "Failed to generate the file.", result.Code ?? 400);
        }
        catch (UnitOfWorkAbortException ex)
        {
            return Fail<EftBatchDto>(ex.Result.Message ?? "Failed to generate the file.", ex.Result.Code ?? 400);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<EftBatchDto>("The batch was modified by another operation. Reload and retry.", 409);
        }

        await PublishEventAsync(new EftBatchGeneratedEvent
        {
            BatchId = batch.Id,
            Number = batch.Number,
            BankAccountId = batch.BankAccountId,
            Format = batch.Format,
            TotalCount = batch.TotalCount,
            TotalAmount = batch.TotalAmount,
            TenantId = batch.TenantId
        }, cancellationToken);

        return await GetAsync(batch.Id, cancellationToken);
    }

    public async Task<Result<EftFileDto>> DownloadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var batch = await _batchRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch == null)
            return Fail<EftFileDto>("EFT batch not found.", 404);
        if (batch.Status != EftBatchStatus.Generated || string.IsNullOrEmpty(batch.FileContentEncrypted))
            return Fail<EftFileDto>("The batch has no generated file. Generate it first.", 400);
        if (!_protector.IsConfigured)
            return Fail<EftFileDto>("Configure Finance:Encryption:EncryptionKey to download EFT files.", 400);

        string content;
        try
        {
            content = _protector.Unprotect(batch.FileContentEncrypted!);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to decrypt EFT file for batch {BatchId}.", batch.Id);
            return Fail<EftFileDto>("The EFT file could not be decrypted.", 500);
        }

        // ★ 留痕与状态核对是同一条条件写：上面那次读取之后批次可能已被并发作废（行已硬删、
        // 付款已回到待付队列），这时再把文件交出去，它们会被再装一批付第二次。
        if (!await StampHandoutAsync(batch.Id, cancellationToken))
            return Fail<EftFileDto>("The batch was voided by another operation. Reload and retry.", 409);

        return Ok(new EftFileDto
        {
            FileName = batch.FileName ?? $"{batch.Number ?? batch.Id.ToString()}.txt",
            // ASCII 而不是 UTF-8：写入器已保证内容纯 ASCII（EftFieldWriter），这里是第二道闸 ——
            // 万一漏网一个非 ASCII 字符，它变成一个可见的 '?' 而不是让其后每个字段错位。
            Content = Encoding.ASCII.GetBytes(content)
        });
    }

    public async Task<Result<EftBatchDto>> VoidBatchAsync(Guid id, VoidEftBatchDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        var reasonTooLong = BankingFieldLimits.FirstTooLong((input.Reason, BankingFieldLimits.VoidReasonMaxLength, "reason"));
        if (reasonTooLong != null)
            return Fail<EftBatchDto>(reasonTooLong, 400);

        var batch = await _batchRepository.AsQueryable(true).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch == null)
            return Fail<EftBatchDto>("EFT batch not found.", 404);
        if (batch.Status == EftBatchStatus.Voided)
            return Fail<EftBatchDto>("The batch is already voided.", 409);

        // ★ 文件交出去过就不能顺手作废：作废硬删批次行，那些付款回到待付队列、
        // 看上去从没付过，于是会被再装一批发出去 —— 而第二笔看起来完全正常。
        // 框架无从知道文件是否真的上传到了银行门户，所以把这个判断交还给操作者，
        // 但**必须是显式的**：默认放行的话，重复付款就成了不需要任何人点头的默认行为。
        //
        // ★★ 判据必须绕开变更跟踪器重新读一次，不能用上面那个 batch 实例：
        // 留痕是 ExecuteUpdate 写的，而 ExecuteUpdate **不会更新已跟踪的实体**。
        // 同一个 scope 里先下载再作废（后台任务、批处理）时，EF 的标识解析会把
        // 跟踪器里那份 FirstDownloadedTime=null 的旧副本交还给我们，守卫就此静默失效 ——
        // 而「守卫读到陈旧值于是不再设防」恰恰不会让任何测试变红。
        var handedOutAt = await _batchRepository.AsNoTracking()
            .Where(b => b.Id == batch.Id)
            .Select(b => b.FirstDownloadedTime)
            .FirstOrDefaultAsync(cancellationToken);
        if (handedOutAt.HasValue && !input.AcknowledgeFileNotSubmitted)
            return Fail<EftBatchDto>(
                $"The file for this batch was already downloaded on {handedOutAt.Value:yyyy-MM-dd HH:mm} UTC. "
                + "Voiding returns its payments to the unpaid queue, so they can be paid again. "
                + "Confirm the file was never submitted to the bank to proceed.",
                409);

        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var lines = await _lineRepository.ToListAsync(l => l.EftBatchId == batch.Id, ct);
                if (lines.Count > 0)
                {
                    // 行是硬删的（唯一索引无软删过滤，见 EftBatchLineConfiguration），
                    // 删完就再也答不出「这个批次里装的是哪几笔」。已交出过文件的批次尤其要留痕：
                    // 万一真的付了两次，第一次付了什么全靠这条日志。
                    // 同上：交出留痕取自刚才那次无跟踪读取，不取跟踪器里的副本。
                    if (handedOutAt.HasValue)
                        Logger.LogWarning(
                            "Voiding EFT batch {BatchNumber} ({BatchId}) whose file was handed out since {FirstDownloadedTime}. Released payments: {PaymentEntryIds}. Reason: {Reason}",
                            batch.Number, batch.Id, handedOutAt,
                            string.Join(", ", lines.Select(l => l.PaymentEntryId)), input.Reason);

                    await _lineRepository.DeleteManyAsync(lines, ct);
                }

                batch.Status = EftBatchStatus.Voided;
                batch.VoidReason = input.Reason;
                await _batchRepository.UpdateAsync(batch, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<EftBatchDto>("The batch was modified by another operation. Reload and retry.", 409);
        }

        return await GetAsync(batch.Id, cancellationToken);
    }

    /// <summary>
    /// 记下「文件被交出去了」—— 首次时间取最早那次，次数每次递增。仅当批次仍是
    /// <see cref="EftBatchStatus.Generated"/> 时生效；返回 <c>false</c> = 批次已被并发作废，文件不得交出。
    /// </summary>
    /// <remarks>
    /// ★ 走 <c>ExecuteUpdate</c> 而不是加载实体再保存：后者会经过变更跟踪器，从而对
    /// <see cref="EftBatch.ConcurrencyStamp"/> 做乐观并发比对，两个人同时下载就会有一个拿到 409 ——
    /// 让一个纯粹的读动作因为并发而失败。这里的写入是单调的（时间取 coalesce、次数自增），
    /// 下载之间天然不需要并发校验。
    /// <para>
    /// ★★ 但它必须**换掉**并发标记，并且只在 Generated 态生效，作废守卫与交出文件之间
    /// 两个方向的竞态都靠这两点关上：
    /// 下载读到 Generated 之后作废先提交，条件写命中 0 行，文件不交出；
    /// 作废读到「从未下载」之后下载先戳，作废那次带跟踪的整行写入比对的是旧标记，
    /// 命中 0 行转 409 并整体回滚（行不删、付款不放回队列），也不会把刚戳上的留痕写回 null。
    /// 下载之间不受影响：它们从不做带跟踪的比对。
    /// </para>
    /// <para>
    /// 首次时间用 <c>x.FirstDownloadedTime ?? now</c> 而不是「先查再判断」：并发下载下
    /// 后者会让两次都认为自己是第一次，把首次时间改晚 —— 而这个字段的全部意义就是
    /// 「最早交出去是什么时候」。
    /// </para>
    /// </remarks>
    private async Task<bool> StampHandoutAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var stamp = Guid.NewGuid().ToString("N");
        var affected = await _batchRepository.AsQueryable(true)
            .Where(b => b.Id == batchId && b.Status == EftBatchStatus.Generated)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.FirstDownloadedTime, x => x.FirstDownloadedTime ?? now)
                .SetProperty(x => x.DownloadCount, x => x.DownloadCount + 1)
                .SetProperty(x => x.ConcurrencyStamp, stamp), cancellationToken);
        return affected == 1;
    }

    /// <summary>原子递增 <see cref="BankAccount.EftFileCreationNumber"/> 并返回 1-9999 循环序号（须在活动事务内）。</summary>
    /// <remarks>
    /// ★ 同一条语句换掉档案的并发标记（与 <see cref="CheckNumberAllocator"/> 同一口径）：在分配之前读过档案的
    /// 实体级整行写入（档案编辑）会比对失败转 409，而不是把序号写回旧值，回退后下一份文件复用一个
    /// 已用过的 File Creation Number，银行按重复文件拒收。
    /// </remarks>
    private async Task<int> AllocateFileCreationNumberAsync(Guid bankAccountId, CancellationToken cancellationToken)
    {
        await _bankAccountRepository.EnsureTransactionStartedAsync(cancellationToken);
        var stamp = Guid.NewGuid().ToString("N");
        await _bankAccountRepository.AsQueryable(true)
            .Where(b => b.Id == bankAccountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.EftFileCreationNumber, x => x.EftFileCreationNumber + 1)
                .SetProperty(x => x.ConcurrencyStamp, stamp), cancellationToken);
        var raw = await _bankAccountRepository.AsQueryable()
            .Where(b => b.Id == bankAccountId)
            .Select(b => b.EftFileCreationNumber)
            .FirstAsync(cancellationToken);
        return ((raw - 1) % 9999) + 1;
    }

    /// <summary>
    /// 生效日是否已经过去：按「在任何时区都已经过去」判断，时钟取注入的 <see cref="TimeProvider"/>。
    /// </summary>
    /// <remarks>
    /// 管理端发的是浏览器本地的日历日，而本模块没有业务时区设置：拿 UTC 日期比，北美晚上八点之后
    /// UTC 已是明天，表单默认值（今天）被当成过去拒掉。所以只拒绝早于「此刻全球最早那个日历日」
    /// （UTC-12）的日期，那在任何时区都已经过去了。
    /// </remarks>
    private bool IsEffectiveDatePast(DateTime effectiveDate)
    {
        var earliestToday = TimeProvider.GetUtcNow().UtcDateTime.AddHours(-EarliestUtcOffsetHours).ToUtcDate();
        return effectiveDate.ToUtcDate() < earliestToday;
    }

    /// <summary>最西的时区（UTC-12）与 UTC 相差的小时数。</summary>
    private const int EarliestUtcOffsetHours = 12;

    /// <summary>
    /// 解密一个存储的账号（未存 = 空串）；解不开时记一条点名档案的警告（不含任何账号内容）并返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>与支票白纸打印的守卫（<c>CheckBatchComposer.ValidateBlankStockPrintable</c>）同一处理。</remarks>
    private string? TryDecrypt(string? encrypted, string associatedData, string profileKind, Guid profileId)
    {
        if (string.IsNullOrEmpty(encrypted))
            return string.Empty;
        try
        {
            return _protector.Unprotect(encrypted, associatedData);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "The stored account number on {ProfileKind} {ProfileId} cannot be decrypted; refusing to generate the EFT file.",
                profileKind, profileId);
            return null;
        }
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

    private async Task<EftBatchDto> ToDtoAsync(EftBatch batch, CancellationToken cancellationToken)
    {
        var dto = batch.MapTo<EftBatchDto>();
        await FillBankNamesAsync(new List<EftBatchDto> { dto }, cancellationToken);

        var lines = await _lineRepository.AsNoTracking().Where(l => l.EftBatchId == batch.Id).ToListAsync(cancellationToken);
        if (lines.Count > 0)
        {
            var paymentIds = lines.Select(l => l.PaymentEntryId).Distinct().ToList();
            var paymentNumbers = await _paymentRepository.AsNoTracking()
                .Where(p => paymentIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Number })
                .ToDictionaryAsync(p => p.Id, p => p.Number, cancellationToken);

            var partyBankIds = lines.Select(l => l.PartyBankAccountId).Distinct().ToList();
            var masks = await _partyBankRepository.AsNoTracking()
                .Where(a => partyBankIds.Contains(a.Id))
                .Select(a => new { a.Id, a.AccountNumberMasked })
                .ToDictionaryAsync(a => a.Id, a => a.AccountNumberMasked, cancellationToken);

            dto.Lines = lines.Select(l => new EftBatchLineDto
            {
                Id = l.Id,
                PaymentEntryId = l.PaymentEntryId,
                PaymentNumber = paymentNumbers.GetValueOrDefault(l.PaymentEntryId),
                PartyBankAccountId = l.PartyBankAccountId,
                PartyBankAccountMasked = masks.GetValueOrDefault(l.PartyBankAccountId),
                Amount = l.Amount,
                PayeeName = l.PayeeName
            }).ToList();
        }

        return dto;
    }

    private async Task FillBankNamesAsync(IList<EftBatchDto> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        var bankIds = items.Select(b => b.BankAccountId).Distinct().ToList();
        var names = await _bankAccountRepository.AsNoTracking()
            .Where(b => bankIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        foreach (var dto in items)
            dto.BankAccountName = names.GetValueOrDefault(dto.BankAccountId);
    }
}
