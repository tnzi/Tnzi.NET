namespace Tnzi.Finance.Services;

/// <summary>
/// 银行存款单服务
/// </summary>
/// <remarks>
/// <para>
/// 过账规则：借 目标银行科目<b>一行</b>总额；贷 来源科目（待存款项）每张收款一行；
/// 贷 各「其它款项」行自己指定的科目。借方之所以坚持一行，是因为整张单据存在的理由
/// 就是让银行流水上那一行 1 : 1 配得上；贷方之所以逐行展开，是因为
/// 「这笔存款由哪几张收款组成」正是待存款项科目要携带的信息。
/// </para>
/// <para>
/// 独占声明（一张收款至多进一张存活的存款单）由 <c>DepositLine.ClaimedPaymentEntryId</c>
/// 的唯一索引保证，<b>不是</b>先查再写保证的。期间封账、已完成对账、已匹配流水三道守卫
/// 分别由 <see cref="Internal.LedgerPostingEngine"/> 与 <see cref="Internal.ReversalGuard"/>
/// 统一承担，本服务不另写一份。
/// </para>
/// </remarks>
public class DepositService : ApplicationService, IDepositService
{
    private readonly IRepository<Deposit, Guid> _depositRepository;
    private readonly IRepository<DepositLine, Guid> _lineRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IReadOnlyRepository<PaymentEntry, Guid> _paymentRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly IReadOnlyRepository<Customer, Guid> _customerRepository;
    private readonly IDocumentNumberService _numberService;
    private readonly LedgerPostingEngine _engine;
    private readonly FinanceDocumentHelper _helper;
    private readonly PostingGuardRunner _guards;
    private readonly FinanceOptions _options;
    private readonly DepositDisplayNames _displayNames;

    public DepositService(
        IServiceProvider serviceProvider,
        IRepository<Deposit, Guid> depositRepository,
        IRepository<DepositLine, Guid> lineRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IReadOnlyRepository<PaymentEntry, Guid> paymentRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        IReadOnlyRepository<Customer, Guid> customerRepository,
        IDocumentNumberService numberService,
        LedgerPostingEngine engine,
        FinanceDocumentHelper helper,
        PostingGuardRunner guards,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _depositRepository = Check.NotNull(depositRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _entryRepository = Check.NotNull(entryRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _customerRepository = Check.NotNull(customerRepository);
        _numberService = Check.NotNull(numberService);
        _engine = Check.NotNull(engine);
        _helper = Check.NotNull(helper);
        _guards = Check.NotNull(guards);
        _options = Check.NotNull(options).Value;
        _displayNames = new DepositDisplayNames(_paymentRepository, _accountRepository, _customerRepository);
    }

    public async Task<Result<IPagedList<DepositDto>>> GetPagedAsync(DepositQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        // 逐字段投影而非 ProjectTo：Deposit 有 Lines 导航、DepositDto 有同名属性，
        // ProjectTo 会把整页每张单据的全部行一起拉下来 —— 列表一行都不显示它们。
        // 同族单据（Invoice/Bill/CreditMemo/Expense）手写 Select 正是为了这个。
        var pagedList = await _depositRepository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(d => d.CreationTime)
            .Select(d => new DepositDto
            {
                Id = d.Id,
                Number = d.Number,
                Status = d.Status,
                FromAccountId = d.FromAccountId,
                ToAccountId = d.ToAccountId,
                DepositDate = d.DepositDate,
                Currency = d.Currency,
                ExchangeRate = d.ExchangeRate,
                Amount = d.Amount,
                BaseAmount = d.BaseAmount,
                Reference = d.Reference,
                Memo = d.Memo,
                JournalEntryId = d.JournalEntryId,
                VoidJournalEntryId = d.VoidJournalEntryId,
                ConcurrencyStamp = d.ConcurrencyStamp,
                CreationTime = d.CreationTime
            })
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        await _displayNames.FillAccountNamesAsync(pagedList.Items, cancellationToken);
        return Ok(pagedList);
    }

    public async Task<Result<DepositDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deposit = await _depositRepository.AsNoTracking()
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (deposit == null)
            return Fail<DepositDto>("Deposit not found.", 404);

        var dto = deposit.MapTo<DepositDto>();
        dto.Lines = deposit.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l => new DepositLineDto
            {
                Id = l.Id,
                LineNumber = l.LineNumber,
                PaymentEntryId = l.PaymentEntryId,
                AccountId = l.AccountId,
                Amount = l.Amount,
                Description = l.Description,
                Reference = l.Reference,
                IsClaimActive = l.ClaimedPaymentEntryId.HasValue
            })
            .ToList();

        await _displayNames.FillAccountNamesAsync(new List<DepositDto> { dto }, cancellationToken);
        await _displayNames.FillLineLabelsAsync(dto.Lines, cancellationToken);
        return Ok(dto);
    }

