namespace Tnzi.Finance.Services;

/// <summary>
/// 银行对账服务
/// </summary>
/// <remarks>
/// join 表方案：<see cref="ReconciliationLine"/> 引用已过账的 <see cref="JournalLine"/>，
/// 不修改总账行；cleared = 存在关联行，JournalLineId 唯一索引防止跨对账重复勾选。
/// 完成条件：对账单期末余额 = 该科目全部已勾选行的累计净额（首次对账从零起算）。
/// 已完成对账的差额展示被冻结为完成时刻的事实（差额 0），不随后续对账推进重算。
/// 冲销对（原行 + 冲销行）净额为 0，可同时勾选互抵。首版限本位币科目。
/// P3 银行流水导入落地后，匹配引擎的产出即自动生成勾选行——那些行由导入的银行流水
/// 持有（<c>MatchedJournalLineId</c>/<c>ReconciliationLineId</c> 回指），本服务不得单方面丢弃，
/// 见 <see cref="SetLinesAsync"/>。
/// </remarks>
public class ReconciliationService : ApplicationService, IReconciliationService
{
    /// <summary>
    /// 一次问持有者的最大行数。
    /// </summary>
    /// <remarks>
    /// 契约把入参说成"有界"，而工作区的候选集其实不分页 —— 老账户上几万行是常态。
    /// 1000 远低于 SQL Server 的 2100 参数上限，也让往返次数保持在个位数。
    /// </remarks>
    private const int HoldProbeBatchSize = 1000;

    private readonly IRepository<Reconciliation, Guid> _reconciliationRepository;
    private readonly IRepository<ReconciliationLine, Guid> _lineRepository;
    private readonly IReadOnlyRepository<JournalLine, Guid> _journalLineRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    /// <summary>
    /// 账本之外持有勾选行的东西（银行流水等）。未注册即"无人持有"，回到引入契约前的行为。
    /// </summary>
    private readonly IEnumerable<IJournalLineHoldProvider> _holdProviders;
    private readonly FinanceDocumentHelper _helper;
    private readonly FinanceOptions _options;

