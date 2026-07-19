namespace Tnzi.Finance.Services;

/// <summary>
/// 目录项服务
/// </summary>
public class ItemService : ApplicationService, IItemService
{
    private readonly IRepository<Item, Guid> _itemRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;

    public ItemService(
        IServiceProvider serviceProvider,
        IRepository<Item, Guid> itemRepository,
        IReadOnlyRepository<Account, Guid> accountRepository)
        : base(serviceProvider)
    {
        _itemRepository = Check.NotNull(itemRepository);
        _accountRepository = Check.NotNull(accountRepository);
    }

    public async Task<Result<IPagedList<ItemDto>>> GetPagedAsync(ItemQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _itemRepository.AsNoTracking()
            .Filter(query)
            .OrderBy(i => i.Name)
            .ProjectTo<Item, ItemDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<ItemDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _itemRepository.GetAsync(id, cancellationToken);
        if (item == null)
            return Fail<ItemDto>("Item not found.", 404);

        return Ok(item.MapTo<ItemDto>());
    }

    public async Task<Result<ItemDto>> CreateAsync(CreateItemDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = await ValidateAsync(input, excludeId: null, cancellationToken);
        if (!validation.Succeeded)
            return Fail<ItemDto>(validation.Message ?? "Invalid item.", validation.Code ?? 400);

        var item = new Item();
        Apply(item, input, isActive: true);

        try
        {
            await _itemRepository.InsertAsync(item, cancellationToken);
            await _itemRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<ItemDto>($"Item code '{item.Code}' already exists.", 409);
        }

        return Ok(item.MapTo<ItemDto>());
    }

    public async Task<Result<ItemDto>> UpdateAsync(Guid id, UpdateItemDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var item = await _itemRepository.GetAsync(id, cancellationToken);
        if (item == null)
            return Fail<ItemDto>("Item not found.", 404);

        var validation = await ValidateAsync(input, excludeId: id, cancellationToken);
        if (!validation.Succeeded)
            return Fail<ItemDto>(validation.Message ?? "Invalid item.", validation.Code ?? 400);

        Apply(item, input, input.IsActive);

        try
        {
            await _itemRepository.UpdateAsync(item, cancellationToken);
            await _itemRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<ItemDto>($"Item code '{item.Code}' already exists.", 409);
        }

        return Ok(item.MapTo<ItemDto>());
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _itemRepository.GetAsync(id, cancellationToken);
        if (item == null)
            return Fail("Item not found.", 404);

        // 被单据行引用时拒绝删除：Item 软删后会被全局过滤器隐藏，而其已过账的发票/账单/贷项行仍留在子账，
        // 导致行项名称丢失、报表回退显示原始 GUID。引导用 IsActive=false 停用而非删除（对齐 Customer/Vendor/Tax 守卫）。
        // 引用仓储在删除冷路径按需解析（避免把 InvoiceLine/BillLine/CreditMemoLine 依赖强加到共享服务图上的
        // Payroll 最小测试基类；那里从不调用本删除路径）。
        var invoiceLineRepository = GetRequiredService<IReadOnlyRepository<InvoiceLine, Guid>>();
        var billLineRepository = GetRequiredService<IReadOnlyRepository<BillLine, Guid>>();
        var creditMemoLineRepository = GetRequiredService<IReadOnlyRepository<CreditMemoLine, Guid>>();
        var referenced =
            await invoiceLineRepository.AnyAsync(l => l.ItemId == id, cancellationToken) ||
            await billLineRepository.AnyAsync(l => l.ItemId == id, cancellationToken) ||
            await creditMemoLineRepository.AnyAsync(l => l.ItemId == id, cancellationToken);
        if (referenced)
            return Fail("Cannot delete an item referenced by invoice, bill, or credit-memo lines. Deactivate it instead.", 409);

        await _itemRepository.DeleteAsync(item, cancellationToken);
        return Ok();
    }

    private async Task<Result> ValidateAsync(CreateItemDto input, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail("Item name is required.");

        if (input.SalesPrice is < 0 || input.PurchasePrice is < 0)
            return Fail("Prices must not be negative.");

        var code = input.Code?.Trim();
        if (!string.IsNullOrEmpty(code) &&
            await _itemRepository.AnyAsync(i => i.Code == code && i.Id != excludeId, cancellationToken))
        {
            return Fail($"Item code '{code}' already exists.", 409);
        }

        // 默认科目须存在、可过账叶子，且类型匹配槽位：收入科目 RootType=Income、费用科目 RootType=Expense。
        // 否则一张无行覆盖的销售/采购单会静默把收入/成本过到资产负债类科目（凭证仍借贷平、TB 仍归零，
        // 但 P&L 收入少计、资产多计，无任何报错）——QuickBooks/Xero/Odoo 均对 item 默认科目做同类限制。
        if (input.IncomeAccountId is { } incomeId)
        {
            var ok = await _accountRepository.AnyAsync(
                a => a.Id == incomeId && !a.IsGroup && a.RootType == AccountRootType.Income, cancellationToken);
            if (!ok)
                return Fail("The default income account must be a postable (non-group) Income account.");
        }
        if (input.ExpenseAccountId is { } expenseId)
        {
            var ok = await _accountRepository.AnyAsync(
                a => a.Id == expenseId && !a.IsGroup && a.RootType == AccountRootType.Expense, cancellationToken);
            if (!ok)
                return Fail("The default expense account must be a postable (non-group) Expense account.");
        }

        return Ok();
    }

    private static void Apply(Item item, CreateItemDto input, bool isActive)
    {
        item.Code = string.IsNullOrWhiteSpace(input.Code) ? null : input.Code.Trim();
        item.Name = input.Name.Trim();
        item.Type = input.Type;
        item.Description = input.Description;
        item.SalesPrice = input.SalesPrice;
        item.PurchasePrice = input.PurchasePrice;
        item.IncomeAccountId = input.IncomeAccountId;
        item.ExpenseAccountId = input.ExpenseAccountId;
        item.DefaultTaxCodeId = input.DefaultTaxCodeId;
        item.IsActive = isActive;
    }
}
