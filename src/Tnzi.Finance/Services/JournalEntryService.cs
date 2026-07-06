namespace Tnzi.Finance.Services;

/// <summary>
/// 会计凭证服务（草稿工作流 + 过账 + 冲销）
/// </summary>
public class JournalEntryService : ApplicationService, IJournalEntryService
{
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IRepository<JournalLine, Guid> _lineRepository;
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly LedgerPostingEngine _engine;
    private readonly FinanceOptions _options;

    public JournalEntryService(
        IServiceProvider serviceProvider,
        IRepository<JournalEntry, Guid> entryRepository,
        IRepository<JournalLine, Guid> lineRepository,
        IRepository<Account, Guid> accountRepository,
        LedgerPostingEngine engine,
        IOptions<FinanceOptions> options)
        : base(serviceProvider)
    {
        _entryRepository = Check.NotNull(entryRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _engine = Check.NotNull(engine);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<JournalEntryDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await _entryRepository.AsNoTracking()
            .Include(e => e.Lines.OrderBy(l => l.LineNumber))
            .ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (entry == null)
            return Fail<JournalEntryDto>("Journal entry not found.", 404);

        return Ok(entry.MapTo<JournalEntryDto>());
    }

    public async Task<Result<IPagedList<JournalEntryDto>>> GetListAsync(JournalEntryQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        // 仅头部投影（不联分录行）
        var pagedList = await _entryRepository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(e => e.PostingDate)
            .ThenByDescending(e => e.CreationTime)
            .Select(e => new JournalEntryDto
            {
                Id = e.Id,
                Number = e.Number,
                Status = e.Status,
                PostingDate = e.PostingDate,
                Memo = e.Memo,
                Currency = e.Currency,
                ExchangeRate = e.ExchangeRate,
                SourceType = e.SourceType,
                SourceId = e.SourceId,
                TotalDebit = e.TotalDebit,
                TotalCredit = e.TotalCredit,
                PostedTime = e.PostedTime,
                PostedById = e.PostedById,
                ReversalOfEntryId = e.ReversalOfEntryId,
                ReversedByEntryId = e.ReversedByEntryId,
                CreationTime = e.CreationTime
            })
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<JournalEntryDto>> CreateDraftAsync(CreateJournalEntryDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = await ValidateDraftInputAsync(input, cancellationToken);
        if (!validation.Succeeded)
            return Fail<JournalEntryDto>(validation.Message ?? "Invalid journal entry.", validation.Code ?? 400);

        var entry = BuildDraft(new JournalEntry(), input);

        await _entryRepository.InsertAsync(entry, cancellationToken);
        await _entryRepository.SaveChangesAsync(cancellationToken);

        return Ok(entry.MapTo<JournalEntryDto>());
    }

    public async Task<Result<JournalEntryDto>> UpdateDraftAsync(Guid id, CreateJournalEntryDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var entry = await _entryRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (entry == null)
            return Fail<JournalEntryDto>("Journal entry not found.", 404);
        if (entry.Status != JournalEntryStatus.Draft)
            return Fail<JournalEntryDto>("Only draft entries can be modified.", 409);

        var validation = await ValidateDraftInputAsync(input, cancellationToken);
        if (!validation.Succeeded)
            return Fail<JournalEntryDto>(validation.Message ?? "Invalid journal entry.", validation.Code ?? 400);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                // 整体替换分录行（草稿行无软删除，物理删除）
                var oldLines = entry.Lines.ToList();
                entry.Lines.Clear();
                await _lineRepository.DeleteManyAsync(oldLines, ct);

                BuildDraft(entry, input);
                await _entryRepository.UpdateAsync(entry, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<JournalEntryDto>("The journal entry was modified by another operation. Reload and retry.", 409);
        }

        return Ok(entry.MapTo<JournalEntryDto>());
    }

    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await _entryRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (entry == null)
            return Fail("Journal entry not found.", 404);
        if (entry.Status != JournalEntryStatus.Draft)
            return Fail("Only draft entries can be deleted. Posted entries must be reversed.", 409);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                await _lineRepository.DeleteManyAsync(entry.Lines.ToList(), ct);
                await _entryRepository.DeleteAsync(entry, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("The journal entry was modified by another operation. Reload and retry.", 409);
        }

        return Ok();
    }

    public async Task<Result<JournalEntryDto>> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await _entryRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (entry == null)
            return Fail<JournalEntryDto>("Journal entry not found.", 404);
        if (entry.Status != JournalEntryStatus.Draft)
            return Fail<JournalEntryDto>("Only draft entries can be posted.", 409);

        Result postResult;
        try
        {
            postResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var engineResult = await _engine.PostAsync(entry, ct);
                if (!engineResult.Succeeded)
                    return engineResult;

                await _entryRepository.UpdateAsync(entry, ct);
                return engineResult;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 乐观并发：另一操作（并发过账/冲销/编辑）已改变凭证，本事务已整体回滚（凭证号一并回收）
            return Fail<JournalEntryDto>("The journal entry was modified by another operation. Reload and retry.", 409);
        }

        if (!postResult.Succeeded)
            return Fail<JournalEntryDto>(postResult.Message ?? "Posting failed.", postResult.Code ?? 400);

        await PublishEventAsync(new JournalEntryPostedEvent
        {
            EntryId = entry.Id,
            Number = entry.Number!,
            PostingDate = entry.PostingDate,
            SourceType = entry.SourceType,
            SourceId = entry.SourceId,
            TotalDebit = entry.TotalDebit,
            TotalCredit = entry.TotalCredit,
            TenantId = entry.TenantId
        }, cancellationToken);

        return await GetAsync(entry.Id, cancellationToken);
    }