    /// <summary>
    /// 候选清单 = 该科目上已过账的 Inbound 收款 <b>减去</b> 已被存活存款单声明的那些。
    /// 「未被声明」用子查询下推数据库（NOT IN），不把两边都拉进内存做差集。
    /// </summary>
    public async Task<Result<List<UndepositedReceiptDto>>> GetUndepositedReceiptsAsync(UndepositedReceiptQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        if (query.AccountId == Guid.Empty)
            return Fail<List<UndepositedReceiptDto>>("An account is required.");

        var accountResult = await _helper.GetPostableAccountAsync(query.AccountId, cancellationToken);
        if (!accountResult.Succeeded)
            return Fail<List<UndepositedReceiptDto>>(accountResult.Message!, accountResult.Code ?? 400);

        var claimed = _lineRepository.AsQueryable()
            .Where(l => l.ClaimedPaymentEntryId != null)
            .Select(l => l.ClaimedPaymentEntryId!.Value);

        var queryable = _paymentRepository.AsNoTracking()
            .Where(p => p.Status == FinanceDocumentStatus.Posted
                && p.Direction == PaymentDirection.Inbound
                && p.DepositToAccountId == query.AccountId
                && !claimed.Contains(p.Id));

        if (!string.IsNullOrWhiteSpace(query.Currency))
        {
            var currency = query.Currency.Trim().ToUpperInvariant();
            queryable = queryable.Where(p => p.Currency == currency);
        }

        if (query.From.HasValue)
        {
            var from = query.From.Value.ToUtcDate();
            queryable = queryable.Where(p => p.DocDate >= from);
        }

        if (query.To.HasValue)
        {
            var toExclusive = query.To.Value.ToUtcDate().AddDays(1);
            queryable = queryable.Where(p => p.DocDate < toExclusive);
        }

        var payments = await queryable
            .OrderBy(p => p.DocDate).ThenBy(p => p.Number)
            .ToListAsync(cancellationToken);

        var items = payments.Select(p => new UndepositedReceiptDto
        {
            PaymentEntryId = p.Id,
            PaymentNumber = p.Number,
            PartyType = p.PartyType,
            PartyId = p.PartyId,
            DocDate = p.DocDate,
            Currency = p.Currency,
            Amount = p.Amount,
            PaymentMethod = p.PaymentMethod,
            Reference = p.Reference
        }).ToList();

        var names = await _displayNames.LoadCustomerNamesAsync(
            items.Where(i => i.PartyType == FinancePartyType.Customer).Select(i => i.PartyId), cancellationToken);
        foreach (var item in items)
            item.PartyName = names.GetValueOrDefault(item.PartyId);

        return Ok(items);
    }

    public async Task<Result<DepositDto>> CreateDraftAsync(CreateDepositDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var draftResult = await BuildDraftAsync(input, cancellationToken);
        if (!draftResult.Succeeded)
            return Fail<DepositDto>(draftResult.Message ?? "Invalid deposit.", draftResult.Code ?? 400);

        var deposit = new Deposit();
        Apply(deposit, draftResult.Data!);

        try
        {
            await _depositRepository.InsertAsync(deposit, cancellationToken);
            await _depositRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 唯一索引是这条规则的唯一守卫：并发下两个人可以同时读到「这张支票还没人收」。
            return ClaimConflict<DepositDto>();
        }

        return await GetAsync(deposit.Id, cancellationToken);
    }