    public ReconciliationService(
        IServiceProvider serviceProvider,
        IRepository<Reconciliation, Guid> reconciliationRepository,
        IRepository<ReconciliationLine, Guid> lineRepository,
        IReadOnlyRepository<JournalLine, Guid> journalLineRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        IEnumerable<IJournalLineHoldProvider>? holdProviders,
        FinanceDocumentHelper helper,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _reconciliationRepository = Check.NotNull(reconciliationRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _journalLineRepository = Check.NotNull(journalLineRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _holdProviders = holdProviders ?? Enumerable.Empty<IJournalLineHoldProvider>();
        _helper = Check.NotNull(helper);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<IPagedList<ReconciliationDto>>> GetPagedAsync(ReconciliationQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _reconciliationRepository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(r => r.StatementDate)
            .ThenByDescending(r => r.CreationTime)
            .ProjectTo<Reconciliation, ReconciliationDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        await FillListComputedAsync(pagedList.Items, cancellationToken);
        return Ok(pagedList);
    }

    public async Task<Result<ReconciliationDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var reconciliation = await _reconciliationRepository.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (reconciliation == null)
            return Fail<ReconciliationDto>("Reconciliation not found.", 404);

        return Ok(await ToDtoAsync(reconciliation, cancellationToken));
    }

    public async Task<Result<ReconciliationDto>> CreateDraftAsync(CreateReconciliationDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        // 本位币科目与外币限定科目皆可对账；对账币种 = 科目限定币种 ?? 本位币（纯派生，币种不可变守卫背书）
        var accountResult = await _helper.GetFundsAccountAsync(input.AccountId, requiredCurrency: null, cancellationToken);
        if (!accountResult.Succeeded)
            return Fail<ReconciliationDto>(accountResult.Message!, accountResult.Code ?? 400);

        // 同一科目同时只允许一张进行中的对账（候选行归属才不产生歧义）；
        // check-then-act 竞态由 Draft 过滤唯一索引兜底
        var hasDraft = await _reconciliationRepository.AnyAsync(
            r => r.AccountId == input.AccountId && r.Status == ReconciliationStatus.Draft, cancellationToken);
        if (hasDraft)
            return Fail<ReconciliationDto>("A draft reconciliation already exists for this account. Complete or delete it first.", 409);

        var reconciliation = new Reconciliation
        {
            AccountId = input.AccountId,
            StatementDate = input.StatementDate.ToUtcDate(),
            StatementEndingBalance = _helper.Round(input.StatementEndingBalance),
            Note = input.Note
        };

        try
        {
            await _reconciliationRepository.InsertAsync(reconciliation, cancellationToken);
            await _reconciliationRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<ReconciliationDto>("A draft reconciliation already exists for this account. Complete or delete it first.", 409);
        }

        return Ok(await ToDtoAsync(reconciliation, cancellationToken));
    }

    public async Task<Result<ReconciliationDto>> UpdateDraftAsync(Guid id, CreateReconciliationDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var reconciliation = await _reconciliationRepository.AsQueryable(true)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (reconciliation == null)
            return Fail<ReconciliationDto>("Reconciliation not found.", 404);
        if (reconciliation.Status != ReconciliationStatus.Draft)
            return Fail<ReconciliationDto>("Completed reconciliations are locked.", 409);
        if (input.AccountId != reconciliation.AccountId)
            return Fail<ReconciliationDto>("The reconciliation account cannot be changed. Delete the draft and create a new one.", 400);

        reconciliation.StatementDate = input.StatementDate.ToUtcDate();
        reconciliation.StatementEndingBalance = _helper.Round(input.StatementEndingBalance);
        reconciliation.Note = input.Note;

        try
        {
            await _reconciliationRepository.UpdateAsync(reconciliation, cancellationToken);
            await _reconciliationRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<ReconciliationDto>("The reconciliation was modified by another operation. Reload and retry.", 409);
        }

        return Ok(await ToDtoAsync(reconciliation, cancellationToken));
    }

    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var reconciliation = await _reconciliationRepository.AsQueryable(true)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (reconciliation == null)
            return Fail("Reconciliation not found.", 404);
        if (reconciliation.Status != ReconciliationStatus.Draft)
            return Fail("Completed reconciliations are locked and cannot be deleted.", 409);

        // 行硬删 + 头软删须原子：并发方（另一端刚 Complete/编辑）触发并发冲突时整体回滚，
        // 不留下"行已删而头存活"的空壳草稿
        try
        {
            return await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var lines = await _lineRepository.ToListAsync(l => l.ReconciliationId == id, ct);
                if (lines.Count > 0)
                    await _lineRepository.DeleteManyAsync(lines, ct);
                await _reconciliationRepository.DeleteAsync(reconciliation, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("The reconciliation was modified by another operation. Reload and retry.", 409);
        }
    }

    public async Task<Result<ReconciliationWorksheetDto>> GetWorksheetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var reconciliation = await _reconciliationRepository.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (reconciliation == null)
            return Fail<ReconciliationWorksheetDto>("Reconciliation not found.", 404);

        return Ok(await BuildWorksheetAsync(reconciliation, cancellationToken));
    }

    public async Task<Result<ReconciliationWorksheetDto>> SetLinesAsync(Guid id, SetReconciliationLinesDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        Check.NotNull(input.JournalLineIds);

        var reconciliation = await _reconciliationRepository.AsQueryable(true)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (reconciliation == null)
            return Fail<ReconciliationWorksheetDto>("Reconciliation not found.", 404);
        if (reconciliation.Status != ReconciliationStatus.Draft)
            return Fail<ReconciliationWorksheetDto>("Completed reconciliations are locked.", 409);

        var requestedIds = input.JournalLineIds.Distinct().ToList();
        var (accountCurrency, _, _) = await LoadAccountAsync(reconciliation.AccountId, cancellationToken);

        // 全部校验先行：行属于对账科目、已过账、未被其它对账占用；
        // 外币口径额外要求行币种 == 对账币种（挡住本位币重估调整行被误勾）
        if (requestedIds.Count > 0)
        {
            var validQuery = _journalLineRepository.AsNoTracking()
                .Where(l => requestedIds.Contains(l.Id) && l.AccountId == reconciliation.AccountId && l.IsPosted);
            if (IsForeignCaliber(accountCurrency))
            {
                var reconCurrency = ReconciliationCurrencyOf(accountCurrency);
                validQuery = validQuery.Where(l => l.Currency == reconCurrency);
            }
            var validCount = await validQuery.CountAsync(cancellationToken);
            if (validCount != requestedIds.Count)
                return Fail<ReconciliationWorksheetDto>("Every line must be a posted ledger line of the reconciliation account in its reconciliation currency.", 400);

            var takenElsewhere = await _lineRepository.AnyAsync(
                l => requestedIds.Contains(l.JournalLineId) && l.ReconciliationId != id, cancellationToken);
            if (takenElsewhere)
                return Fail<ReconciliationWorksheetDto>("One or more lines are already cleared by another reconciliation.", 409);
        }

        var existing = await _lineRepository.ToListAsync(l => l.ReconciliationId == id, cancellationToken);
        var existingIds = existing.Select(l => l.JournalLineId).ToHashSet();
        var toRemove = existing.Where(l => !requestedIds.Contains(l.JournalLineId)).ToList();

        // 全量替换会删掉未入参的勾选行——若某行是银行流水的清算记录，删掉即让那笔 Matched
        // 流水指向一条不存在的行（孤儿：流水仍报 Matched，总账行却又变回可匹配）。
        // 释放路径只有一条：银行流水页的 unmatch（原子地删行 + 复位流水状态/回指字段）。
        // 工作区呈现端据 ReconciliationCandidateLineDto.IsStatementMatched 禁用复选框，
        // 此处是 API 客户端与并发确认的兜底
        if (toRemove.Count > 0)
        {
            var removedJournalLineIds = toRemove.Select(l => l.JournalLineId).ToList();
            var heldByStatement = false;
            foreach (var provider in _holdProviders)
            {
                if ((await provider.GetHoldsAsync(removedJournalLineIds, cancellationToken)).Count > 0)
                {
                    heldByStatement = true;
                    break;
                }
            }
            if (heldByStatement)
            {
                return Fail<ReconciliationWorksheetDto>(
                    "One or more lines you are clearing are matched to an imported bank transaction. " +
                    "Unmatch them on the bank feed screen first.", 409);
            }
        }

        var toAdd = requestedIds.Where(lineId => !existingIds.Contains(lineId))
            .Select(lineId => new ReconciliationLine
            {
                ReconciliationId = id,
                JournalLineId = lineId,
                TenantId = reconciliation.TenantId
            })
            .ToList();

        // 删旧 + 插新须原子（无环境事务时仓储逐调用立即提交，必须显式包工作单元）；
        // 并发勾选竞态由 JournalLineId 唯一索引兜底，UoW 提交冲突整体回滚后翻译 409
        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                if (toRemove.Count > 0)
                    await _lineRepository.DeleteManyAsync(toRemove, ct);
                if (toAdd.Count > 0)
                    await _lineRepository.InsertManyAsync(toAdd, ct);
                // 触碰父对账行以轮换其并发戳（WHERE stamp=old）：勾选行本身无并发令牌，若不 bump 父行，
                // 一次 SetLines 可与 CompleteAsync 交错、把行插进已完成对账并静默漂移累计 cleared 锚点。
                // 影响 0 行（对账已被并发完成）→ DbUpdateConcurrencyException → 整体回滚 + 409。
                await _reconciliationRepository.UpdateAsync(reconciliation, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<ReconciliationWorksheetDto>("One or more lines were cleared concurrently by another reconciliation. Reload and retry.", 409);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<ReconciliationWorksheetDto>("The reconciliation was completed by another operation. Reload and retry.", 409);
        }

        return Ok(await BuildWorksheetAsync(reconciliation, cancellationToken));
    }

    public async Task<Result<ReconciliationDto>> CompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var reconciliation = await _reconciliationRepository.AsQueryable(true)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (reconciliation == null)
            return Fail<ReconciliationDto>("Reconciliation not found.", 404);
        if (reconciliation.Status != ReconciliationStatus.Draft)
            return Fail<ReconciliationDto>("The reconciliation is already completed.", 409);

        var (accountCurrency, _, _) = await LoadAccountAsync(reconciliation.AccountId, cancellationToken);
        var clearedBalance = await ClearedBalanceAsync(reconciliation.AccountId, accountCurrency, cancellationToken);
        var difference = reconciliation.StatementEndingBalance - clearedBalance;
        if (difference != 0)
            return Fail<ReconciliationDto>($"The reconciliation is off by {difference}. Cleared balance {clearedBalance} must equal the statement ending balance {reconciliation.StatementEndingBalance}.", 400);

        reconciliation.Status = ReconciliationStatus.Completed;
        reconciliation.CompletedTime = DateTime.UtcNow;

        try
        {
            await _reconciliationRepository.UpdateAsync(reconciliation, cancellationToken);
            await _reconciliationRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<ReconciliationDto>("The reconciliation was modified by another operation. Reload and retry.", 409);
        }

        return Ok(await ToDtoAsync(reconciliation, cancellationToken, clearedBalance));
    }

