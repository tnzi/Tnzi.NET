
namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 银行流水导入与匹配服务
/// </summary>
/// <remarks>
/// 导入去重 → 匹配引擎建议 → 确认在当前 Draft 对账生成勾选行（ReconciliationService 零改动）。
/// 匹配/确认限本位币科目（外币可导入，suggest/confirm 返回 400）。确认的并发由
/// <see cref="ReconciliationLine"/>.JournalLineId 全局唯一索引兜底（catch 包住 InsertAsync 翻译 409）。
/// </remarks>
public class BankFeedService : ApplicationService, IBankFeedService
{
    private readonly IRepository<BankTransaction, Guid> _txnRepository;
    private readonly IRepository<BankImportBatch, Guid> _batchRepository;
    private readonly IRepository<ReconciliationLine, Guid> _reconLineRepository;
    private readonly IRepository<BankAccount, Guid> _bankAccountRepository;
    private readonly IRepository<Reconciliation, Guid> _reconRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly FinanceDocumentHelper _helper;
    private readonly BankMatchEngine _engine;
    private readonly IBankRuleEvaluator _ruleEvaluator;
    private readonly IReadOnlyRepository<BankRule, Guid> _ruleRepository;
    private readonly BankDocumentDrafter _drafter;
    private readonly BankStatementIngestor _ingestor;
    private readonly ILedgerPostingService _postingService;
    private readonly IEnumerable<IBankFeedProvider> _providers;
    private readonly FinanceOptions _options;

    public BankFeedService(
        IServiceProvider serviceProvider,
        IRepository<BankTransaction, Guid> txnRepository,
        IRepository<BankImportBatch, Guid> batchRepository,
        IRepository<ReconciliationLine, Guid> reconLineRepository,
        IRepository<BankAccount, Guid> bankAccountRepository,
        IRepository<Reconciliation, Guid> reconRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        FinanceDocumentHelper helper,
        BankMatchEngine engine,
        IBankRuleEvaluator ruleEvaluator,
        IReadOnlyRepository<BankRule, Guid> ruleRepository,
        BankDocumentDrafter drafter,
        BankStatementIngestor ingestor,
        ILedgerPostingService postingService,
        IOptionsSnapshot<FinanceOptions> options,
        IEnumerable<IBankFeedProvider>? providers = null)
        : base(serviceProvider)
    {
        _txnRepository = Check.NotNull(txnRepository);
        _batchRepository = Check.NotNull(batchRepository);
        _reconLineRepository = Check.NotNull(reconLineRepository);
        _bankAccountRepository = Check.NotNull(bankAccountRepository);
        _reconRepository = Check.NotNull(reconRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _helper = Check.NotNull(helper);
        _engine = Check.NotNull(engine);
        _ruleEvaluator = Check.NotNull(ruleEvaluator);
        _ruleRepository = Check.NotNull(ruleRepository);
        _drafter = Check.NotNull(drafter);
        _ingestor = Check.NotNull(ingestor);
        _postingService = Check.NotNull(postingService);
        _options = Check.NotNull(options).Value;
        _providers = providers ?? Enumerable.Empty<IBankFeedProvider>();
    }

    private string BaseCurrency => _helper.NormalizeCurrency(null);

    private bool IsForeignAccount(string? accountCurrency)
        => !string.IsNullOrEmpty(accountCurrency) &&
           !string.Equals(accountCurrency.Trim(), BaseCurrency, StringComparison.OrdinalIgnoreCase);

    public async Task<Result<IPagedList<BankTransactionDto>>> GetPagedAsync(BankTransactionQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _txnRepository.AsNoTracking();
        if (query.AccountId.HasValue)
            queryable = queryable.Where(t => t.AccountId == query.AccountId.Value);
        if (query.ImportBatchId.HasValue)
            queryable = queryable.Where(t => t.ImportBatchId == query.ImportBatchId.Value);
        if (query.Status.HasValue)
            queryable = queryable.Where(t => t.Status == query.Status.Value);
        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.ToUtcDate();
            queryable = queryable.Where(t => t.TxnDate >= from);
        }
        if (query.DateTo.HasValue)
        {
            var toExclusive = query.DateTo.Value.ToUtcDate().AddDays(1);
            queryable = queryable.Where(t => t.TxnDate < toExclusive);
        }
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(t =>
                (t.Description != null && t.Description.ToLower().Contains(keyword)) ||
                (t.Payee != null && t.Payee.ToLower().Contains(keyword)) ||
                (t.Reference != null && t.Reference.ToLower().Contains(keyword)));
        }