    public async Task<Result<DepositDto>> UpdateDraftAsync(Guid id, CreateDepositDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var deposit = await _depositRepository.AsQueryable(true)
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (deposit == null)
            return Fail<DepositDto>("Deposit not found.", 404);
        if (deposit.Status != FinanceDocumentStatus.Draft)
            return Fail<DepositDto>("Only draft deposits can be edited.", 409);

        // 全部校验先在本地缓冲跑完，通过之后才碰实体：中途失败若已改过被跟踪的实体，
        // 同一 scope 里后续任何一次 SaveChanges 都会把半成品写进库（引擎的同款纪律）。
        var draftResult = await BuildDraftAsync(input, cancellationToken, excludeDepositId: deposit.Id);
        if (!draftResult.Succeeded)
            return Fail<DepositDto>(draftResult.Message ?? "Invalid deposit.", draftResult.Code ?? 400);

        var oldLines = deposit.Lines.ToList();

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                // 旧行必须**先落库删除**再插新行：同一次 SaveChanges 里删旧插新，
                // 若某张收款被移出又移回，唯一索引会在语句层面瞬时冲突。
                if (oldLines.Count > 0)
                {
                    deposit.Lines.Clear();
                    await _lineRepository.DeleteManyAsync(oldLines, ct);
                    await _lineRepository.SaveChangesAsync(ct);
                }

                Apply(deposit, draftResult.Data!);
                await _depositRepository.UpdateAsync(deposit, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<DepositDto>("The deposit was modified by another operation. Reload and retry.", 409);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return ClaimConflict<DepositDto>();
        }

        return await GetAsync(deposit.Id, cancellationToken);
    }

    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deposit = await _depositRepository.AsQueryable(true)
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (deposit == null)
            return Fail("Deposit not found.", 404);
        if (deposit.Status != FinanceDocumentStatus.Draft)
            return Fail("Only draft deposits can be deleted. Posted deposits must be voided.", 409);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                // 行硬删 = 释放它持有的收款（草稿从未过账，没有任何证据链需要保留）
                if (deposit.Lines.Count > 0)
                    await _lineRepository.DeleteManyAsync(deposit.Lines.ToList(), ct);
                await _depositRepository.DeleteAsync(deposit, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("The deposit was modified by another operation. Reload and retry.", 409);
        }