    public async Task<Result<JournalEntryDto>> ReverseAsync(Guid id, ReverseJournalEntryDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var original = await _entryRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (original == null)
            return Fail<JournalEntryDto>("Journal entry not found.", 404);
        if (original.Status == JournalEntryStatus.Draft)
            return Fail<JournalEntryDto>("Draft entries cannot be reversed. Delete the draft instead.", 409);
        if (original.Status == JournalEntryStatus.Reversed || original.ReversedByEntryId.HasValue)
            return Fail<JournalEntryDto>("The entry has already been reversed.", 409);

        var reversalDate = (input.PostingDate ?? original.PostingDate).ToUtcDate();
        JournalEntry? reversal = null;

        Result reverseResult;
        try
        {
            reverseResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var buildResult = await _engine.BuildReversalAsync(original, reversalDate, input.Memo, ct);
                if (!buildResult.Succeeded)
                    return Result.Failure(buildResult.Message ?? "Reversal failed.", buildResult.Code ?? 400);

                reversal = buildResult.Data!;
                await _entryRepository.InsertAsync(reversal, ct);

                original.Status = JournalEntryStatus.Reversed;
                original.ReversedByEntryId = reversal.Id;
                await _entryRepository.UpdateAsync(original, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 乐观并发：并发冲销/过账已改变原凭证，本事务整体回滚
            // （冲销凭证插入与已分配的凭证号一并撤销，不产生重复冲销）
            return Fail<JournalEntryDto>("The journal entry was modified by another operation. Reload and retry.", 409);
        }

        if (!reverseResult.Succeeded)
            return Fail<JournalEntryDto>(reverseResult.Message ?? "Reversal failed.", reverseResult.Code ?? 400);

        await PublishEventAsync(new JournalEntryReversedEvent
        {
            OriginalEntryId = original.Id,
            OriginalNumber = original.Number,
            ReversalEntryId = reversal!.Id,
            ReversalNumber = reversal.Number!,
            PostingDate = reversal.PostingDate,
            TenantId = original.TenantId
        }, cancellationToken);

        return await GetAsync(reversal.Id, cancellationToken);
    }

    /// <summary>
    /// 草稿输入校验 —— 刻意比过账宽松（允许单行、允许两侧为零的未完成行），
    /// 只拦截结构性错误；借贷平衡、行数下限、期间锁定等权威校验统一由
    /// <see cref="LedgerPostingEngine.PostAsync"/> 在过账时执行。
    /// </summary>
    private async Task<Result> ValidateDraftInputAsync(CreateJournalEntryDto input, CancellationToken cancellationToken)
    {
        if (input.Lines == null || input.Lines.Count == 0)
            return Fail("At least one line is required.");
        if (input.Lines.Count > _options.MaxLinesPerEntry)
            return Fail($"A journal entry cannot contain more than {_options.MaxLinesPerEntry} lines.");

        for (var i = 0; i < input.Lines.Count; i++)
        {
            var line = input.Lines[i];
            if (line.Debit < 0 || line.Credit < 0)
                return Fail($"Line {i + 1}: amounts cannot be negative.");
            if (line.Debit > 0 && line.Credit > 0)
                return Fail($"Line {i + 1}: a line cannot carry both debit and credit amounts.");
        }

        if (input.ExchangeRate is <= 0)
            return Fail("ExchangeRate must be greater than 0 when specified.");

        var accountIds = input.Lines.Select(l => l.AccountId).Distinct().ToList();
        var existingCount = await _accountRepository.CountAsync(a => accountIds.Contains(a.Id), cancellationToken);
        if (existingCount != accountIds.Count)
            return Fail("One or more accounts do not exist.");

        return Ok();
    }

    private JournalEntry BuildDraft(JournalEntry entry, CreateJournalEntryDto input)
    {
        entry.Status = JournalEntryStatus.Draft;
        entry.PostingDate = input.PostingDate.ToUtcDate();
        entry.Memo = input.Memo;
        entry.Currency = input.Currency?.Trim().ToUpperInvariant() ?? _options.BaseCurrency.Trim().ToUpperInvariant();
        entry.ExchangeRate = input.ExchangeRate ?? 0m;
        entry.SourceType ??= "Manual";

        var lineNumber = 1;
        foreach (var line in input.Lines)
        {
            entry.Lines.Add(new JournalLine
            {
                LineNumber = lineNumber++,
                AccountId = line.AccountId,
                TxnDebit = line.Debit,
                TxnCredit = line.Credit,
                Currency = entry.Currency,
                Memo = line.Memo,
                PartyType = line.PartyType,
                PartyId = line.PartyId,
                Dimensions = line.Dimensions,
                IsPosted = false,
                PostingDate = entry.PostingDate
            });
        }

        return entry;
    }
}