        var pagedList = await queryable
            .OrderByDescending(t => t.TxnDate)
            .ThenByDescending(t => t.CreationTime)
            .ProjectTo<BankTransaction, BankTransactionDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        await FillRuleSuggestionsAsync(pagedList.Items, cancellationToken);
        return Ok(pagedList);
    }

    /// <summary>
    /// 把命中的规则展开成可直接下手的建议（单据类型 + 归类科目 + 往来方）。
    /// </summary>
    /// <remarks>
    /// 流水上只存规则 Id——规则被改了建议就该跟着改，规则才是意图所在。展开放在
    /// 这里而不是逐行查：一页几十行各查一次规则表，就是把一次往返变成几十次。
    /// 悬空 Id（规则已被删）当作"无建议"静默略过：规则是可删的运维配置，不该
    /// 让一条流水读不出来。
    /// </remarks>
    private async Task FillRuleSuggestionsAsync(IEnumerable<BankTransactionDto> source, CancellationToken cancellationToken)
    {
        var items = source as IReadOnlyCollection<BankTransactionDto> ?? source.ToList();
        var ruleIds = items.Where(t => t.SuggestedRuleId.HasValue).Select(t => t.SuggestedRuleId!.Value).Distinct().ToList();
        if (ruleIds.Count == 0)
            return;

        var rules = await _ruleRepository.AsNoTracking()
            .Where(r => ruleIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name, r.DocType, r.CounterAccountId, r.PartyId, r.PaymentMethod })
            .ToListAsync(cancellationToken);
        if (rules.Count == 0)
            return;

        var accountIds = rules.Where(r => r.CounterAccountId.HasValue).Select(r => r.CounterAccountId!.Value).Distinct().ToList();
        var accountNames = accountIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _accountRepository.AsNoTracking()
                .Where(a => accountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => $"{a.Code} {a.Name}", cancellationToken);

        var byId = rules.ToDictionary(r => r.Id);
        foreach (var item in items)
        {
            if (!item.SuggestedRuleId.HasValue || !byId.TryGetValue(item.SuggestedRuleId.Value, out var rule))
                continue;

            item.SuggestedRuleName = rule.Name;
            item.SuggestedDocType = rule.DocType;
            item.SuggestedCounterAccountId = rule.CounterAccountId;
            item.SuggestedPartyId = rule.PartyId;
            item.SuggestedPaymentMethod = rule.PaymentMethod;
            if (rule.CounterAccountId.HasValue && accountNames.TryGetValue(rule.CounterAccountId.Value, out var name))
                item.SuggestedCounterAccountName = name;
        }
    }

    // 摄取（文件解析 / 提供者拉取 / 去重落库）与对账是两件事，前者住在
    // BankStatementIngestor 里；这里保留接口方法并转交。
    public Task<Result<BankImportResultDto>> ImportStatementAsync(
        Guid accountId, BankTransactionSource source, string? fileName, string content, CsvMappingDto? mapping,
        CancellationToken cancellationToken = default)
        => _ingestor.ImportStatementAsync(accountId, source, fileName, content, mapping, cancellationToken);

    public Task<Result<BankImportResultDto>> PullFromProviderAsync(PullBankFeedDto input, CancellationToken cancellationToken = default)
        => _ingestor.PullFromProviderAsync(input, cancellationToken);

    public async Task<Result<BankSuggestResultDto>> SuggestMatchesAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account == null)
            return Fail<BankSuggestResultDto>("Account not found.", 404);
        if (IsForeignAccount(account.Currency))
            return Fail<BankSuggestResultDto>("Automatic matching is limited to base-currency accounts in this version.", 400);

        var pending = await _txnRepository.AsQueryable(true)
            .Where(t => t.AccountId == accountId && t.Status == BankTransactionStatus.Pending)
            .OrderBy(t => t.TxnDate)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
            return Ok(new BankSuggestResultDto());

        var autoConfirm = _options.BankFeedAutoConfirmExactMatches;
        // 同一科目至多一张 Draft 对账，而自动确认与 AutoApply 规则都要它 —— 查一次共用
        var draft = await _reconRepository.AsNoTracking()
            .FirstOrDefaultAsync(r => r.AccountId == accountId && r.Status == ReconciliationStatus.Draft, cancellationToken);

        var summary = new BankSuggestResultDto { Evaluated = pending.Count };
        var assigned = new HashSet<Guid>();

        // 自动入账的清单在事务内收集、事务外执行，理由见下方 ApplyRuleAsync。
        var autoApply = new List<(Guid TxnId, BankRuleMatch Rule)>();

        // 规则回答的是匹配引擎回答不了的那一半：账上根本没有对手方，但这笔流水
        // 是什么已经知道了。一次评估整批，逐条查规则表会把 N 条流水变成 N 次往返。
        var ruleMatches = await _ruleEvaluator.EvaluateManyAsync(pending, cancellationToken);

        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                foreach (var txn in pending)
                {
                    var suggestion = await _engine.SuggestAsync(txn, ct);
                    if (suggestion == null || assigned.Contains(suggestion.JournalLineId))
                    {
                        txn.SuggestedJournalLineId = null;
                        txn.MatchConfidence = null;
                        txn.MatchRule = null;

                        // 没有对手方 → 交给规则。规则只在这里参与：账上已经有那笔钱
                        // 时，凭空再记一笔是重复入账。
                        if (ruleMatches.TryGetValue(txn.Id, out var rule))
                        {
                            txn.SuggestedRuleId = rule.RuleId;

                            // 已经生成过单据的流水不再自动入账：上一轮多半是过账失败留下的
                            // 草稿，再跑一遍只会每小时多一张没人引用的草稿。
                            if (rule.AutoApply && draft != null && txn.CreatedDocId == null)
                                autoApply.Add((txn.Id, rule));
                            else
                                summary.RuleSuggested++;
                        }
                        else
                        {
                            txn.SuggestedRuleId = null;
                        }

                        await _txnRepository.UpdateAsync(txn, ct);
                        continue;
                    }

                    // 账上找到了对手方，规则的建议就此作废——它只是备选解释。
                    txn.SuggestedRuleId = null;

                    if (autoConfirm && draft != null && suggestion.Rule == "exact-ref")
                    {
                        var line = new ReconciliationLine
                        {
                            ReconciliationId = draft.Id,
                            JournalLineId = suggestion.JournalLineId,
                            TenantId = draft.TenantId
                        };
                        await _reconLineRepository.InsertAsync(line, ct);

                        txn.Status = BankTransactionStatus.Matched;
                        txn.MatchedJournalLineId = suggestion.JournalLineId;
                        txn.ReconciliationLineId = line.Id;
                        txn.MatchConfidence = suggestion.Confidence;
                        txn.MatchRule = suggestion.Rule;
                        txn.SuggestedJournalLineId = null;
                        await _txnRepository.UpdateAsync(txn, ct);

                        assigned.Add(suggestion.JournalLineId);
                        summary.AutoConfirmed++;
                    }
                    else
                    {
                        txn.SuggestedJournalLineId = suggestion.JournalLineId;
                        txn.MatchConfidence = suggestion.Confidence;
                        txn.MatchRule = suggestion.Rule;
                        await _txnRepository.UpdateAsync(txn, ct);
                        summary.Suggested++;
                    }
                }

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<BankSuggestResultDto>("A suggested line was cleared concurrently. Reload and retry.", 409);
        }

        // ★自动入账在建议事务**提交之后**逐条执行。
        // 它内部要建单、过账、确认匹配，每一步都自带工作单元；嵌进上面那个批量事务里，
        // 任一步失败就会把外层事务一并物理回滚并把嵌套深度清零，接着外层提交撞上
        // "事务未启用" —— 整批已经算好的建议凭空消失，而错误信息与真正的原因无关。
        foreach (var (txnId, rule) in autoApply)
        {
            if (await ApplyRuleAsync(txnId, rule, cancellationToken))
                summary.AutoCategorized++;
            else
                summary.RuleSuggested++;
        }

        return Ok(summary);
    }

    /// <summary>
    /// 执行一条 AutoApply 规则：建单 → 过账 → 用过账产生的分录行确认匹配。
    /// </summary>
    /// <remarks>
    /// 与「新建并对账」(<c>PostAndMatch</c>) 走同一条路径，所以自动入账与手工
    /// 点一次的结果逐字相同——不存在"自动记的账和手动记的账不一样"这种事。
    ///
    /// 失败返回 false 而**不抛**：一条配错的规则不该让整批建议计算失败；那条
    /// 流水退回普通建议，操作员在界面上照常处理。但**必须留下日志** —— 否则一条
    /// 配错的规则会每轮静默失败一次，没有任何地方看得见。
    ///
    /// 必须在批量事务**之外**调用（调用点有详述）。
    /// </remarks>
    private async Task<bool> ApplyRuleAsync(Guid bankTransactionId, BankRuleMatch rule, CancellationToken cancellationToken)
    {
        var input = new CreateBankDocumentDto
        {
            DocType = rule.DocType,
            CounterAccountId = rule.CounterAccountId,
            PartyId = rule.PartyId,
            PaymentMethod = rule.PaymentMethod,
            PostAndMatch = true,
        };

        // 走公共入口而不是另开一条内部路径：自动入账与操作员手工点一次的
        // 前置校验、写入顺序、失败回滚必须逐字相同。
        var result = await CreateDocumentAsync(bankTransactionId, input, cancellationToken);
        if (result.Succeeded && result.Data?.Matched == true)
            return true;

        Logger?.LogWarning(
            "Bank rule {RuleId} could not be applied to transaction {TransactionId}: {Reason}",
            rule.RuleId, bankTransactionId, result.Message ?? "the document was created but not matched");
        return false;
    }

    public async Task<Result<List<BankMatchCandidateDto>>> GetCandidatesAsync(Guid bankTransactionId, CancellationToken cancellationToken = default)
    {
        var txn = await _txnRepository.AsNoTracking().FirstOrDefaultAsync(t => t.Id == bankTransactionId, cancellationToken);
        if (txn == null)
            return Fail<List<BankMatchCandidateDto>>("Bank transaction not found.", 404);

        var account = await _accountRepository.AsNoTracking().FirstOrDefaultAsync(a => a.Id == txn.AccountId, cancellationToken);
        if (account != null && IsForeignAccount(account.Currency))
            return Fail<List<BankMatchCandidateDto>>("Matching is limited to base-currency accounts in this version.", 400);

        var candidates = await _engine.GetCandidatesAsync(txn.AccountId, txn.Amount, cancellationToken);
        var list = candidates.Select(c => new BankMatchCandidateDto
        {
            JournalLineId = c.JournalLineId,
            JournalEntryId = c.JournalEntryId,
            EntryNumber = c.EntryNumber,
            PostingDate = c.PostingDate,
            Memo = c.Memo,
            Amount = c.NetAmount
        }).ToList();

        return Ok(list);
    }

    public async Task<Result<BankTransactionDto>> ConfirmMatchAsync(Guid bankTransactionId, ConfirmBankMatchDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var txn = await _txnRepository.AsQueryable(true).FirstOrDefaultAsync(t => t.Id == bankTransactionId, cancellationToken);
        if (txn == null)
            return Fail<BankTransactionDto>("Bank transaction not found.", 404);
        if (txn.Status != BankTransactionStatus.Pending)
            return Fail<BankTransactionDto>("Only pending transactions can be matched.", 409);

        var account = await _accountRepository.AsNoTracking().FirstOrDefaultAsync(a => a.Id == txn.AccountId, cancellationToken);
        if (account != null && IsForeignAccount(account.Currency))
            return Fail<BankTransactionDto>("Matching is limited to base-currency accounts in this version.", 400);

        var journalLineId = input.JournalLineId ?? txn.SuggestedJournalLineId;
        if (journalLineId == null)
            return Fail<BankTransactionDto>("No journal line was provided and there is no suggested match.", 400);

        // 用引擎候选集重校验：命中即行属于科目、已过账、未 cleared、未被占用、金额精确相等
        var candidates = await _engine.GetCandidatesAsync(txn.AccountId, txn.Amount, cancellationToken);
        if (candidates.All(c => c.JournalLineId != journalLineId.Value))
            return Fail<BankTransactionDto>("The selected journal line is not a valid, unmatched candidate for this transaction.", 400);

        // tracked 加载：确认时同 UoW 内 bump 父对账的乐观戳，与 CompleteAsync 的父行更新互斥，
        // 杜绝"读到 Draft → 并发完成 → 再插勾选行进已完成对账"的 TOCTOU（累计 cleared 锚点被静默漂移）。
        var draft = await _reconRepository.AsQueryable(true).FirstOrDefaultAsync(r => r.AccountId == txn.AccountId && r.Status == ReconciliationStatus.Draft, cancellationToken);
        if (draft == null)
            return Fail<BankTransactionDto>("Create a draft reconciliation for this account before confirming matches.", 400);

        Guid reconLineId = Guid.Empty;
        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var line = new ReconciliationLine
                {
                    ReconciliationId = draft.Id,
                    JournalLineId = journalLineId.Value,
                    TenantId = draft.TenantId
                };
                await _reconLineRepository.InsertAsync(line, ct);

                txn.Status = BankTransactionStatus.Matched;
                txn.MatchedJournalLineId = journalLineId.Value;
                txn.ReconciliationLineId = line.Id;
                if (input.JournalLineId != null)
                {
                    // 用户显式挑选：记为人工确认（置信度 1.0）
                    txn.MatchConfidence = 1.0m;
                    txn.MatchRule = "manual";
                }
                txn.SuggestedJournalLineId = null;
                await _txnRepository.UpdateAsync(txn, ct);

                // 触碰父对账行以轮换其并发戳（WHERE stamp=old）：若对账已被并发 CompleteAsync 完成，
                // 此更新影响 0 行 → DbUpdateConcurrencyException → 整批回滚 + 409
                await _reconRepository.UpdateAsync(draft, ct);

                reconLineId = line.Id;
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<BankTransactionDto>("The journal line was cleared concurrently by another reconciliation. Reload and retry.", 409);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<BankTransactionDto>("The transaction was modified by another operation. Reload and retry.", 409);
        }

        await PublishEventAsync(new BankTransactionMatchedEvent
        {
            BankTransactionId = txn.Id,
            AccountId = txn.AccountId,
            JournalLineId = journalLineId.Value,
            ReconciliationLineId = reconLineId,
            TenantId = txn.TenantId
        }, cancellationToken);

        return await GetDtoAsync(txn.Id, cancellationToken);
    }

    public async Task<Result<BankTransactionDto>> UnmatchAsync(Guid bankTransactionId, CancellationToken cancellationToken = default)
    {
        var txn = await _txnRepository.AsQueryable(true).FirstOrDefaultAsync(t => t.Id == bankTransactionId, cancellationToken);
        if (txn == null)
            return Fail<BankTransactionDto>("Bank transaction not found.", 404);
        if (txn.Status != BankTransactionStatus.Matched || txn.ReconciliationLineId == null)
            return Fail<BankTransactionDto>("Only matched transactions can be unmatched.", 409);

        var line = await _reconLineRepository.AsQueryable(true).FirstOrDefaultAsync(l => l.Id == txn.ReconciliationLineId.Value, cancellationToken);
        Reconciliation? recon = null;
        if (line != null)
        {
            // tracked 加载：删勾选行时同 UoW 内 bump 父对账的乐观戳（理由见 ConfirmMatchAsync）
            recon = await _reconRepository.AsQueryable(true).FirstOrDefaultAsync(r => r.Id == line.ReconciliationId, cancellationToken);
            if (recon != null && recon.Status == ReconciliationStatus.Completed)
                return Fail<BankTransactionDto>("The reconciliation is completed and locked; the match cannot be undone.", 409);
        }

        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                if (line != null)
                    await _reconLineRepository.DeleteAsync(line, ct);

                txn.Status = BankTransactionStatus.Pending;
                txn.MatchedJournalLineId = null;
                txn.ReconciliationLineId = null;
                txn.MatchConfidence = null;
                txn.MatchRule = null;
                await _txnRepository.UpdateAsync(txn, ct);

                // 触碰父对账行以轮换其并发戳（WHERE stamp=old）：勾选行本身无并发令牌，若不 bump 父行，
                // 本次解除可与 CompleteAsync 交错，把一行从**已完成**的对账里抽掉 —— 该期从此永久对不平
                // 且不能重开。影响 0 行（对账已被并发完成）→ DbUpdateConcurrencyException → 整体回滚 + 409。
                if (recon != null)
                    await _reconRepository.UpdateAsync(recon, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<BankTransactionDto>("The transaction was modified by another operation. Reload and retry.", 409);
        }

        return await GetDtoAsync(txn.Id, cancellationToken);
    }

    public async Task<Result<BankTransactionDto>> ExcludeAsync(Guid bankTransactionId, CancellationToken cancellationToken = default)
    {
        var txn = await _txnRepository.AsQueryable(true).FirstOrDefaultAsync(t => t.Id == bankTransactionId, cancellationToken);
        if (txn == null)
            return Fail<BankTransactionDto>("Bank transaction not found.", 404);
        if (txn.Status == BankTransactionStatus.Matched)
            return Fail<BankTransactionDto>("Unmatch the transaction before excluding it.", 409);
        if (txn.Status == BankTransactionStatus.Excluded)
            return Ok((await GetDtoAsync(txn.Id, cancellationToken)).Data!);

        // 建议一并清掉（含规则建议）：排除的意思就是"这行不入账"，还挂着一条
        // "来自规则 X" 的归类建议只会让操作员以为它还等着处理。
        txn.Status = BankTransactionStatus.Excluded;
        txn.SuggestedJournalLineId = null;
        txn.SuggestedRuleId = null;
        txn.MatchConfidence = null;
        txn.MatchRule = null;
        await _txnRepository.UpdateAsync(txn, cancellationToken);
        await _txnRepository.SaveChangesAsync(cancellationToken);
        return await GetDtoAsync(txn.Id, cancellationToken);
    }

    public async Task<Result<BankTransactionDto>> RestoreAsync(Guid bankTransactionId, CancellationToken cancellationToken = default)
    {
        var txn = await _txnRepository.AsQueryable(true).FirstOrDefaultAsync(t => t.Id == bankTransactionId, cancellationToken);
        if (txn == null)
            return Fail<BankTransactionDto>("Bank transaction not found.", 404);
        if (txn.Status != BankTransactionStatus.Excluded)
            return Fail<BankTransactionDto>("Only excluded transactions can be restored.", 409);

        txn.Status = BankTransactionStatus.Pending;
        await _txnRepository.UpdateAsync(txn, cancellationToken);
        await _txnRepository.SaveChangesAsync(cancellationToken);
        return await GetDtoAsync(txn.Id, cancellationToken);
    }

    public async Task<Result<BankDocumentResultDto>> CreateDocumentAsync(Guid bankTransactionId, CreateBankDocumentDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var txn = await _txnRepository.AsQueryable(true).FirstOrDefaultAsync(t => t.Id == bankTransactionId, cancellationToken);
        if (txn == null)
            return Fail<BankDocumentResultDto>("Bank transaction not found.", 404);
        if (txn.Status != BankTransactionStatus.Pending)
            return Fail<BankDocumentResultDto>("Only pending transactions can spawn a document.", 409);

        // 「新建并对账」的两个前置条件与 ConfirmMatchAsync 逐字一致，且在建任何东西
        // **之前**校验：先建后发现不能确认，会留下一张操作员没打算单独维护的孤儿草稿。
        if (input.PostAndMatch)
        {
            var gateAccount = await _accountRepository.AsNoTracking().FirstOrDefaultAsync(a => a.Id == txn.AccountId, cancellationToken);
            if (gateAccount != null && IsForeignAccount(gateAccount.Currency))
                return Fail<BankDocumentResultDto>("Matching is limited to base-currency accounts in this version.", 400);

            var hasDraft = await _reconRepository.AsNoTracking()
                .AnyAsync(r => r.AccountId == txn.AccountId && r.Status == ReconciliationStatus.Draft, cancellationToken);
            if (!hasDraft)
                return Fail<BankDocumentResultDto>("Create a draft reconciliation for this account before confirming matches.", 400);
        }

        var draft = await _drafter.CreateDraftAsync(txn, input, cancellationToken);
        if (!draft.Succeeded)
            return Fail<BankDocumentResultDto>(draft.Message!, draft.Code ?? 400);
        var (docType, docId) = draft.Data;

        txn.CreatedDocType = docType;
        txn.CreatedDocId = docId;
        await _txnRepository.UpdateAsync(txn, cancellationToken);
        await _txnRepository.SaveChangesAsync(cancellationToken);

        var result = new BankDocumentResultDto { DocType = docType, DocId = docId };
        if (!input.PostAndMatch)
            return Ok(result);

        // 草稿此刻已经落库并挂在流水上（CreatedDocType/CreatedDocId）。过账失败时**不删**它：
        // 那是操作员已经能在单据列表里看到、改完再过账的东西，删掉等于把刚录进去的内容扔了。
        // 但消息里必须说清楚它还在，否则读起来像什么都没发生 —— 而流水下次仍会带出这张草稿。
        var postResult = await _drafter.PostDraftAsync(docType, docId, cancellationToken);
        if (!postResult.Succeeded)
            return Fail<BankDocumentResultDto>(
                $"{postResult.Message} The draft {docType} was created and is still linked to this transaction; fix it and post it manually.",
                postResult.Code ?? 400);
        result.Posted = true;

        // 过账产生的、落在本银行科目上的那一行即是要勾选的行。按来源反查而不是
        // 拿引擎候选集里"金额相等的某一行"——同额候选可能不止一条，猜错就把别人
        // 的流水配到了这张新单据上。凭证也按这一行反查它所属的那张：跨币种划转会
        // 过账出 2-3 张凭证，"第一张"未必承载本银行科目那一行。
        var entries = await _postingService.GetBySourceAsync(docType, docId.ToString(), cancellationToken);
        var entry = entries.Succeeded
            ? entries.Data!.FirstOrDefault(e => e.Lines.Any(l => l.AccountId == txn.AccountId))
            : null;
        if (entry == null)
            return Fail<BankDocumentResultDto>("The posted document produced no journal line on this bank account.", 500);
        var line = entry.Lines.First(l => l.AccountId == txn.AccountId);
        result.JournalEntryId = entry.Id;

        var confirmResult = await ConfirmMatchAsync(txn.Id, new ConfirmBankMatchDto { JournalLineId = line.Id }, cancellationToken);
        if (!confirmResult.Succeeded)
            return Fail<BankDocumentResultDto>(confirmResult.Message!, confirmResult.Code ?? 400);
        result.Matched = true;

        return Ok(result);
    }


    public async Task<Result<IPagedList<BankImportBatchDto>>> GetBatchesAsync(BankImportBatchQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _batchRepository.AsNoTracking();
        if (query.AccountId.HasValue)
            queryable = queryable.Where(b => b.AccountId == query.AccountId.Value);

        var pagedList = await queryable
            .OrderByDescending(b => b.CreationTime)
            .ProjectTo<BankImportBatch, BankImportBatchDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        if (pagedList.Items.Count > 0)
        {
            var batchIds = pagedList.Items.Select(b => b.Id).ToList();
            var matchedCounts = await _txnRepository.AsNoTracking()
                .Where(t => batchIds.Contains(t.ImportBatchId) && t.Status == BankTransactionStatus.Matched)
                .GroupBy(t => t.ImportBatchId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

            var accountIds = pagedList.Items.Select(b => b.AccountId).Distinct().ToList();
            var names = await _accountRepository.AsNoTracking()
                .Where(a => accountIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Code, a.Name })
                .ToDictionaryAsync(a => a.Id, a => $"{a.Code} {a.Name}", cancellationToken);

            foreach (var dto in pagedList.Items)
            {
                dto.MatchedCount = matchedCounts.GetValueOrDefault(dto.Id);
                dto.AccountName = names.GetValueOrDefault(dto.AccountId);
            }
        }

        return Ok(pagedList);
    }

    public async Task<Result> DeleteBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var batch = await _batchRepository.AsQueryable(true).FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);
        if (batch == null)
            return Fail("Import batch not found.", 404);

        var hasMatched = await _txnRepository.AnyAsync(t => t.ImportBatchId == batchId && t.Status == BankTransactionStatus.Matched, cancellationToken);
        if (hasMatched)
            return Fail("The batch has matched transactions; unmatch them before deleting the batch.", 409);

        try
        {
            return await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var txns = await _txnRepository.ToListAsync(t => t.ImportBatchId == batchId, ct);
                if (txns.Count > 0)
                    await _txnRepository.DeleteManyAsync(txns, ct);
                await _batchRepository.DeleteAsync(batch, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("The batch was modified by another operation. Reload and retry.", 409);
        }
    }

    private async Task<Result<BankTransactionDto>> GetDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        var txn = await _txnRepository.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (txn == null)
            return Fail<BankTransactionDto>("Bank transaction not found.", 404);
        return Ok(txn.MapTo<BankTransactionDto>());
    }

}
