using Microsoft.Extensions.Configuration;
using Tnzi.Modules;

namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 薪资组件的费用 / 负债科目也是「分录之外还要往这个科目上写」的引用者：
/// 一个只被组件指着、还没跑过工资的科目在核心的分录检查里是干净的，删掉后下一次
/// 过账工资才撞上「科目不存在」。本模块经 <see cref="IMasterDataUsageProvider"/> 回答这件事。
/// </summary>
/// <remarks>
/// 与 <c>Tnzi.Finance.Offers</c> / <c>Tnzi.Finance.Recurring</c> 的同名测试成对：契约只会增加拒绝。
/// </remarks>
public class MasterDataUsageTests : PayrollIntegrationTestBase
{
    private async Task<Guid> CreateLeafAccountAsync(string code, AccountRootType rootType)
    {
        var created = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.CreateAsync(new CreateAccountDto
        {
            Code = code,
            Name = code,
            RootType = rootType,
        }));
        created.Succeeded.ShouldBeTrue(created.Message);
        return created.Data!.Id;
    }

    private async Task CreateComponentWithAccountsAsync(string code, Guid? expenseAccountId, Guid? liabilityAccountId)
    {
        var result = await InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.CreateAsync(new CreateSalaryComponentDto
        {
            Code = code,
            Name = code,
            Type = SalaryComponentType.Earning,
            Formula = "BASE",
            ExpenseAccountId = expenseAccountId,
            LiabilityAccountId = liabilityAccountId
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task Account_ReferencedAsComponentExpense_DeleteReturns409()
    {
        await SeedCoaAsync();
        var expense = await CreateLeafAccountAsync("6190", AccountRootType.Expense);
        await CreateComponentWithAccountsAsync("ALLOW", expense, null);

        var deleted = await InScopeAsync<IChartOfAccountsService, Result>(s => s.DeleteAsync(expense));

        deleted.Succeeded.ShouldBeFalse("a salary component posts its expense here every pay run");
        deleted.Code.ShouldBe(409);
        deleted.Message!.ShouldContain("salary component");
    }

    [Fact]
    public async Task Account_ReferencedAsComponentLiability_DeleteReturns409()
    {
        await SeedCoaAsync();
        var liability = await CreateLeafAccountAsync("2490", AccountRootType.Liability);
        await CreateComponentWithAccountsAsync("UNION", null, liability);

        var deleted = await InScopeAsync<IChartOfAccountsService, Result>(s => s.DeleteAsync(liability));

        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
    }

    /// <summary>没被任何组件引用时照旧放行：契约回答的是事实。</summary>
    [Fact]
    public async Task UnreferencedAccount_IsStillDeletable()
    {
        await SeedCoaAsync();
        var spare = await CreateLeafAccountAsync("6195", AccountRootType.Expense);
        await CreateComponentWithAccountsAsync("BASIC", null, null);

        var deleted = await InScopeAsync<IChartOfAccountsService, Result>(s => s.DeleteAsync(spare));

        deleted.Succeeded.ShouldBeTrue(deleted.Message);
    }

    /// <summary>DI 接线在集成测试里看不见（基类镜像了注册图），所以直接对模块的注册断言。</summary>
    [Fact]
    public async Task PayrollModule_RegistersTheUsageProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        await new PayrollModule().ConfigureServicesAsync(new ServiceConfigurationContext(services, configuration));

        var provider = services.Single(d => d.ServiceType == typeof(IMasterDataUsageProvider));
        provider.ImplementationType.ShouldBe(typeof(PayrollMasterDataUsageProvider));
        provider.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }
}