    /// <summary>本位币币种</summary>
    private string BaseCurrency => _helper.NormalizeCurrency(null);

    /// <summary>
    /// 口径切换的单一私有判据：外币限定科目（Currency 非空且 != 本位币）走交易币口径（Txn），
    /// 本位币限定/不限币科目走本位币口径（Debit/Credit，现状零变化）
    /// </summary>
    private bool IsForeignCaliber(string? accountCurrency)
        => !string.IsNullOrEmpty(accountCurrency) &&
           !string.Equals(accountCurrency.Trim(), BaseCurrency, StringComparison.OrdinalIgnoreCase);

    /// <summary>对账币种 = 科目限定币种 ?? 本位币（纯派生）</summary>
    private string ReconciliationCurrencyOf(string? accountCurrency)
        => string.IsNullOrEmpty(accountCurrency) ? BaseCurrency : accountCurrency.Trim().ToUpperInvariant();

    /// <summary>加载对账科目的币种/编码/名称（币种不可变，安全缓存于操作内）</summary>
    private async Task<(string? Currency, string? Code, string? Name)> LoadAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _accountRepository.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new { a.Currency, a.Code, a.Name })
            .FirstOrDefaultAsync(cancellationToken);
        return (account?.Currency, account?.Code, account?.Name);
    }

    /// <summary>
    /// 科目累计已勾选净额（该科目全部对账的勾选行，借方为正）。
    /// 外币限定科目走交易币口径（Σ TxnDebit − TxnCredit WHERE 行币种 == 对账币种，天然排除本位币重估调整行）；
    /// 其余走本位币口径。行只能属于一张对账且同科目仅一张 Draft，故全量求和即累计 cleared 余额
    /// </summary>
    private async Task<decimal> ClearedBalanceAsync(Guid accountId, string? accountCurrency, CancellationToken cancellationToken)
    {
        var joined = _lineRepository.AsNoTracking()
            .Join(_journalLineRepository.AsNoTracking(), rl => rl.JournalLineId, jl => jl.Id, (rl, jl) => jl)
            .Where(jl => jl.AccountId == accountId);

        if (IsForeignCaliber(accountCurrency))
        {
            var reconCurrency = ReconciliationCurrencyOf(accountCurrency);
            var sums = await joined
                .Where(jl => jl.Currency == reconCurrency)
                .GroupBy(jl => 1)
                .Select(g => new { Debit = g.Sum(l => l.TxnDebit), Credit = g.Sum(l => l.TxnCredit) })
                .FirstOrDefaultAsync(cancellationToken);
            return (sums?.Debit ?? 0m) - (sums?.Credit ?? 0m);
        }

        var baseSums = await joined
            .GroupBy(jl => 1)
            .Select(g => new { Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
            .FirstOrDefaultAsync(cancellationToken);
        return (baseSums?.Debit ?? 0m) - (baseSums?.Credit ?? 0m);
    }

    /// <summary>
    /// 单实体 DTO 组装（账户名 + 对账币种 + 本对账行数 + 差额）。
    /// 已完成对账的差额冻结为完成时刻的事实（差额 0），不随后续对账的勾选推进重算；
    /// Draft 按累计 cleared 现算（可经 <paramref name="clearedBalance"/> 复用已算好的值）
    /// </summary>
    private async Task<ReconciliationDto> ToDtoAsync(Reconciliation reconciliation, CancellationToken cancellationToken, decimal? clearedBalance = null)
    {
        var dto = reconciliation.MapTo<ReconciliationDto>();

        var (accountCurrency, code, name) = await LoadAccountAsync(reconciliation.AccountId, cancellationToken);
        dto.AccountName = code == null ? null : $"{code} {name}";
        dto.Currency = ReconciliationCurrencyOf(accountCurrency);

        dto.LineCount = await _lineRepository.AsNoTracking()
            .CountAsync(l => l.ReconciliationId == reconciliation.Id, cancellationToken);

        if (reconciliation.Status == ReconciliationStatus.Completed)
        {
            dto.ClearedBalance = reconciliation.StatementEndingBalance;
            dto.Difference = 0m;
        }
        else
        {
            dto.ClearedBalance = clearedBalance ?? await ClearedBalanceAsync(reconciliation.AccountId, accountCurrency, cancellationToken);
            dto.Difference = reconciliation.StatementEndingBalance - dto.ClearedBalance;
        }

        return dto;
    }

    private async Task<ReconciliationWorksheetDto> BuildWorksheetAsync(Reconciliation reconciliation, CancellationToken cancellationToken)
    {
        var (accountCurrency, _, _) = await LoadAccountAsync(reconciliation.AccountId, cancellationToken);
        var foreign = IsForeignCaliber(accountCurrency);
        var reconCurrency = ReconciliationCurrencyOf(accountCurrency);

        // 反连接留在数据库侧：其它对账占用的行不进入候选（cleared 行随经营年限只增不减，
        // 决不能物化进内存再回填 NOT IN 参数列表）；IsSelected 同查询投影，单次往返。
        // 外币口径：候选只取本币行（天然排除本位币重估/调整行），金额投影交易币口径
        var reconLines = _lineRepository.AsNoTracking();
        var candidates = _journalLineRepository.AsNoTracking()
            .Where(l => l.AccountId == reconciliation.AccountId && l.IsPosted &&
                        !reconLines.Any(rl => rl.JournalLineId == l.Id && rl.ReconciliationId != reconciliation.Id));
        if (foreign)
            candidates = candidates.Where(l => l.Currency == reconCurrency);

        var lines = await candidates
            .OrderBy(l => l.PostingDate)
            .ThenBy(l => l.JournalEntry!.Number)
            .ThenBy(l => l.LineNumber)
            .Select(l => new ReconciliationCandidateLineDto
            {
                JournalLineId = l.Id,
                JournalEntryId = l.JournalEntryId,
                EntryNumber = l.JournalEntry!.Number,
                PostingDate = l.PostingDate,
                Memo = l.Memo ?? l.JournalEntry.Memo,
                Debit = foreign ? l.TxnDebit : l.Debit,
                Credit = foreign ? l.TxnCredit : l.Credit,
                IsSelected = reconLines.Any(rl => rl.JournalLineId == l.Id && rl.ReconciliationId == reconciliation.Id)
            })
            .ToListAsync(cancellationToken);

        // 该行是否被账本之外的东西（已匹配的银行流水）持有 → 呈现端禁用勾选框。
        // 从"同查询 EXISTS"改为"候选物化后再问一次"：持有者不在会计内核里，为它保留一个
        // 可组合进表达式树的 IQueryable 就得把银行域焊死在内核上。入参是**已经在内存里**
        // 的这一页候选（`lines` 刚 ToListAsync 出来），所以只是多一次有界往返，
        // 不是把只增不减的持有者全集物化出来。
        // ★分批问：候选集在一个经营多年的银行科目上可以是几万行（工作区目前不分页），
        // 整批塞进 IN 列表会撞上 SQL Server 的 2100 参数上限，工作区就此打不开。
        if (lines.Count > 0)
        {
            var heldIds = new HashSet<Guid>();
            foreach (var chunk in lines.Select(l => l.JournalLineId).Chunk(HoldProbeBatchSize))
            {
                foreach (var provider in _holdProviders)
                {
                    var held = await provider.GetHoldsAsync(chunk, cancellationToken);
                    foreach (var hold in held)
                        heldIds.Add(hold.JournalLineId);
                }
            }

            if (heldIds.Count > 0)
            {
                foreach (var line in lines.Where(l => heldIds.Contains(l.JournalLineId)))
                    line.IsStatementMatched = true;
            }
        }

        decimal clearedBalance;
        decimal difference;
        if (reconciliation.Status == ReconciliationStatus.Completed)
        {
            // 与 ToDtoAsync 同口径：已完成对账冻结完成时刻的事实
            clearedBalance = reconciliation.StatementEndingBalance;
            difference = 0m;
        }
        else
        {
            clearedBalance = await ClearedBalanceAsync(reconciliation.AccountId, accountCurrency, cancellationToken);
            difference = reconciliation.StatementEndingBalance - clearedBalance;
        }

        return new ReconciliationWorksheetDto
        {
            ReconciliationId = reconciliation.Id,
            Currency = reconCurrency,
            StatementEndingBalance = reconciliation.StatementEndingBalance,
            ClearedBalance = clearedBalance,
            Difference = difference,
            Lines = lines
        };
    }

    /// <summary>
    /// 列表 DTO 组装（账户名 + 对账币种 + 行数 + 累计已勾选净额 + 差额）。
    /// 与 <see cref="ToDtoAsync"/> 同口径——差额 0 必须意味着“已配平”，绝不能是“没算”
    /// </summary>
    private async Task FillListComputedAsync(IList<ReconciliationDto> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        var accountIds = items.Select(r => r.AccountId).Distinct().ToList();
        var accounts = await _accountRepository.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Code, a.Name, a.Currency })
            .ToListAsync(cancellationToken);
        var names = accounts.ToDictionary(a => a.Id, a => $"{a.Code} {a.Name}");
        var accountCurrencies = accounts.ToDictionary(a => a.Id, a => a.Currency);

        var reconciliationIds = items.Select(r => r.Id).ToList();
        var lineCounts = await _lineRepository.AsNoTracking()
            .Where(l => reconciliationIds.Contains(l.ReconciliationId))
            .GroupBy(l => l.ReconciliationId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        // 只有 Draft 行要现算累计 cleared（Completed 冻结为完成时刻的事实，见 ToDtoAsync）
        var draftAccountIds = items
            .Where(r => r.Status == ReconciliationStatus.Draft)
            .Select(r => r.AccountId)
            .Distinct()
            .ToList();
        var cleared = await ClearedBalancesAsync(draftAccountIds, accountCurrencies, cancellationToken);

        foreach (var dto in items)
        {
            dto.AccountName = names.GetValueOrDefault(dto.AccountId);
            dto.Currency = ReconciliationCurrencyOf(accountCurrencies.GetValueOrDefault(dto.AccountId));
            dto.LineCount = lineCounts.GetValueOrDefault(dto.Id);

            if (dto.Status == ReconciliationStatus.Completed)
            {
                dto.ClearedBalance = dto.StatementEndingBalance;
                dto.Difference = 0m;
            }
            else
            {
                dto.ClearedBalance = cleared.GetValueOrDefault(dto.AccountId);
                dto.Difference = dto.StatementEndingBalance - dto.ClearedBalance;
            }
        }
    }

    /// <summary>
    /// 批量版 <see cref="ClearedBalanceAsync"/>：单次分组查询覆盖整页（逐行现算就是 N+1）。
    /// 按 (科目, 行币种) 分组并同时投影两口径毛额，再在内存里按各科目自己的口径收敛——
    /// 一页里本位币科目与外币限定科目可以混排，口径判据始终是逐科目的。
    /// 分组基数 = 页内科目数 × 出现过的币种数，恒小
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> ClearedBalancesAsync(
        IReadOnlyCollection<Guid> accountIds,
        IReadOnlyDictionary<Guid, string?> accountCurrencies,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, decimal>();
        if (accountIds.Count == 0)
            return result;

        var rows = await _lineRepository.AsNoTracking()
            .Join(_journalLineRepository.AsNoTracking(), rl => rl.JournalLineId, jl => jl.Id, (rl, jl) => jl)
            .Where(jl => accountIds.Contains(jl.AccountId))
            .GroupBy(jl => new { jl.AccountId, jl.Currency })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.Currency,
                Debit = g.Sum(l => l.Debit),
                Credit = g.Sum(l => l.Credit),
                TxnDebit = g.Sum(l => l.TxnDebit),
                TxnCredit = g.Sum(l => l.TxnCredit)
            })
            .ToListAsync(cancellationToken);

        foreach (var accountId in accountIds)
        {
            var accountCurrency = accountCurrencies.GetValueOrDefault(accountId);
            var mine = rows.Where(r => r.AccountId == accountId);
            result[accountId] = IsForeignCaliber(accountCurrency)
                ? mine.Where(r => r.Currency == ReconciliationCurrencyOf(accountCurrency))
                      .Sum(r => r.TxnDebit - r.TxnCredit)
                : mine.Sum(r => r.Debit - r.Credit);
        }

        return result;
    }
}
