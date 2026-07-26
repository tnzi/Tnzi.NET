namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 银行规则管理
/// </summary>
public class BankRuleService : ApplicationService, IBankRuleService
{
    private readonly IRepository<BankRule, Guid> _repository;
    private readonly IRepository<BankRuleCondition, Guid> _conditionRepository;
    private readonly IReadOnlyRepository<BankTransaction, Guid> _txnRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly IBankRuleEvaluator _evaluator;

    public BankRuleService(
        IServiceProvider serviceProvider,
        IRepository<BankRule, Guid> repository,
        IRepository<BankRuleCondition, Guid> conditionRepository,
        IReadOnlyRepository<BankTransaction, Guid> txnRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        IBankRuleEvaluator evaluator)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _conditionRepository = Check.NotNull(conditionRepository);
        _txnRepository = Check.NotNull(txnRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _evaluator = Check.NotNull(evaluator);
    }

    public async Task<Result<IPagedList<BankRuleDto>>> GetPagedAsync(BankRuleQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _repository.AsNoTracking();

        if (query.IsEnabled.HasValue)
            queryable = queryable.Where(r => r.IsEnabled == query.IsEnabled.Value);

        // 按账户过滤时带上"全部账户"规则：它们同样会作用于该账户，
        // 把它们藏起来会让操作员看不见真正在起作用的那条。
        if (query.AccountId.HasValue)
            queryable = queryable.Where(r => r.AccountId == null || r.AccountId == query.AccountId.Value);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(r => r.Name.ToLower().Contains(keyword));
        }

