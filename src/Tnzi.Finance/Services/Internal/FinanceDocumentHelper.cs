namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 业务单据服务共享工具（科目角色解析 / 可过账校验 / 目录项批量加载 / 税额计算包装）
/// </summary>
public sealed class FinanceDocumentHelper
{
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly IReadOnlyRepository<Item, Guid> _itemRepository;
    private readonly ITaxCalculator _taxCalculator;
    private readonly FinanceOptions _options;

    public FinanceDocumentHelper(
        IReadOnlyRepository<Account, Guid> accountRepository,
        IReadOnlyRepository<Item, Guid> itemRepository,
        ITaxCalculator taxCalculator,
        IOptionsSnapshot<FinanceOptions> options)
    {
        _accountRepository = Check.NotNull(accountRepository);
        _itemRepository = Check.NotNull(itemRepository);
        _taxCalculator = Check.NotNull(taxCalculator);
        _options = Check.NotNull(options).Value;
    }

    /// <summary>本位币舍入小数位</summary>
    public int Decimals => _options.BaseCurrencyDecimals;

    /// <summary>金额舍入（AwayFromZero）</summary>
    public decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);

    /// <summary>规范化币种（null/空白 → 本位币）</summary>
    public string NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency)
            ? _options.BaseCurrency.Trim().ToUpperInvariant()
            : currency.Trim().ToUpperInvariant();

    /// <summary>按系统角色解析可过账科目</summary>
    public async Task<Result<Account>> ResolveSystemAccountAsync(AccountSystemRole role, CancellationToken cancellationToken)
    {
        var account = await _accountRepository.FirstOrDefaultAsync(
            a => a.SystemRole == role && a.IsActive && !a.IsGroup, cancellationToken);

        return account == null
            ? Result<Account>.Failure($"Posting requires an active account with the {role} system role. Assign the role in the chart of accounts.", 400)
            : Result<Account>.Success(account);
    }

    /// <summary>校验科目存在且可过账（非分组、启用）</summary>
    public async Task<Result<Account>> GetPostableAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _accountRepository.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        return CheckPostable(account);
    }

    /// <summary>
    /// 判定一个已加载的科目可过账（非分组、启用）；<paramref name="account"/> 为 null 表示不存在。
    /// 与 <see cref="GetPostableAccountAsync"/> 同一份口径与措辞，供先批量加载再逐个判定的调用方使用。
    /// </summary>
    public static Result<Account> CheckPostable(Account? account)
    {
        if (account == null)
            return Result<Account>.Failure("Account not found.", 404);
        if (account.IsGroup)
            return Result<Account>.Failure($"Cannot post to group account '{account.Code}'.", 400);
        if (!account.IsActive)
            return Result<Account>.Failure($"Account '{account.Code}' is inactive.", 400);

        return Result<Account>.Success(account);
    }

    /// <summary>一条 IN 查询加载一批科目（重复 Id 只查一次；不存在的 Id 不在字典里）</summary>
    public async Task<Dictionary<Guid, Account>> LoadAccountsAsync(IEnumerable<Guid> accountIds, CancellationToken cancellationToken)
    {
        var ids = accountIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, Account>();

        return await _accountRepository.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);
    }

    /// <summary>
    /// 按单据行序逐行判定入账科目可过账，科目只发一条 IN 查询；返回 行 Id → 科目 Id。
    /// </summary>
    /// <remarks>
    /// 过账引擎随后还会批量校验一遍科目，但它报的是<b>凭证</b>行号（第 1 行是应收 / 应付），
    /// 与单据上的行号对不上，所以单据这一步不能省；它只是不该每行各查一次：
    /// 一张几百行的单据（行数上限 <see cref="FinanceOptions.MaxLinesPerEntry"/>）会在过账前先跑几百个顺序往返。
    /// </remarks>
    /// <param name="lines">按单据行序：行 Id、单据行号、解析出的科目（null = 行上与目录项都没给）</param>
    /// <param name="accountKind">缺科目时错误消息里的科目种类（<c>income</c> / <c>expense</c>）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<Result<Dictionary<Guid, Guid>>> ResolvePostableLineAccountsAsync(
        IReadOnlyList<(Guid LineId, int LineNumber, Guid? AccountId)> lines, string accountKind, CancellationToken cancellationToken)
    {
        Check.NotNull(lines);
        Check.NotNullOrWhiteSpace(accountKind);

        var accounts = await LoadAccountsAsync(
            lines.Where(l => l.AccountId.HasValue).Select(l => l.AccountId!.Value), cancellationToken);

        var resolved = new Dictionary<Guid, Guid>();
        foreach (var (lineId, lineNumber, accountId) in lines)
        {
            if (!accountId.HasValue)
            {
                return Result<Dictionary<Guid, Guid>>.Failure(
                    $"Line {lineNumber}: no {accountKind} account (set the line account or the item default).", 400);
            }

            var accountResult = CheckPostable(accounts.GetValueOrDefault(accountId.Value));
            if (!accountResult.Succeeded)
                return Result<Dictionary<Guid, Guid>>.Failure($"Line {lineNumber}: {accountResult.Message}", accountResult.Code ?? 400);

            resolved[lineId] = accountId.Value;
        }

        return Result<Dictionary<Guid, Guid>>.Success(resolved);
    }

    /// <summary>
    /// 资金侧科目（收款存入 / 付款付出 / 费用付出）的准入：不得是 A/R、A/P 控制科目，两个方向都拒。
    /// </summary>
    /// <remarks>
    /// 控制科目的余额要等于账龄合计（子账↔总账 tie-out）。从资金侧直接借贷它，会生成一笔没有任何往来单据的
    /// 控制科目变动：收款「存进」应付 = 借 AP（无往来方）/ 贷 AR，AR 子账照样一致而 AP 从此对不上账龄；
    /// 每张凭证自身平衡、试算平衡为零，没有任何报表会报警。判据是角色而不是「本方向解析出的那个科目」，
    /// 后者只挡住一半。存款单「其它款项」行有同源守卫（<c>DepositService.ValidateOtherFundsAccountAsync</c>）。
    /// </remarks>
    /// <param name="account">已通过可过账校验的科目</param>
    /// <param name="label">出现在错误消息开头的字段名（如 <c>Deposit account</c>）</param>
    public static Result RejectControlAccount(Account account, string label)
    {
        Check.NotNull(account);

        return account.SystemRole is AccountSystemRole.AccountsReceivable or AccountSystemRole.AccountsPayable
            ? Result.Failure(
                $"{label}: '{account.Code}' is the {account.SystemRole} control account and cannot hold the money. "
                + "Choose a bank, cash or clearing account; amounts owed are settled by applying payments to invoices or bills.",
                400)
            : Result.Success();
    }

    /// <summary>
    /// 校验科目为可过账的资金叶子（CashFlowActivity = CashEquivalent），并可选校验币种兼容。
    /// P3a 银行域（划转/对账）的统一资格判据——资金科目定义演进时只改这里
    /// </summary>
    /// <param name="accountId">科目ID</param>
    /// <param name="requiredCurrency">要求兼容的币种（科目限定币种时须相等）；null 不校验币种</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<Result<Account>> GetFundsAccountAsync(Guid accountId, string? requiredCurrency, CancellationToken cancellationToken)
    {
        var accountResult = await GetPostableAccountAsync(accountId, cancellationToken);
        if (!accountResult.Succeeded)
            return accountResult;

        var account = accountResult.Data!;
        if (account.CashFlowActivity != CashFlowActivity.CashEquivalent)
            return Result<Account>.Failure($"Account '{account.Code}' is not a funds account. Mark it as CashEquivalent in the chart of accounts.", 400);

        if (requiredCurrency != null && !string.IsNullOrEmpty(account.Currency) &&
            !string.Equals(account.Currency, requiredCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return Result<Account>.Failure($"Account '{account.Code}' is restricted to {account.Currency} and cannot be used with {requiredCurrency}.", 400);
        }

        return Result<Account>.Success(account);
    }

    /// <summary>批量加载目录项（全部须存在且启用）</summary>
    public async Task<Result<Dictionary<Guid, Item>>> LoadItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
            return Result<Dictionary<Guid, Item>>.Success(new Dictionary<Guid, Item>());

        var items = await _itemRepository.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        foreach (var id in itemIds)
        {
            if (!items.TryGetValue(id, out var item))
                return Result<Dictionary<Guid, Item>>.Failure($"Item '{id}' not found.", 404);
            if (!item.IsActive)
                return Result<Dictionary<Guid, Item>>.Failure($"Item '{item.Name}' is inactive.", 400);
        }

        return Result<Dictionary<Guid, Item>>.Success(items);
    }

    /// <summary>计算税额（包装 <see cref="ITaxCalculator"/> 的业务异常为 Result）</summary>
    public async Task<Result<TaxCalculationResult>> CalculateTaxAsync(List<TaxCalculationLine> lines, CancellationToken cancellationToken, bool isPurchase = false)
    {
        try
        {
            var result = await _taxCalculator.CalculateAsync(new TaxCalculationRequest { Lines = lines, IsPurchase = isPurchase }, cancellationToken);
            return Result<TaxCalculationResult>.Success(result);
        }
        catch (BusinessException ex)
        {
            return Result<TaxCalculationResult>.Failure(ex.Message, ex.HttpStatusCode);
        }
    }
}
