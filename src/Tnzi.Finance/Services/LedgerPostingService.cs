namespace Tnzi.Finance.Services;

/// <summary>
/// 总账过账服务（编程式过账入口）
/// </summary>
public class LedgerPostingService : ApplicationService, ILedgerPostingService
{
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly IJournalEntryService _journalEntryService;
    private readonly LedgerPostingEngine _engine;
    private readonly PostingGuardRunner _guards;
    private readonly FinanceOptions _options;

    public LedgerPostingService(
        IServiceProvider serviceProvider,
        IRepository<JournalEntry, Guid> entryRepository,
        IRepository<Account, Guid> accountRepository,
        IJournalEntryService journalEntryService,
        LedgerPostingEngine engine,
        PostingGuardRunner guards,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _entryRepository = Check.NotNull(entryRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _journalEntryService = Check.NotNull(journalEntryService);
        _engine = Check.NotNull(engine);
        _guards = Check.NotNull(guards);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<JournalEntryDto>> PostAsync(LedgerPostingRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (string.IsNullOrWhiteSpace(request.SourceType) || string.IsNullOrWhiteSpace(request.SourceId))
            return Fail<JournalEntryDto>("SourceType and SourceId are required.");
        if (request.Lines == null || request.Lines.Count < 2)
            return Fail<JournalEntryDto>("At least two posting lines are required.");

        var guardResult = await _guards.CheckAsync(request.SourceType.Trim(), request.SourceId.Trim(), FinancePostingOperation.Post, request, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<JournalEntryDto>(guardResult.Message ?? "Posting was rejected.", guardResult.Code ?? 403);

        var entry = new JournalEntry
        {
            Status = JournalEntryStatus.Draft,
            PostingDate = request.PostingDate.ToUtcDate(),
            Memo = request.Memo,
            Currency = request.Currency?.Trim().ToUpperInvariant() ?? _options.BaseCurrency.Trim().ToUpperInvariant(),
            ExchangeRate = request.ExchangeRate ?? 0m,
            SourceType = request.SourceType.Trim(),
            SourceId = request.SourceId.Trim()
        };

        // 批量解析科目（AccountId → AccountCode → AccountRole；三次 IN 查询，避免逐行 N+1）
        var resolveResult = await ResolveAccountsAsync(request.Lines, cancellationToken);
        if (!resolveResult.Succeeded)
            return Fail<JournalEntryDto>(resolveResult.Message ?? "Unable to resolve accounts.", resolveResult.Code ?? 400);

        var accountIds = resolveResult.Data!;
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            entry.Lines.Add(new JournalLine
            {
                LineNumber = i + 1,
                AccountId = accountIds[i],
                TxnDebit = line.Debit,
                TxnCredit = line.Credit,
                Currency = entry.Currency,
                Memo = line.Memo,
                PartyType = line.PartyType,
                PartyId = line.PartyId,
                Dimensions = line.Dimensions,
                TaxRateId = line.TaxRateId
            });
        }

        var postResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
        {
            var engineResult = await _engine.PostAsync(entry, ct);
            if (!engineResult.Succeeded)
                return engineResult;

            await _entryRepository.InsertAsync(entry, ct);
            return engineResult;
        }, cancellationToken);

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

        return Ok(entry.MapTo<JournalEntryDto>());
    }

    public async Task<Result<JournalEntryDto>> ReverseAsync(Guid journalEntryId, ReverseJournalEntryDto? input = null, CancellationToken cancellationToken = default)
    {
        // 委托凭证服务：期间锁定、并发（409）、事件与钩子（Reverse on JournalEntry）语义完全一致
        return await _journalEntryService.ReverseAsync(journalEntryId, input ?? new ReverseJournalEntryDto(), cancellationToken);
    }

    public async Task<Result<List<JournalEntryDto>>> GetBySourceAsync(string sourceType, string sourceId, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(sourceType);
        Check.NotNullOrWhiteSpace(sourceId);

        var entries = await _entryRepository.AsNoTracking()
            .Include(e => e.Lines)
            .Where(e => e.SourceType == sourceType && e.SourceId == sourceId)
            .OrderBy(e => e.CreationTime)
            .ToListAsync(cancellationToken);

        return Ok(entries.MapToList<JournalEntryDto>());
    }

    /// <summary>
    /// 批量解析每行的科目：按 AccountId → AccountCode → AccountRole 优先级，
    /// 各维度合并为一次 IN 查询后在内存中逐行匹配
    /// </summary>
    private async Task<Result<Guid[]>> ResolveAccountsAsync(List<LedgerPostingLine> lines, CancellationToken cancellationToken)
    {
        var ids = lines.Where(l => l.AccountId.HasValue)
            .Select(l => l.AccountId!.Value).Distinct().ToList();
        var codes = lines.Where(l => !l.AccountId.HasValue && !string.IsNullOrWhiteSpace(l.AccountCode))
            .Select(l => l.AccountCode!.Trim()).Distinct().ToList();
        var roles = lines.Where(l => !l.AccountId.HasValue && string.IsNullOrWhiteSpace(l.AccountCode) && l.AccountRole.HasValue)
            .Select(l => l.AccountRole!.Value).Distinct().ToList();

        var idSet = ids.Count > 0
            ? (await _accountRepository.AsNoTracking()
                .Where(a => ids.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken)).ToHashSet()
            : new HashSet<Guid>();

        var byCode = codes.Count > 0
            ? await _accountRepository.AsNoTracking()
                .Where(a => codes.Contains(a.Code))
                .Select(a => new { a.Code, a.Id })
                .ToDictionaryAsync(x => x.Code, x => x.Id, cancellationToken)
            : new Dictionary<string, Guid>();

        var byRole = roles.Count > 0
            ? await _accountRepository.AsNoTracking()
                .Where(a => a.SystemRole != null && roles.Contains(a.SystemRole.Value))
                .Select(a => new { a.SystemRole, a.Id })
                .ToDictionaryAsync(x => x.SystemRole!.Value, x => x.Id, cancellationToken)
            : new Dictionary<AccountSystemRole, Guid>();

        var resolved = new Guid[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.AccountId.HasValue && idSet.Contains(line.AccountId.Value))
            {
                resolved[i] = line.AccountId.Value;
            }
            else if (!line.AccountId.HasValue && !string.IsNullOrWhiteSpace(line.AccountCode) &&
                     byCode.TryGetValue(line.AccountCode.Trim(), out var idByCode))
            {
                resolved[i] = idByCode;
            }
            else if (!line.AccountId.HasValue && string.IsNullOrWhiteSpace(line.AccountCode) &&
                     line.AccountRole.HasValue && byRole.TryGetValue(line.AccountRole.Value, out var idByRole))
            {
                resolved[i] = idByRole;
            }
            else
            {
                return Fail<Guid[]>($"Line {i + 1}: unable to resolve the account (specify AccountId, AccountCode or AccountRole).");
            }
        }

        return Ok(resolved);
    }
}
