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
/// P3 银行流水导入落地后，匹配引擎的产出即自动生成勾选行。
/// </remarks>
public class ReconciliationService : ApplicationService, IReconciliationService
{
    private readonly IRepository<Reconciliation, Guid> _reconciliationRepository;
    private readonly IRepository<ReconciliationLine, Guid> _lineRepository;
    private readonly IReadOnlyRepository<JournalLine, Guid> _journalLineRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly FinanceDocumentHelper _helper;
    private readonly FinanceOptions _options;

    public ReconciliationService(
        IServiceProvider serviceProvider,
        IRepository<Reconciliation, Guid> reconciliationRepository,
        IRepository<ReconciliationLine, Guid> lineRepository,
        IReadOnlyRepository<JournalLine, Guid> journalLineRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        FinanceDocumentHelper helper,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _reconciliationRepository = Check.NotNull(reconciliationRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _journalLineRepository = Check.NotNull(journalLineRepository);
        _accountRepository = Check.NotNull(accountRepository);
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

        // 首版限本位币科目
        var accountResult = await _helper.GetFundsAccountAsync(input.AccountId, _options.BaseCurrency, cancellationToken);
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

        // 全部校验先行：行属于对账科目、已过账、未被其它对账占用
        if (requestedIds.Count > 0)
        {
            var validCount = await _journalLineRepository.AsNoTracking()
                .CountAsync(l => requestedIds.Contains(l.Id) && l.AccountId == reconciliation.AccountId && l.IsPosted, cancellationToken);
            if (validCount != requestedIds.Count)
                return Fail<ReconciliationWorksheetDto>("Every line must be a posted ledger line of the reconciliation account.", 400);

            var takenElsewhere = await _lineRepository.AnyAsync(
                l => requestedIds.Contains(l.JournalLineId) && l.ReconciliationId != id, cancellationToken);
            if (takenElsewhere)
                return Fail<ReconciliationWorksheetDto>("One or more lines are already cleared by another reconciliation.", 409);
        }

        var existing = await _lineRepository.ToListAsync(l => l.ReconciliationId == id, cancellationToken);
        var existingIds = existing.Select(l => l.JournalLineId).ToHashSet();
        var toRemove = existing.Where(l => !requestedIds.Contains(l.JournalLineId)).ToList();
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
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<ReconciliationWorksheetDto>("One or more lines were cleared concurrently by another reconciliation. Reload and retry.", 409);
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

        var clearedBalance = await ClearedBalanceAsync(reconciliation.AccountId, cancellationToken);
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

    /// <summary>
    /// 科目累计已勾选净额（该科目全部对账的勾选行，本位币借方为正）。
    /// 行只能属于一张对账且同科目仅一张 Draft，故全量求和即「历史已完成 + 本对账」的累计 cleared 余额
    /// </summary>
    private async Task<decimal> ClearedBalanceAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var sums = await _lineRepository.AsNoTracking()
            .Join(_journalLineRepository.AsNoTracking(), rl => rl.JournalLineId, jl => jl.Id, (rl, jl) => jl)
            .Where(jl => jl.AccountId == accountId)
            .GroupBy(jl => 1)
            .Select(g => new { Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
            .FirstOrDefaultAsync(cancellationToken);

        return (sums?.Debit ?? 0m) - (sums?.Credit ?? 0m);
    }

    /// <summary>
    /// 单实体 DTO 组装（账户名 + 本对账行数 + 差额）。
    /// 已完成对账的差额冻结为完成时刻的事实（差额 0），不随后续对账的勾选推进重算；
    /// Draft 按累计 cleared 现算（可经 <paramref name="clearedBalance"/> 复用已算好的值）
    /// </summary>
    private async Task<ReconciliationDto> ToDtoAsync(Reconciliation reconciliation, CancellationToken cancellationToken, decimal? clearedBalance = null)
    {
        var dto = reconciliation.MapTo<ReconciliationDto>();

        var account = await _accountRepository.AsNoTracking()
            .Where(a => a.Id == reconciliation.AccountId)
            .Select(a => new { a.Code, a.Name })
            .FirstOrDefaultAsync(cancellationToken);
        dto.AccountName = account == null ? null : $"{account.Code} {account.Name}";

        dto.LineCount = await _lineRepository.AsNoTracking()
            .CountAsync(l => l.ReconciliationId == reconciliation.Id, cancellationToken);

        if (reconciliation.Status == ReconciliationStatus.Completed)
        {
            dto.ClearedBalance = reconciliation.StatementEndingBalance;
            dto.Difference = 0m;
        }
        else
        {
            dto.ClearedBalance = clearedBalance ?? await ClearedBalanceAsync(reconciliation.AccountId, cancellationToken);
            dto.Difference = reconciliation.StatementEndingBalance - dto.ClearedBalance;
        }

        return dto;
    }

    private async Task<ReconciliationWorksheetDto> BuildWorksheetAsync(Reconciliation reconciliation, CancellationToken cancellationToken)
    {
        // 反连接留在数据库侧：其它对账占用的行不进入候选（cleared 行随经营年限只增不减，
        // 决不能物化进内存再回填 NOT IN 参数列表）；IsSelected 同查询投影，单次往返
        var reconLines = _lineRepository.AsNoTracking();
        var lines = await _journalLineRepository.AsNoTracking()
            .Where(l => l.AccountId == reconciliation.AccountId && l.IsPosted &&
                        !reconLines.Any(rl => rl.JournalLineId == l.Id && rl.ReconciliationId != reconciliation.Id))
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
                Debit = l.Debit,
                Credit = l.Credit,
                IsSelected = reconLines.Any(rl => rl.JournalLineId == l.Id && rl.ReconciliationId == reconciliation.Id)
            })
            .ToListAsync(cancellationToken);

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
            clearedBalance = await ClearedBalanceAsync(reconciliation.AccountId, cancellationToken);
            difference = reconciliation.StatementEndingBalance - clearedBalance;
        }

        return new ReconciliationWorksheetDto
        {
            ReconciliationId = reconciliation.Id,
            StatementEndingBalance = reconciliation.StatementEndingBalance,
            ClearedBalance = clearedBalance,
            Difference = difference,
            Lines = lines
        };
    }

    private async Task FillListComputedAsync(IList<ReconciliationDto> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        var accountIds = items.Select(r => r.AccountId).Distinct().ToList();
        var names = await _accountRepository.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Code, a.Name })
            .ToDictionaryAsync(a => a.Id, a => $"{a.Code} {a.Name}", cancellationToken);

        var reconciliationIds = items.Select(r => r.Id).ToList();
        var lineCounts = await _lineRepository.AsNoTracking()
            .Where(l => reconciliationIds.Contains(l.ReconciliationId))
            .GroupBy(l => l.ReconciliationId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        foreach (var dto in items)
        {
            dto.AccountName = names.GetValueOrDefault(dto.AccountId);
            dto.LineCount = lineCounts.GetValueOrDefault(dto.Id);
        }
    }
}
