namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 往来方与目录：CRUD、编码唯一、关键字过滤、默认科目校验
/// </summary>
public class PartyAndItemTests : FinanceIntegrationTestBase
{
    private Task<Result<CustomerDto>> CreateCustomerAsync(string name, string? code = null)
        => InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto
        {
            Name = name,
            Code = code,
            Email = $"{name.ToLower()}@example.com"
        }));

    [Fact]
    public async Task Customer_Crud_Roundtrip()
    {
        var created = await CreateCustomerAsync("Acme Inc", "CUST-1");
        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Code.ShouldBe("CUST-1");

        var updated = await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.UpdateAsync(created.Data.Id, new UpdateCustomerDto
        {
            Name = "Acme Incorporated",
            Code = "CUST-1",
            Currency = "eur",
            PaymentTermsDays = 45,
            IsActive = false
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.Name.ShouldBe("Acme Incorporated");
        updated.Data.Currency.ShouldBe("EUR");
        updated.Data.PaymentTermsDays.ShouldBe(45);
        updated.Data.IsActive.ShouldBeFalse();

        var deleted = await InScopeAsync<ICustomerService, Result>(s => s.DeleteAsync(created.Data.Id));
        deleted.Succeeded.ShouldBeTrue(deleted.Message);

        var fetched = await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.GetAsync(created.Data.Id));
        fetched.Succeeded.ShouldBeFalse();
        fetched.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Customer_DuplicateCode_Rejected()
    {
        (await CreateCustomerAsync("First", "DUP-1")).Succeeded.ShouldBeTrue();

        var duplicate = await CreateCustomerAsync("Second", "DUP-1");
        duplicate.Succeeded.ShouldBeFalse();
        duplicate.Code.ShouldBe(409);

        // 编码可空：多个无编码客户不冲突
        (await CreateCustomerAsync("NoCode A")).Succeeded.ShouldBeTrue();
        (await CreateCustomerAsync("NoCode B")).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Customer_KeywordFilter_MatchesCodeNameEmail()
    {
        await CreateCustomerAsync("Globex", "GLX");
        await CreateCustomerAsync("Initech", "INI");

        var byName = await InScopeAsync<ICustomerService, Result<IPagedList<CustomerDto>>>(
            s => s.GetPagedAsync(new CustomerQueryDto { Keyword = "glob" }));
        byName.Data!.TotalCount.ShouldBe(1);
        byName.Data.Items[0].Name.ShouldBe("Globex");

        var byEmail = await InScopeAsync<ICustomerService, Result<IPagedList<CustomerDto>>>(
            s => s.GetPagedAsync(new CustomerQueryDto { Keyword = "initech@" }));
        byEmail.Data!.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Vendor_Crud_And_DuplicateCode()
    {
        var created = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto
        {
            Name = "Supplies Co",
            Code = "VEND-1"
        }));
        created.Succeeded.ShouldBeTrue(created.Message);

        var duplicate = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto
        {
            Name = "Other Co",
            Code = "VEND-1"
        }));
        duplicate.Succeeded.ShouldBeFalse();
        duplicate.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Customer_ReferencedByInvoice_CannotBeDeleted()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Referenced Co", "REF-1");
        customer.Succeeded.ShouldBeTrue(customer.Message);

        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customer.Data!.Id,
            DocDate = new DateTime(2026, 3, 10),
            Lines = [new CreateInvoiceLineDto { Description = "X", Quantity = 1, UnitPrice = 100m }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        // 被单据引用 → 拒删 409（引导停用），死注释修复的回归
        var blocked = await InScopeAsync<ICustomerService, Result>(s => s.DeleteAsync(customer.Data.Id));
        blocked.Succeeded.ShouldBeFalse();
        blocked.Code.ShouldBe(409);

        // 无引用客户仍可删
        var free = await CreateCustomerAsync("Free Co", "FREE-1");
        (await InScopeAsync<ICustomerService, Result>(s => s.DeleteAsync(free.Data!.Id))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Vendor_ReferencedByBill_CannotBeDeleted()
    {
        await SeedCoaAsync();
        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = "Ref Vendor", Code = "RV-1" }));
        vendor.Succeeded.ShouldBeTrue(vendor.Message);

        var bill = await InScopeAsync<IBillService, Result<BillDto>>(s => s.CreateDraftAsync(new CreateBillDto
        {
            VendorId = vendor.Data!.Id,
            DocDate = new DateTime(2026, 3, 10),
            Lines = [new CreateBillLineDto { Description = "X", Quantity = 1, UnitPrice = 100m }]
        }));
        bill.Succeeded.ShouldBeTrue(bill.Message);

        var blocked = await InScopeAsync<IVendorService, Result>(s => s.DeleteAsync(vendor.Data.Id));
        blocked.Succeeded.ShouldBeFalse();
        blocked.Code.ShouldBe(409);

        var free = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = "Free Vendor", Code = "FV-1" }));
        (await InScopeAsync<IVendorService, Result>(s => s.DeleteAsync(free.Data!.Id))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Item_DefaultAccount_MustBePostable()
    {
        // 分组科目不可作为默认科目
        var group = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.CreateAsync(new CreateAccountDto
        {
            Code = "4000",
            Name = "Income Group",
            RootType = AccountRootType.Income,
            IsGroup = true
        }));
        group.Succeeded.ShouldBeTrue(group.Message);

        var invalid = await InScopeAsync<IItemService, Result<ItemDto>>(s => s.CreateAsync(new CreateItemDto
        {
            Name = "Consulting",
            IncomeAccountId = group.Data!.Id
        }));
        invalid.Succeeded.ShouldBeFalse();

        var leaf = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.CreateAsync(new CreateAccountDto
        {
            Code = "4100",
            Name = "Service Income",
            RootType = AccountRootType.Income
        }));
        leaf.Succeeded.ShouldBeTrue(leaf.Message);

        var valid = await InScopeAsync<IItemService, Result<ItemDto>>(s => s.CreateAsync(new CreateItemDto
        {
            Name = "Consulting",
            Type = ItemType.Service,
            SalesPrice = 150m,
            IncomeAccountId = leaf.Data!.Id
        }));
        valid.Succeeded.ShouldBeTrue(valid.Message);
        valid.Data!.Type.ShouldBe(ItemType.Service);
        valid.Data.SalesPrice.ShouldBe(150m);
    }

    /// <summary>回归：item 收入科目须 RootType=Income、费用科目须 RootType=Expense，
    /// 否则无行覆盖的销售/采购单会把收入/成本静默过到资产负债类科目（TB 仍平但报表错）。</summary>
    [Fact]
    public async Task Item_DefaultAccount_MustMatchRootType()
    {
        await SeedCoaAsync();
        var assetLeaf = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("1120")); // 银行=资产叶子
        var incomeLeaf = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("4100")); // 服务收入

        // 资产科目作收入默认 → 拒绝
        var invalid = await InScopeAsync<IItemService, Result<ItemDto>>(s => s.CreateAsync(new CreateItemDto
        {
            Name = "MisItem",
            IncomeAccountId = assetLeaf!.Id
        }));
        invalid.Succeeded.ShouldBeFalse();

        // 收入科目作收入默认 → 通过
        var ok = await InScopeAsync<IItemService, Result<ItemDto>>(s => s.CreateAsync(new CreateItemDto
        {
            Name = "GoodItem",
            IncomeAccountId = incomeLeaf!.Id
        }));
        ok.Succeeded.ShouldBeTrue(ok.Message);
    }

    [Fact]
    public async Task Item_NegativePrice_Rejected()
    {
        var result = await InScopeAsync<IItemService, Result<ItemDto>>(s => s.CreateAsync(new CreateItemDto
        {
            Name = "Bad",
            SalesPrice = -1m
        }));
        result.Succeeded.ShouldBeFalse();
    }

    /// <summary>回归：被单据行引用的 item 不可删（死注释修复）——软删会隐藏主数据但行项仍在子账。</summary>
    [Fact]
    public async Task Item_ReferencedByInvoiceLine_CannotBeDeleted()
    {
        await SeedCoaAsync();
        var incomeLeaf = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("4100"));
        var customer = await CreateCustomerAsync("Item Ref Co", "IREF-1");
        customer.Succeeded.ShouldBeTrue(customer.Message);

        var item = await InScopeAsync<IItemService, Result<ItemDto>>(s => s.CreateAsync(new CreateItemDto
        {
            Name = "Billable Service",
            Type = ItemType.Service,
            SalesPrice = 200m,
            IncomeAccountId = incomeLeaf!.Id
        }));
        item.Succeeded.ShouldBeTrue(item.Message);

        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customer.Data!.Id,
            DocDate = new DateTime(2026, 4, 1),
            Lines = [new CreateInvoiceLineDto { ItemId = item.Data!.Id, Description = "Svc", Quantity = 1, UnitPrice = 200m }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        // 被单据行引用 → 拒删 409（引导停用）
        var blocked = await InScopeAsync<IItemService, Result>(s => s.DeleteAsync(item.Data.Id));
        blocked.Succeeded.ShouldBeFalse();
        blocked.Code.ShouldBe(409);

        // 无引用 item 仍可删
        var free = await InScopeAsync<IItemService, Result<ItemDto>>(s => s.CreateAsync(new CreateItemDto
        {
            Name = "Unused Item",
            Type = ItemType.Service,
            SalesPrice = 10m,
            IncomeAccountId = incomeLeaf.Id
        }));
        free.Succeeded.ShouldBeTrue(free.Message);
        (await InScopeAsync<IItemService, Result>(s => s.DeleteAsync(free.Data!.Id))).Succeeded.ShouldBeTrue();
    }
}
