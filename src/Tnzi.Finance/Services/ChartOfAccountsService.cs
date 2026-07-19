namespace Tnzi.Finance.Services;

/// <summary>
/// 科目表服务
/// </summary>
public class ChartOfAccountsService : ApplicationService, IChartOfAccountsService
{
    private const int MaxTreeDepth = 64;

    /// <summary>单次余额查询的科目上限（参数化 IN 列表有数据库上限，SQL Server 为 2100）</summary>
    private const int MaxBalanceAccounts = 500;

    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly IReadOnlyRepository<JournalLine, Guid> _lineRepository;
    private readonly BalanceSummaryReader _balanceReader;

    public ChartOfAccountsService(
        IServiceProvider serviceProvider,
        IRepository<Account, Guid> accountRepository,
        IReadOnlyRepository<JournalLine, Guid> lineRepository,
        BalanceSummaryReader balanceReader)
        : base(serviceProvider)
    {
        _accountRepository = Check.NotNull(accountRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _balanceReader = Check.NotNull(balanceReader);
    }

    public async Task<Result<AccountDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetAsync(id, cancellationToken);
        if (account == null)
            return Fail<AccountDto>("Account not found.", 404);

        return Ok(account.MapTo<AccountDto>());
    }

    public async Task<Result<IPagedList<AccountDto>>> GetListAsync(AccountQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _accountRepository.AsNoTracking()
            .Filter(query)
            .OrderBy(a => a.Code)
            .ProjectTo<Account, AccountDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<List<AccountTreeDto>>> GetTreeAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var accounts = await _accountRepository.AsNoTracking()
            .Where(a => includeInactive || a.IsActive)
            .OrderBy(a => a.Code)
            .ToListAsync(cancellationToken);

        var nodes = accounts.Select(a => a.MapTo<AccountTreeDto>()).ToList();
        var roots = TreeHelper.ToTree(nodes, n => n.Id, n => n.ParentId, (p, c) => p.Children.Add(c));

        return Ok(roots.ToList());
    }

    public async Task<Result<AccountDto>> CreateAsync(CreateAccountDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var code = input.Code?.Trim();
        var validation = await ValidateAccountCoreAsync(code, input.Name, input.IsGroup, input.SystemRole, excludeId: null, cancellationToken);
        if (!validation.Succeeded)
            return Fail<AccountDto>(validation.Message ?? "Invalid account.", validation.Code ?? 400);

        if (input.ParentId.HasValue)
        {
            var parentResult = await ValidateParentAsync(input.ParentId.Value, input.RootType, cancellationToken);
            if (!parentResult.Succeeded)
                return Fail<AccountDto>(parentResult.Message ?? "Invalid parent account.", parentResult.Code ?? 400);
        }

        var account = new Account
        {
            Code = code!,
            Name = input.Name.Trim(),
            Description = input.Description,
            RootType = input.RootType,
            SubType = input.SubType,
            ParentId = input.ParentId,
            IsGroup = input.IsGroup,
            Currency = input.Currency?.Trim().ToUpperInvariant(),
            SystemRole = input.SystemRole,
            CashFlowActivity = input.CashFlowActivity,
            IsActive = true
        };

        try
        {
            await _accountRepository.InsertAsync(account, cancellationToken);
            await _accountRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 预检与写入之间的并发窗口由唯一索引兜底，翻译为与预检一致的 409
            await _accountRepository.DeleteAsync(account, cancellationToken);
            return Fail<AccountDto>("Account code or system role already exists.", 409);
        }

        return Ok(account.MapTo<AccountDto>());
    }

