namespace Tnzi.Finance.Offers.Tests.Integration;

/// <summary>
/// 加载本模块后，核心的主数据删除守卫多问一个问题：报价单 / 采购订单还在用它吗？
/// </summary>
/// <remarks>
/// 与父测试项目的 <c>OfferPackageAbsenceTests</c> 成对：那边证明"不加载 = 少拒绝"，
/// 这边证明"加载 = 多拒绝"。合起来才说清 <see cref="IMasterDataUsageProvider"/> 的语义 ——
/// **它只会增加拒绝，永远不会放行任何一个原本被拒绝的删除**。
///
/// ★ 顺带补上一个拆分之前就存在的洞：核心的三个删除守卫从来没查过这两类单据，
/// 于是删掉一个只出现在报价单上的客户，那张报价单会永久指向一条被软删因而不可见的记录。
/// 拆分让这个洞**必须**现在补 —— 不补，它就永远补不了（补它需要一条 内核 → 本模块 的反向引用）。
/// </remarks>
public class MasterDataUsageTests : OfferIntegrationTestBase
{
    private async Task<Guid> CustomerAsync(string name)
    {
        var r = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = name, Currency = "USD" }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task<Guid> VendorAsync(string name)
    {
        var r = await InScopeAsync<IVendorService, Result<VendorDto>>(
            s => s.CreateAsync(new CreateVendorDto { Name = name, Currency = "USD" }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task<Guid> ItemAsync(string name)
    {
        var r = await InScopeAsync<IItemService, Result<ItemDto>>(
            s => s.CreateAsync(new CreateItemDto { Name = name }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    [Fact]
    public async Task CustomerOnAnEstimate_CannotBeDeleted()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync("Fabrikam Inc");
        var revenue = await AccountIdByCodeAsync("4100");

        var draft = await InScopeAsync<IEstimateService, Result<EstimateDto>>(s => s.CreateDraftAsync(new CreateEstimateDto
        {
            CustomerId = customer,
            DocDate = DateTime.UtcNow.Date,
            Currency = "USD",
            Lines = [new CreateOfferLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 500m }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        // 没有任何会计单据引用他 —— 只有一张报价单。拆分之前这里会放行，
        // 然后那张报价单的往来方名字就永久空了。
        var deleted = await InScopeAsync<ICustomerService, Result>(s => s.DeleteAsync(customer));

        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
        deleted.Message.ShouldNotBeNull();
        deleted.Message.ShouldContain("estimates");
    }

    [Fact]
    public async Task VendorOnAPurchaseOrder_CannotBeDeleted()
    {
        await SeedCoaAsync();
        var vendor = await VendorAsync("Tailspin Supply");
        var expense = await AccountIdByCodeAsync("5200");

        var draft = await InScopeAsync<IPurchaseOrderService, Result<PurchaseOrderDto>>(s => s.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            VendorId = vendor,
            DocDate = DateTime.UtcNow.Date,
            Currency = "USD",
            Lines = [new CreateOfferLineDto { AccountId = expense, Quantity = 1, UnitPrice = 80m }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var deleted = await InScopeAsync<IVendorService, Result>(s => s.DeleteAsync(vendor));

        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
        deleted.Message.ShouldNotBeNull();
        deleted.Message.ShouldContain("purchase orders");
    }

    [Fact]
    public async Task ItemOnAnOfferLine_CannotBeDeleted()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync("Litware Group");
        var item = await ItemAsync("Consulting day");
        var revenue = await AccountIdByCodeAsync("4100");

        var draft = await InScopeAsync<IEstimateService, Result<EstimateDto>>(s => s.CreateDraftAsync(new CreateEstimateDto
        {
            CustomerId = customer,
            DocDate = DateTime.UtcNow.Date,
            Currency = "USD",
            Lines = [new CreateOfferLineDto { ItemId = item, AccountId = revenue, Quantity = 2, UnitPrice = 900m }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var deleted = await InScopeAsync<IItemService, Result>(s => s.DeleteAsync(item));

        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
        deleted.Message.ShouldNotBeNull();
        deleted.Message.ShouldContain("estimate or purchase-order lines");
    }

    /// <summary>
    /// 科目也是要约行引用的主数据：一个只被报价单行指着、还没被过账用过的科目在核心的分录检查里
    /// 是干净的，删掉后转发票那一步才撞上「科目不存在」。与 Recurring 回答的 <c>Account</c> 同一契约。
    /// </summary>
    [Fact]
    public async Task AccountOnAnOfferLine_CannotBeDeleted()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync("Northwind Traders");
        var created = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.CreateAsync(new CreateAccountDto
        {
            Code = "4190",
            Name = "Project revenue",
            RootType = AccountRootType.Income,
        }));
        created.Succeeded.ShouldBeTrue(created.Message);

        var draft = await InScopeAsync<IEstimateService, Result<EstimateDto>>(s => s.CreateDraftAsync(new CreateEstimateDto
        {
            CustomerId = customer,
            DocDate = DateTime.UtcNow.Date,
            Currency = "USD",
            Lines = [new CreateOfferLineDto { AccountId = created.Data!.Id, Quantity = 1, UnitPrice = 500m }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var deleted = await InScopeAsync<IChartOfAccountsService, Result>(s => s.DeleteAsync(created.Data!.Id));

        deleted.Succeeded.ShouldBeFalse("an open estimate line posts to this account when converted");
        deleted.Code.ShouldBe(409);
        deleted.Message.ShouldNotBeNull();
        deleted.Message.ShouldContain("estimate or purchase-order lines");
    }

    /// <summary>
    /// 没被任何要约单据引用时照旧放行：本契约回答的是事实，不是"一律拒绝"。
    /// </summary>
    [Fact]
    public async Task UnreferencedMasterData_IsStillDeletable()
    {
        var customer = await CustomerAsync("Adventure Works");

        var deleted = await InScopeAsync<ICustomerService, Result>(s => s.DeleteAsync(customer));

        deleted.Succeeded.ShouldBeTrue(deleted.Message);
    }
}
