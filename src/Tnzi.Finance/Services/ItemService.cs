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

        // P2b：被单据行引用时拒绝删除
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

        // 默认科目须存在且为可过账叶子科目
        foreach (var accountId in new[] { input.IncomeAccountId, input.ExpenseAccountId })
        {
            if (!accountId.HasValue)
                continue;

            var isPostable = await _accountRepository.AnyAsync(
                a => a.Id == accountId.Value && !a.IsGroup, cancellationToken);
            if (!isPostable)
                return Fail("The default account must be an existing postable (non-group) account.");
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