    public async Task<Result<AccountDto>> UpdateAsync(Guid id, UpdateAccountDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var account = await _accountRepository.GetAsync(id, cancellationToken);
        if (account == null)
            return Fail<AccountDto>("Account not found.", 404);

        var code = input.Code?.Trim();
        var validation = await ValidateAccountCoreAsync(code, input.Name, account.IsGroup, input.SystemRole, excludeId: id, cancellationToken);
        if (!validation.Succeeded)
            return Fail<AccountDto>(validation.Message ?? "Invalid account.", validation.Code ?? 400);

        if (input.ParentId.HasValue && input.ParentId.Value != account.ParentId)
        {
            if (input.ParentId.Value == id)
                return Fail<AccountDto>("An account cannot be its own parent.");

            var parentResult = await ValidateParentAsync(input.ParentId.Value, account.RootType, cancellationToken);
            if (!parentResult.Succeeded)
                return Fail<AccountDto>(parentResult.Message ?? "Invalid parent account.", parentResult.Code ?? 400);

            if (await WouldCreateCycleAsync(id, input.ParentId.Value, cancellationToken))
                return Fail<AccountDto>("Re-parenting would create a cycle in the account tree.");
        }

        var newCurrency = input.Currency?.Trim().ToUpperInvariant();
        if (!string.Equals(account.Currency, newCurrency, StringComparison.Ordinal) &&
            await _lineRepository.AnyAsync(l => l.AccountId == id, cancellationToken))
        {
            return Fail<AccountDto>("Cannot change the currency of an account that has journal lines.", 409);
        }

        // 过账管线按角色解析科目且要求启用（FinanceDocumentHelper.ResolveSystemAccountAsync /
        // LedgerPostingEngine 的 RoundingDifference 解析），停用 = 对应过账永久 400，
        // 且尚未过账的种子科目（如 1130 Undeposited Funds）第一天就能被停掉。
        // 判据是更新后的结果状态：同一次更新里清掉角色再停用是允许的，故不挡角色迁移
        if (input.SystemRole.HasValue && !input.IsActive)
        {
            return Fail<AccountDto>(
                $"Cannot deactivate the account holding the {input.SystemRole.Value} system role: postings resolve it by role and require it to be active. " +
                "Clear the system role first, or move it to another account.", 409);
        }

        account.Code = code!;
        account.Name = input.Name.Trim();
        account.Description = input.Description;
        account.SubType = input.SubType;
        account.ParentId = input.ParentId;
        account.Currency = newCurrency;
        account.SystemRole = input.SystemRole;
        account.CashFlowActivity = input.CashFlowActivity;
        account.IsActive = input.IsActive;

        try
        {
            await _accountRepository.UpdateAsync(account, cancellationToken);
            await _accountRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<AccountDto>("Account code or system role already exists.", 409);
        }

        return Ok(account.MapTo<AccountDto>());
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetAsync(id, cancellationToken);
        if (account == null)
            return Fail("Account not found.", 404);

        // 角色科目即使一条分录都没有也不可删：过账按角色解析（而非按 Id 引用），
        // 删掉 = 对应过账永久 400。先清角色（或迁到别的科目）再删
        if (account.SystemRole.HasValue)
        {
            return Fail(
                $"Cannot delete the account holding the {account.SystemRole.Value} system role: postings resolve it by role. " +
                "Clear the system role first, or move it to another account.", 409);
        }

        if (await _accountRepository.AnyAsync(a => a.ParentId == id, cancellationToken))
            return Fail("Cannot delete an account that has child accounts.", 409);

        if (await _lineRepository.AnyAsync(l => l.AccountId == id, cancellationToken))
            return Fail("Cannot delete an account that has journal lines.", 409);

        await _accountRepository.DeleteAsync(account, cancellationToken);
        return Ok();
    }

    public async Task<Result<List<AccountBalanceDto>>> GetBalancesAsync(
        IEnumerable<Guid> accountIds, DateTime asOf, CancellationToken cancellationToken = default)
    {
        Check.NotNull(accountIds);

        var ids = accountIds.Distinct().ToList();
        if (ids.Count == 0)
            return Ok(new List<AccountBalanceDto>());
        if (ids.Count > MaxBalanceAccounts)
            return Fail<List<AccountBalanceDto>>($"Cannot read more than {MaxBalanceAccounts} account balances in one request. Split the request into batches.", 400);

        // as-of 边界与报表一致（PostingDate < 次日）——未来日期的过账不进当日余额，
        // 科目表现金余额与同日资产负债表现金恒等
        var asOfDate = asOf.ToUtcDate();
        var sums = await _balanceReader.SumCumulativeByAccountsAsync(ids, asOfDate.AddDays(1), cancellationToken);

        var balances = ids.Select(accountId =>
        {
            sums.TryGetValue(accountId, out var sum);
            return new AccountBalanceDto
            {
                AccountId = accountId,
                AsOf = asOfDate,
                Debit = sum.Debit,
                Credit = sum.Credit,
                Balance = sum.Debit - sum.Credit
            };
        }).ToList();

        return Ok(balances);
    }

