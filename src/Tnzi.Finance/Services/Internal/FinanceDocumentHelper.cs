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
        IOptions<FinanceOptions> options)
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
        if (account == null)
            return Result<Account>.Failure("Account not found.", 404);
        if (account.IsGroup)
            return Result<Account>.Failure($"Cannot post to group account '{account.Code}'.", 400);
        if (!account.IsActive)
            return Result<Account>.Failure($"Account '{account.Code}' is inactive.", 400);

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
    public async Task<Result<TaxCalculationResult>> CalculateTaxAsync(List<TaxCalculationLine> lines, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _taxCalculator.CalculateAsync(new TaxCalculationRequest { Lines = lines }, cancellationToken);
            return Result<TaxCalculationResult>.Success(result);
        }
        catch (BusinessException ex)
        {
            return Result<TaxCalculationResult>.Failure(ex.Message, ex.HttpStatusCode);
        }
    }
}