        var pagedList = await queryable
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.CreationTime)
            .Select(r => new BankRuleDto
            {
                Id = r.Id,
                Name = r.Name,
                Priority = r.Priority,
                IsEnabled = r.IsEnabled,
                AccountId = r.AccountId,
                Direction = r.Direction,
                MatchMode = r.MatchMode,
                DocType = r.DocType,
                CounterAccountId = r.CounterAccountId,
                PartyId = r.PartyId,
                PaymentMethod = r.PaymentMethod,
                AutoApply = r.AutoApply,
                CreationTime = r.CreationTime,
                Conditions = r.Conditions.OrderBy(c => c.LineNumber).Select(c => new BankRuleConditionDto
                {
                    Id = c.Id,
                    LineNumber = c.LineNumber,
                    Field = c.Field,
                    Operator = c.Operator,
                    Value = c.Value
                }).ToList()
            })
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        await FillAccountNamesAsync(pagedList.Items, cancellationToken);
        return Ok(pagedList);
    }

    public async Task<Result<BankRuleDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await _repository.AsNoTracking()
            .Include(r => r.Conditions)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (rule == null)
            return Fail<BankRuleDto>("Bank rule not found.", 404);

        var dto = ToDto(rule);
        await FillAccountNamesAsync([dto], cancellationToken);
        return Ok(dto);
    }

    public async Task<Result<BankRuleDto>> CreateAsync(CreateBankRuleDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var rule = new BankRule();
        var applyResult = await ApplyAsync(rule, input, isCreate: true, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<BankRuleDto>(applyResult.Message ?? "Invalid rule.", applyResult.Code ?? 400);

        await _repository.InsertAsync(rule, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return await GetAsync(rule.Id, cancellationToken);
    }

    public async Task<Result<BankRuleDto>> UpdateAsync(Guid id, CreateBankRuleDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var rule = await _repository.AsQueryable(true)
            .Include(r => r.Conditions)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (rule == null)
            return Fail<BankRuleDto>("Bank rule not found.", 404);

        var oldConditions = rule.Conditions.ToList();
        var applyResult = await ApplyAsync(rule, input, isCreate: false, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<BankRuleDto>(applyResult.Message ?? "Invalid rule.", applyResult.Code ?? 400);

        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            if (oldConditions.Count > 0)
                await _conditionRepository.DeleteManyAsync(oldConditions, ct);
            await _repository.UpdateAsync(rule, ct);
        }, cancellationToken);

        return await GetAsync(rule.Id, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await _repository.AsQueryable(true)
            .Include(r => r.Conditions)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (rule == null)
            return Fail("Bank rule not found.", 404);

        // 规则是运维配置而不是账本记录：删掉它不会改写任何已经发生的事，
        // 已由它入账的单据照旧存在（流水上留的是当时的建议，不是外键）。
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            if (rule.Conditions.Count > 0)
                await _conditionRepository.DeleteManyAsync(rule.Conditions.ToList(), ct);
            await _repository.DeleteAsync(rule, ct);
        }, cancellationToken);

        return Ok();
    }

    public async Task<Result> ReorderAsync(ReorderBankRulesDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (input.RuleIds == null || input.RuleIds.Count == 0)
            return Fail("At least one rule id is required.", 400);

        var ids = input.RuleIds.Distinct().ToList();
        if (ids.Count != input.RuleIds.Count)
            return Fail("The same rule appears more than once in the requested order.", 400);

        // ★必须在**全量**顺序上重排，不能只给提交的这几条编 1..N。
        // 提交的往往是当前这一页（界面上的上移/下移只看得见一页），把它们编成 1..N
        // 会与页外规则的既有号相撞；而求值器是"按 Priority 再按创建时间、首个命中者胜"，
        // 打平之后谁先谁后由创建时间决定 —— 一条规则就此悄悄抢走另一条的流水，
        // 而操作员看到的是自己刚拖出来的顺序。
        var all = await _repository.AsQueryable(true)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.CreationTime)
            .ToListAsync(cancellationToken);

        var byId = all.ToDictionary(r => r.Id);
        if (ids.Any(id => !byId.ContainsKey(id)))
            return Fail("One or more rules no longer exist. Reload and retry.", 404);

        // 提交集合当前占据的那些位置，按提交顺序填回去；没提交的规则原地不动。
        var submitted = ids.ToHashSet();
        var slots = new List<int>();
        for (var i = 0; i < all.Count; i++)
        {
            if (submitted.Contains(all[i].Id))
                slots.Add(i);
        }

        var ordered = new List<BankRule>(all);
        for (var i = 0; i < slots.Count; i++)
            ordered[slots[i]] = byId[ids[i]];

        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            for (var i = 0; i < ordered.Count; i++)
            {
                var rule = ordered[i];
                if (rule.Priority == i + 1)
                    continue;
                rule.Priority = i + 1;
                await _repository.UpdateAsync(rule, ct);
            }
        }, cancellationToken);

        return Ok();
    }

    public async Task<Result<BankRuleTestResultDto>> TestAsync(Guid id, TestBankRuleDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var rule = await _repository.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule == null)
            return Fail<BankRuleTestResultDto>("Bank rule not found.", 404);

        var sample = Math.Clamp(input.Sample <= 0 ? 20 : input.Sample, 1, 200);

        var queryable = _txnRepository.AsNoTracking().Where(t => t.Status == BankTransactionStatus.Pending);
        if (input.AccountId.HasValue)
            queryable = queryable.Where(t => t.AccountId == input.AccountId.Value);
        else if (rule.AccountId.HasValue)
            queryable = queryable.Where(t => t.AccountId == rule.AccountId.Value);

        var pending = await queryable.OrderByDescending(t => t.TxnDate).ToListAsync(cancellationToken);

        // ★经完整求值器跑，而不是只拿这一条规则去比对：操作员真正需要知道的
        // 不是"我这条规则能匹配什么"，而是"这些流水最终归谁"——首个命中者胜
        // 意味着一条更高优先级的规则可能把它们全都抢走。
        var matches = await _evaluator.EvaluateManyAsync(pending, cancellationToken);

        var rows = new List<BankRuleTestRowDto>();
        var matched = 0;
        foreach (var txn in pending)
        {
            if (!matches.TryGetValue(txn.Id, out var match))
                continue;
            matched++;
            if (rows.Count >= sample)
                continue;

            rows.Add(new BankRuleTestRowDto
            {
                TransactionId = txn.Id,
                TxnDate = txn.TxnDate,
                Amount = txn.Amount,
                Description = txn.Description,
                Payee = txn.Payee,
                WinningRuleId = match.RuleId,
                WinningRuleName = match.RuleName
            });
        }

        return Ok(new BankRuleTestResultDto { Evaluated = pending.Count, Matched = matched, Rows = rows });
    }

    private async Task<Result> ApplyAsync(BankRule rule, CreateBankRuleDto input, bool isCreate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail("A rule name is required.", 400);

        if (input.Conditions == null)
            return Fail("Conditions are required (send an empty list for an account/direction-only rule).", 400);

        for (var i = 0; i < input.Conditions.Count; i++)
        {
            var c = input.Conditions[i];
            if (string.IsNullOrWhiteSpace(c.Value))
                return Fail($"Condition {i + 1}: a value is required.", 400);

            // 运算符与字段必须配套：把 "contains" 用在金额上、"greater than" 用在
            // 摘要上，规则会静默地永不命中——那比直接拒绝难查得多。
            var numeric = c.Operator is BankRuleOperator.GreaterThan or BankRuleOperator.LessThan;
            if (c.Field == BankRuleField.Amount)
            {
                if (c.Operator is not (BankRuleOperator.Equals or BankRuleOperator.GreaterThan or BankRuleOperator.LessThan))
                    return Fail($"Condition {i + 1}: an amount can only be compared with equals, greater than or less than.", 400);
                if (!decimal.TryParse(c.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    return Fail($"Condition {i + 1}: '{c.Value}' is not a number.", 400);
            }
            else if (numeric)
            {
                return Fail($"Condition {i + 1}: greater than / less than only apply to the amount.", 400);
            }
        }

        if (input.AccountId.HasValue && !await _accountRepository.AnyAsync(a => a.Id == input.AccountId.Value, cancellationToken))
            return Fail("Bank account not found.", 404);
        if (input.CounterAccountId.HasValue && !await _accountRepository.AnyAsync(a => a.Id == input.CounterAccountId.Value, cancellationToken))
            return Fail("Counter account not found.", 404);

        // 自动入账的规则必须自己说得清钱记到哪儿：缺科目就自动过账，等于让
        // 系统替人猜一个科目。
        if (input.AutoApply && input.DocType != BankFeedDocType.PaymentEntry && !input.CounterAccountId.HasValue)
            return Fail("A rule that posts automatically must name the account to post to.", 400);
        if (input.AutoApply && input.DocType == BankFeedDocType.PaymentEntry && !input.PartyId.HasValue)
            return Fail("A rule that posts a payment automatically must name the party.", 400);

        rule.Name = input.Name.Trim();
        rule.IsEnabled = input.IsEnabled;
        rule.AccountId = input.AccountId;
        rule.Direction = input.Direction;
        rule.MatchMode = input.MatchMode;
        rule.DocType = input.DocType;
        rule.CounterAccountId = input.CounterAccountId;
        rule.PartyId = input.PartyId;
        rule.PaymentMethod = string.IsNullOrWhiteSpace(input.PaymentMethod) ? null : input.PaymentMethod.Trim();
        rule.AutoApply = input.AutoApply;

        if (input.Priority.HasValue)
            rule.Priority = input.Priority.Value;
        else if (isCreate)
            rule.Priority = await NextPriorityAsync(cancellationToken);

        rule.Conditions.Clear();
        var lineNo = 1;
        foreach (var c in input.Conditions)
        {
            rule.Conditions.Add(new BankRuleCondition
            {
                BankRuleId = rule.Id,
                LineNumber = lineNo++,
                Field = c.Field,
                Operator = c.Operator,
                Value = c.Value.Trim()
            });
        }

        return Ok();
    }

    /// <summary>新规则排到末尾：它不该悄悄抢走既有规则的流水。</summary>
    private async Task<int> NextPriorityAsync(CancellationToken cancellationToken)
    {
        // 取最大值交给数据库（投影成可空以便空表返回 null 而不是抛）
        var max = await _repository.AsNoTracking().Select(r => (int?)r.Priority).MaxAsync(cancellationToken);
        return (max ?? 0) + 1;
    }

    private async Task FillAccountNamesAsync(IEnumerable<BankRuleDto> source, CancellationToken cancellationToken)
    {
        var dtos = source as IReadOnlyCollection<BankRuleDto> ?? source.ToList();
        var ids = dtos.SelectMany(d => new[] { d.AccountId, d.CounterAccountId })
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return;

        var names = await _accountRepository.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.Code, a.Name })
            .ToDictionaryAsync(a => a.Id, a => $"{a.Code} {a.Name}", cancellationToken);

        foreach (var dto in dtos)
        {
            if (dto.AccountId.HasValue && names.TryGetValue(dto.AccountId.Value, out var accountName))
                dto.AccountName = accountName;
            if (dto.CounterAccountId.HasValue && names.TryGetValue(dto.CounterAccountId.Value, out var counterName))
                dto.CounterAccountName = counterName;
        }
    }

    private static BankRuleDto ToDto(BankRule rule)
    {
        var dto = rule.MapTo<BankRuleDto>();
        dto.Conditions = rule.Conditions.OrderBy(c => c.LineNumber).Select(c => c.MapTo<BankRuleConditionDto>()).ToList();
        return dto;
    }
}