    public async Task<Result<int>> SeedDefaultAsync(CancellationToken cancellationToken = default)
    {
        if (await _accountRepository.AnyAsync(cancellationToken: cancellationToken))
            return Fail<int>("Chart of accounts is not empty. The default template can only be seeded into an empty chart.", 409);

        var byCode = new Dictionary<string, Account>();
        var accounts = new List<Account>();

        foreach (var template in DefaultChartOfAccounts.Template)
        {
            Guid? parentId = null;
            if (template.ParentCode != null)
            {
                if (!byCode.TryGetValue(template.ParentCode, out var parent))
                    throw new InvalidOperationException($"Default chart of accounts template is invalid: parent '{template.ParentCode}' must precede '{template.Code}'.");
                parentId = parent.Id;
            }

            var account = new Account
            {
                Id = SequentialGuid.NewGuid(),
                Code = template.Code,
                Name = template.Name,
                RootType = template.RootType,
                SubType = template.SubType,
                ParentId = parentId,
                IsGroup = template.IsGroup,
                SystemRole = template.SystemRole,
                CashFlowActivity = template.CashFlowActivity,
                IsActive = true
            };

            byCode.Add(template.Code, account);
            accounts.Add(account);
        }

        try
        {
            await _accountRepository.InsertManyAsync(accounts, cancellationToken);
            await _accountRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 并发播种：另一请求已抢先写入，与预检语义保持一致返回 409
            await _accountRepository.DeleteManyAsync(accounts, cancellationToken);
            return Fail<int>("Chart of accounts is not empty. The default template can only be seeded into an empty chart.", 409);
        }

        LogInformation("Seeded default chart of accounts with {Count} accounts.", accounts.Count);
        return Ok(accounts.Count);
    }

    public Task<Account?> FindByRoleAsync(AccountSystemRole role, CancellationToken cancellationToken = default)
        => _accountRepository.FirstOrDefaultAsync(a => a.SystemRole == role, cancellationToken);

    public Task<Account?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(code);
        return _accountRepository.FirstOrDefaultAsync(a => a.Code == code, cancellationToken);
    }

    /// <summary>
    /// 创建/更新共用的字段与唯一性校验（excludeId 排除自身）
    /// </summary>
    private async Task<Result> ValidateAccountCoreAsync(
        string? code, string? name, bool isGroup, AccountSystemRole? systemRole, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Fail("Account code is required.");
        if (string.IsNullOrWhiteSpace(name))
            return Fail("Account name is required.");
        if (isGroup && systemRole.HasValue)
            return Fail("A group account cannot carry a system role.");

        if (await _accountRepository.AnyAsync(
                a => a.Code == code && (excludeId == null || a.Id != excludeId.Value), cancellationToken))
        {
            return Fail($"Account code '{code}' already exists.", 409);
        }

        if (systemRole.HasValue && await _accountRepository.AnyAsync(
                a => a.SystemRole == systemRole.Value && (excludeId == null || a.Id != excludeId.Value), cancellationToken))
        {
            return Fail($"System role '{systemRole.Value}' is already assigned to another account.", 409);
        }

        return Ok();
    }

    private async Task<Result> ValidateParentAsync(Guid parentId, AccountRootType rootType, CancellationToken cancellationToken)
    {
        var parent = await _accountRepository.GetAsync(parentId, cancellationToken);
        if (parent == null)
            return Fail("Parent account not found.");
        if (!parent.IsGroup)
            return Fail("Parent account must be a group account.");
        if (parent.RootType != rootType)
            return Fail("Parent account must have the same root type.");

        return Ok();
    }

    private async Task<bool> WouldCreateCycleAsync(Guid accountId, Guid newParentId, CancellationToken cancellationToken)
    {
        Guid? currentId = newParentId;
        for (var depth = 0; currentId.HasValue && depth < MaxTreeDepth; depth++)
        {
            if (currentId.Value == accountId)
                return true;

            currentId = await _accountRepository.AsNoTracking()
                .Where(a => a.Id == currentId.Value)
                .Select(a => a.ParentId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return false;
    }
}
