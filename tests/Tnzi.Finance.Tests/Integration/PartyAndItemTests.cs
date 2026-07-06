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
}
