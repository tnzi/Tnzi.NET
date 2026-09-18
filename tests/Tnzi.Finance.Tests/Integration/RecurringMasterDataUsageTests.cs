using Tnzi.Finance.Recurring;
using Tnzi.Modules;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 周期性模板也是主数据的引用者：只被模板引用的客户 / 供应商 / 目录项 / 科目不可删。
/// </summary>
/// <remarks>
/// <para>
/// 被保护的缺陷：核心三个删除守卫经 <see cref="IMasterDataUsageProvider"/> 提问「除了会计单据还有谁在用」，
/// 而 <c>Tnzi.Finance.Recurring</c> 从没注册过实现。删掉一个只出现在一条月度订阅模板上的客户会成功，
/// 下一个到期日起每一期都因「Customer not found or inactive」落一行 Failed，重试到上限后彻底沉默 ——
/// 模板仍是 Active、列表照常显示下次运行日，而客户再也收不到账单。
/// </para>
/// <para>
/// 科目那一侧原本连契约都没问（<c>FinanceMasterDataKind</c> 没有 Account、<c>ChartOfAccountsService.DeleteAsync</c>
/// 不问 provider）：一个还没被过账用过的费用科目可以被删掉，而模板每期都要往它上面写。
/// </para>
/// </remarks>
public class RecurringMasterDataUsageTests : FinanceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // 镜像 FinanceRecurringModule 的注册（测试基类是模块注册图的手工镜像）
        services.AddScoped<IMasterDataUsageProvider, RecurringMasterDataUsageProvider>();
    }

    private static DateTime Today() => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    private async Task<Guid> CustomerAsync()
    {
        var r = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Retainer Customer", Currency = "USD" }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task<Guid> VendorAsync()
    {
        var r = await InScopeAsync<IVendorService, Result<VendorDto>>(
            s => s.CreateAsync(new CreateVendorDto { Name = "Rent Landlord", Currency = "USD" }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task<Guid> ItemAsync()
    {
        var revenue = await AccountIdByCodeAsync("4100");
        var r = await InScopeAsync<IItemService, Result<ItemDto>>(
            s => s.CreateAsync(new CreateItemDto { Name = "Support plan", Code = "SUPPORT", IncomeAccountId = revenue, SalesPrice = 99m }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task<RecurringDocumentDto> InvoiceTemplateAsync(Guid customer, Guid? itemId = null, Guid? accountId = null)
    {
        var revenue = accountId ?? await AccountIdByCodeAsync("4100");
        var r = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.CreateAsync(new CreateRecurringDocumentDto
        {
            Name = "Monthly retainer",
            Kind = RecurringDocKind.Invoice,
            PartyId = customer,
            Currency = "USD",
            Frequency = RecurrenceFrequency.Monthly,
            Interval = 1,
            StartDate = Today().AddDays(10),
            DueDays = 30,
            Lines = [new CreateRecurringLineDto { AccountId = revenue, ItemId = itemId, Quantity = 1, UnitPrice = 500m }],
        }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!;
    }

    private async Task<RecurringDocumentDto> BillTemplateAsync(Guid vendor)
    {
        var expense = await AccountIdByCodeAsync("5200");
        var r = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.CreateAsync(new CreateRecurringDocumentDto
        {
            Name = "Office rent",
            Kind = RecurringDocKind.Bill,
            PartyId = vendor,
            Currency = "USD",
            Frequency = RecurrenceFrequency.Monthly,
            Interval = 1,
            StartDate = Today().AddDays(10),
            DueDays = 15,
            Lines = [new CreateRecurringLineDto { AccountId = expense, Quantity = 1, UnitPrice = 2000m }],
        }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!;
    }

    [Fact]
    public async Task Customer_ReferencedByActiveTemplate_DeleteReturns409()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        await InvoiceTemplateAsync(customer);

        var deleted = await InScopeAsync<ICustomerService, Result>(s => s.DeleteAsync(customer));

        deleted.Succeeded.ShouldBeFalse("the template would fail every period from now on");
        deleted.Code.ShouldBe(409);
        deleted.Message!.ShouldContain("recurring", Case.Insensitive);
    }

    [Fact]
    public async Task Customer_ReferencedOnlyByEndedTemplate_DeleteSucceeds()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var template = await InvoiceTemplateAsync(customer);
        (await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.EndAsync(template.Id))).Succeeded.ShouldBeTrue();

        var deleted = await InScopeAsync<ICustomerService, Result>(s => s.DeleteAsync(customer));

        deleted.Succeeded.ShouldBeTrue(deleted.Message);
    }

    [Fact]
    public async Task Vendor_ReferencedByBillTemplate_DeleteReturns409()
    {
        await SeedCoaAsync();
        var vendor = await VendorAsync();
        await BillTemplateAsync(vendor);

        var deleted = await InScopeAsync<IVendorService, Result>(s => s.DeleteAsync(vendor));

        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Item_ReferencedByTemplateLine_DeleteReturns409()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var item = await ItemAsync();
        await InvoiceTemplateAsync(customer, itemId: item);

        var deleted = await InScopeAsync<IItemService, Result>(s => s.DeleteAsync(item));

        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
    }

    /// <summary>
    /// 科目守卫：一个还没被过账用过、但模板每期都要写的科目同样不可删。
    /// </summary>
    [Fact]
    public async Task Account_ReferencedByTemplateLine_DeleteReturns409()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var created = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.CreateAsync(new CreateAccountDto
        {
            Code = "4190",
            Name = "Retainer revenue",
            RootType = AccountRootType.Income,
        }));
        created.Succeeded.ShouldBeTrue(created.Message);
        await InvoiceTemplateAsync(customer, accountId: created.Data!.Id);

        var deleted = await InScopeAsync<IChartOfAccountsService, Result>(s => s.DeleteAsync(created.Data.Id));

        deleted.Succeeded.ShouldBeFalse("the template posts to this account every period");
        deleted.Code.ShouldBe(409);
    }

    /// <summary>
    /// DI 接线在集成测试里看不见（基类镜像了注册图），所以直接对模块的注册断言。
    /// </summary>
    [Fact]
    public async Task FinanceRecurringModule_RegistersTheUsageProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        await new FinanceRecurringModule().ConfigureServicesAsync(new ServiceConfigurationContext(services, configuration));

        var provider = services.Single(d => d.ServiceType == typeof(IMasterDataUsageProvider));
        provider.ImplementationType.ShouldBe(typeof(RecurringMasterDataUsageProvider));
        provider.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }
}