        return Ok();
    }

    public async Task<Result<DepositDto>> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deposit = await _depositRepository.AsQueryable(true)
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (deposit == null)
            return Fail<DepositDto>("Deposit not found.", 404);
        if (deposit.Status != FinanceDocumentStatus.Draft)
            return Fail<DepositDto>("Only draft deposits can be posted.", 409);
        if (deposit.Lines.Count == 0)
            return Fail<DepositDto>("The deposit has no lines.", 400);
        if (deposit.Amount <= 0)
            return Fail<DepositDto>("The deposit total must be greater than zero.", 400);

        var guardResult = await _guards.CheckAsync(FinanceSourceTypes.Deposit, deposit.Id.ToString(), FinancePostingOperation.Post, deposit, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<DepositDto>(guardResult.Message ?? "Posting was rejected.", guardResult.Code ?? 403);

        // 草稿期间科目可能被停用/改币种/取消资金分类，收款可能已被作废 —— 过账时全部重校验
        var accountsResult = await ValidateAccountsAsync(deposit.FromAccountId, deposit.ToAccountId, deposit.Currency, cancellationToken);
        if (!accountsResult.Succeeded)
            return Fail<DepositDto>(accountsResult.Message ?? "Invalid accounts.", accountsResult.Code ?? 400);

        var lines = deposit.Lines.OrderBy(l => l.LineNumber).ToList();
        var receiptIds = lines.Where(l => l.PaymentEntryId.HasValue).Select(l => l.PaymentEntryId!.Value).ToList();
        var payments = receiptIds.Count == 0
            ? new List<PaymentEntry>()
            : await _paymentRepository.AsNoTracking().Where(p => receiptIds.Contains(p.Id)).ToListAsync(cancellationToken);
        var paymentById = payments.ToDictionary(p => p.Id);

        foreach (var line in lines)
        {
            if (line.PaymentEntryId.HasValue)
            {
                if (!paymentById.TryGetValue(line.PaymentEntryId.Value, out var payment))
                    return Fail<DepositDto>($"Line {line.LineNumber}: the receipt no longer exists.", 409);

                var receiptResult = ValidateReceipt(payment, deposit.FromAccountId, deposit.Currency, line.LineNumber);
                if (!receiptResult.Succeeded)
                    return Fail<DepositDto>(receiptResult.Message!, receiptResult.Code ?? 400);
                if (payment.Amount != line.Amount)
                    return Fail<DepositDto>($"Line {line.LineNumber}: the receipt amount changed. Reload the deposit and retry.", 409);
            }
            else
            {
                var fundsResult = await ValidateOtherFundsAccountAsync(line.AccountId, deposit, line.LineNumber, cancellationToken);
                if (!fundsResult.Succeeded)
                    return Fail<DepositDto>(fundsResult.Message!, fundsResult.Code ?? 400);
            }
        }

        var entry = new JournalEntry
        {
            Status = JournalEntryStatus.Draft,
            PostingDate = deposit.DepositDate,
            Memo = string.IsNullOrWhiteSpace(deposit.Memo) ? "Bank deposit" : deposit.Memo,
            Currency = deposit.Currency,
            ExchangeRate = deposit.ExchangeRate,
            SourceType = FinanceSourceTypes.Deposit,
            SourceId = deposit.Id.ToString()
        };

        // 借方一行总额：银行流水那一行要 1 : 1 配得上，拆开就再也配不上了
        var lineNumber = 1;
        entry.Lines.Add(new JournalLine
        {
            LineNumber = lineNumber++,
            AccountId = deposit.ToAccountId,
            TxnDebit = deposit.Amount,
            Currency = deposit.Currency
        });

        foreach (var line in lines)
        {
            entry.Lines.Add(new JournalLine
            {
                LineNumber = lineNumber++,
                AccountId = line.PaymentEntryId.HasValue ? deposit.FromAccountId : line.AccountId!.Value,
                TxnCredit = line.Amount,
                Currency = deposit.Currency,
                Memo = line.PaymentEntryId.HasValue
                    ? paymentById[line.PaymentEntryId.Value].Number ?? line.Description
                    : line.Description
            });
        }

        Result postResult;
        try
        {
            postResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var engineResult = await _engine.PostAsync(entry, ct);
                if (!engineResult.Succeeded)
                    return engineResult;

                await _entryRepository.InsertAsync(entry, ct);

                deposit.Number = await _numberService.NextFormattedAsync(
                    FinanceSourceTypes.Deposit, _options.DepositNumberPrefix, _options.JournalNumberPadding, ct);
                deposit.Status = FinanceDocumentStatus.Posted;
                deposit.ExchangeRate = entry.ExchangeRate;
                deposit.BaseAmount = entry.Lines.First(l => l.AccountId == deposit.ToAccountId).Debit;
                deposit.JournalEntryId = entry.Id;
                await _depositRepository.UpdateAsync(deposit, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<DepositDto>("The deposit was modified by another operation. Reload and retry.", 409);
        }

        if (!postResult.Succeeded)
            return Fail<DepositDto>(postResult.Message ?? "Posting failed.", postResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentPostedEvent
        {
            DocType = FinanceSourceTypes.Deposit,
            DocId = deposit.Id,
            Number = deposit.Number!,
            JournalEntryId = entry.Id,
            DocDate = deposit.DepositDate,
            Total = deposit.Amount,
            TenantId = deposit.TenantId
        }, cancellationToken);

        return await GetAsync(deposit.Id, cancellationToken);
    }

    public async Task<Result<DepositDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deposit = await _depositRepository.AsQueryable(true)
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (deposit == null)
            return Fail<DepositDto>("Deposit not found.", 404);
        if (deposit.Status != FinanceDocumentStatus.Posted)
            return Fail<DepositDto>("Only posted deposits can be voided.", 409);

        var guardResult = await _guards.CheckAsync(FinanceSourceTypes.Deposit, deposit.Id.ToString(), FinancePostingOperation.Void, deposit, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<DepositDto>(guardResult.Message ?? "Void was rejected.", guardResult.Code ?? 403);

        var original = await _entryRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == deposit.JournalEntryId, cancellationToken);
        if (original == null)
            return Fail<DepositDto>("The posting journal entry was not found.", 500);

        JournalEntry? reversal = null;
        Result voidResult;
        try
        {
            voidResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var buildResult = await _engine.BuildReversalAsync(original, original.PostingDate, $"Void {deposit.Number}", ct);
                if (!buildResult.Succeeded)
                    return Result.Failure(buildResult.Message ?? "Void failed.", buildResult.Code ?? 400);

                reversal = buildResult.Data!;
                await _entryRepository.InsertAsync(reversal, ct);

                original.Status = JournalEntryStatus.Reversed;
                original.ReversedByEntryId = reversal.Id;
                await _entryRepository.UpdateAsync(original, ct);

                // 释放收款：只清声明，行留着 —— 作废的存款单仍要答得出当初装的是哪几笔
                foreach (var line in deposit.Lines.Where(l => l.ClaimedPaymentEntryId.HasValue))
                {
                    line.ClaimedPaymentEntryId = null;
                    await _lineRepository.UpdateAsync(line, ct);
                }

                deposit.Status = FinanceDocumentStatus.Voided;
                deposit.VoidJournalEntryId = reversal.Id;
                await _depositRepository.UpdateAsync(deposit, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<DepositDto>("The deposit was modified by another operation. Reload and retry.", 409);
        }

        if (!voidResult.Succeeded)
            return Fail<DepositDto>(voidResult.Message ?? "Void failed.", voidResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentVoidedEvent
        {
            DocType = FinanceSourceTypes.Deposit,
            DocId = deposit.Id,
            Number = deposit.Number,
            VoidJournalEntryId = reversal!.Id,
            TenantId = deposit.TenantId
        }, cancellationToken);

        return await GetAsync(deposit.Id, cancellationToken);
    }

    /// <summary>草稿缓冲：全部校验通过后才交给 <see cref="Apply"/> 写进实体</summary>
    private sealed class DepositDraft
    {
        public required Guid FromAccountId { get; init; }
        public required Guid ToAccountId { get; init; }
        public required DateTime DepositDate { get; init; }
        public required string Currency { get; init; }
        public required decimal ExchangeRate { get; init; }
        public required decimal Amount { get; init; }
        public string? Reference { get; init; }
        public string? Memo { get; init; }
        public required List<DepositLine> Lines { get; init; }
    }

    /// <summary>
    /// 校验请求并构建（尚未附着到任何实体的）单据行。
    /// </summary>
    /// <param name="input">请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="excludeDepositId">
    /// 更新草稿时排除自己已持有的声明，否则「原样保存」会被自己占着的那几张收款挡下来
    /// </param>
    private async Task<Result<DepositDraft>> BuildDraftAsync(CreateDepositDto input, CancellationToken cancellationToken, Guid? excludeDepositId = null)
    {
        var paymentIds = input.PaymentEntryIds ?? new List<Guid>();
        var otherFunds = input.OtherFunds ?? new List<CreateDepositFundsLineDto>();

        if (paymentIds.Count == 0 && otherFunds.Count == 0)
            return Result<DepositDraft>.Failure("A deposit requires at least one receipt or one other-funds line.", 400);
        if (paymentIds.Distinct().Count() != paymentIds.Count)
            return Result<DepositDraft>.Failure("The same receipt cannot appear twice on one deposit.", 400);

        // 借方那一行也占一个额度，所以单据行上限比凭证行上限少一
        var maxLines = _options.MaxLinesPerEntry - 1;
        if (paymentIds.Count + otherFunds.Count > maxLines)
            return Result<DepositDraft>.Failure($"Too many lines (max {maxLines}). Split the deposit.", 400);

        var currency = _helper.NormalizeCurrency(input.Currency);
        var accountsResult = await ValidateAccountsAsync(input.FromAccountId, input.ToAccountId, currency, cancellationToken);
        if (!accountsResult.Succeeded)
            return Result<DepositDraft>.Failure(accountsResult.Message!, accountsResult.Code ?? 400);

        // 「其它款项」行的科目校验只看单据两侧科目，用一个轻量载体传，避免提前改动实体
        var header = new Deposit
        {
            FromAccountId = input.FromAccountId,
            ToAccountId = input.ToAccountId,
            Currency = currency
        };

        var lines = new List<DepositLine>();
        var lineNumber = 1;

        if (paymentIds.Count > 0)
        {
            var payments = await _paymentRepository.AsNoTracking()
                .Where(p => paymentIds.Contains(p.Id))
                .ToListAsync(cancellationToken);
            var paymentById = payments.ToDictionary(p => p.Id);

            // 已被别的存活存款单声明的收款：在这里明确拒绝，而不是把 409 留给唯一索引 ——
            // 索引兜的是并发，这里答的是「你选的这几张里有一张已经被人拿走了」
            var claimedIds = await _lineRepository.AsNoTracking()
                .Where(l => l.ClaimedPaymentEntryId != null
                    && paymentIds.Contains(l.ClaimedPaymentEntryId!.Value)
                    && (excludeDepositId == null || l.DepositId != excludeDepositId.Value))
                .Select(l => l.ClaimedPaymentEntryId!.Value)
                .ToListAsync(cancellationToken);

            foreach (var paymentId in paymentIds)
            {
                if (!paymentById.TryGetValue(paymentId, out var payment))
                    return Result<DepositDraft>.Failure($"Receipt '{paymentId}' not found.", 404);
                if (claimedIds.Contains(paymentId))
                    return Result<DepositDraft>.Failure($"Receipt '{payment.Number ?? paymentId.ToString()}' is already on another deposit.", 409);

                var receiptResult = ValidateReceipt(payment, input.FromAccountId, currency, lineNumber);
                if (!receiptResult.Succeeded)
                    return Result<DepositDraft>.Failure(receiptResult.Message!, receiptResult.Code ?? 400);

                lines.Add(new DepositLine
                {
                    LineNumber = lineNumber++,
                    PaymentEntryId = paymentId,
                    ClaimedPaymentEntryId = paymentId,
                    // 金额取自收款单本身，不取调用方传来的数：让两者可以不一致，
                    // 等于允许一张「声明了这张支票、金额却是别的数」的存款单存在
                    Amount = payment.Amount,
                    Description = payment.Memo,
                    Reference = payment.Reference
                });
            }
        }

        foreach (var funds in otherFunds)
        {
            if (funds.Amount <= 0)
                return Result<DepositDraft>.Failure($"Line {lineNumber}: amount must be greater than zero.", 400);

            var accountResult = await ValidateOtherFundsAccountAsync(funds.AccountId, header, lineNumber, cancellationToken);
            if (!accountResult.Succeeded)
                return Result<DepositDraft>.Failure(accountResult.Message!, accountResult.Code ?? 400);

            lines.Add(new DepositLine
            {
                LineNumber = lineNumber++,
                AccountId = funds.AccountId,
                Amount = _helper.Round(funds.Amount),
                Description = funds.Description,
                Reference = funds.Reference
            });
        }

        var total = _helper.Round(lines.Sum(l => l.Amount));
        if (total <= 0)
            return Result<DepositDraft>.Failure("The deposit total must be greater than zero.", 400);

        return Result<DepositDraft>.Success(new DepositDraft
        {
            FromAccountId = input.FromAccountId,
            ToAccountId = input.ToAccountId,
            DepositDate = input.DepositDate.ToUtcDate(),
            Currency = currency,
            ExchangeRate = input.ExchangeRate ?? 0m,
            Amount = total,
            Reference = input.Reference,
            Memo = input.Memo,
            Lines = lines
        });
    }

    /// <summary>把校验通过的缓冲写进实体（此后不再有失败路径）</summary>
    private static void Apply(Deposit deposit, DepositDraft draft)
    {
        deposit.FromAccountId = draft.FromAccountId;
        deposit.ToAccountId = draft.ToAccountId;
        deposit.DepositDate = draft.DepositDate;
        deposit.Currency = draft.Currency;
        deposit.ExchangeRate = draft.ExchangeRate;
        deposit.Amount = draft.Amount;
        deposit.Reference = draft.Reference;
        deposit.Memo = draft.Memo;

        deposit.Lines.Clear();
        foreach (var line in draft.Lines)
        {
            line.DepositId = deposit.Id;
            deposit.Lines.Add(line);
        }
    }

    /// <summary>
    /// 双方须为不同的可过账资金叶子科目（判据统一收口在 <see cref="FinanceDocumentHelper.GetFundsAccountAsync"/>），
    /// 且各自与交易币种兼容
    /// </summary>
    private async Task<Result> ValidateAccountsAsync(Guid fromAccountId, Guid toAccountId, string currency, CancellationToken cancellationToken)
    {
        if (fromAccountId == toAccountId)
            return Fail("The source and destination accounts must be different.");

        var fromResult = await _helper.GetFundsAccountAsync(fromAccountId, currency, cancellationToken);
        if (!fromResult.Succeeded)
            return Fail($"Source account: {fromResult.Message}", fromResult.Code ?? 400);

        var toResult = await _helper.GetFundsAccountAsync(toAccountId, currency, cancellationToken);
        if (!toResult.Succeeded)
            return Fail($"Destination account: {toResult.Message}", toResult.Code ?? 400);

        return Ok();
    }

    /// <summary>收款行的准入：已过账的 Inbound 收款、就落在本单来源科目上、币种一致</summary>
    private Result ValidateReceipt(PaymentEntry payment, Guid fromAccountId, string currency, int lineNumber)
    {
        var label = payment.Number ?? payment.Id.ToString();

        if (payment.Direction != PaymentDirection.Inbound)
            return Fail($"Line {lineNumber}: '{label}' is not an inbound receipt.");
        if (payment.Status != FinanceDocumentStatus.Posted)
            return Fail($"Line {lineNumber}: receipt '{label}' is not posted.");
        if (payment.DepositToAccountId != fromAccountId)
            return Fail($"Line {lineNumber}: receipt '{label}' is not sitting on the deposit's source account.");
        if (!string.Equals(payment.Currency, currency, StringComparison.OrdinalIgnoreCase))
            return Fail($"Line {lineNumber}: receipt '{label}' is in {payment.Currency}, but the deposit is in {currency}.");

        return Ok();
    }

    /// <summary>
    /// 「其它款项」行的贷记科目准入。
    /// </summary>
    /// <remarks>
    /// <b>为什么拒绝 A/R、A/P 控制科目</b>：从存款单直接贷记应收，发票依旧挂着未清、
    /// 同时账上多出一笔无处可核销的贷项 —— 两个错误一次犯完，而且账面总额是平的，
    /// 于是没有任何报表会显出异常。收发票的钱只有一条合法路径：开一张 <see cref="PaymentEntry"/>
    /// 再核销。<see cref="PaymentEntryService"/> 那侧有一条同源的守卫
    /// （存入科目不得是 A/R、A/P 控制科目），两条合起来才把这条路真正封死。
    /// <para>
    /// 同时拒绝本单自己的来源科目与目标科目：贷记目标科目等于把自己的借方抵掉一部分；
    /// 贷记来源科目则是一笔不带收款单的待存款项，正是这张单据要取代的那种糊涂账
    /// （真要平掉一笔来路不明的待存款项余额，用 <see cref="Transfer"/> 或手工凭证，
    /// 那是一个有主体、说得清的动作）。
    /// </para>
    /// </remarks>
    private async Task<Result> ValidateOtherFundsAccountAsync(Guid? accountId, Deposit deposit, int lineNumber, CancellationToken cancellationToken)
    {
        if (!accountId.HasValue || accountId.Value == Guid.Empty)
            return Fail($"Line {lineNumber}: an other-funds line requires a credit account.");

        var accountResult = await _helper.GetPostableAccountAsync(accountId.Value, cancellationToken);
        if (!accountResult.Succeeded)
            return Fail($"Line {lineNumber}: {accountResult.Message}", accountResult.Code ?? 400);

        var account = accountResult.Data!;
        if (account.SystemRole is AccountSystemRole.AccountsReceivable or AccountSystemRole.AccountsPayable)
        {
            return Fail(
                $"Line {lineNumber}: '{account.Code}' is the {account.SystemRole} control account and cannot be credited from a deposit. "
                + "Record the money as a payment entry and apply it to the invoice or bill instead.",
                400);
        }

        if (accountId.Value == deposit.ToAccountId)
            return Fail($"Line {lineNumber}: '{account.Code}' is the deposit's destination account.");
        if (accountId.Value == deposit.FromAccountId)
        {
            return Fail(
                $"Line {lineNumber}: '{account.Code}' is the deposit's source account. "
                + "Add the receipts that make up this money instead, or move an unidentified balance with a transfer.",
                400);
        }

        return Ok();
    }

    private static Result<T> ClaimConflict<T>()
        => Result<T>.Failure("One or more receipts were just taken by another deposit. Reload and retry.", 409);

}
