namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 科目表：默认模板播种、唯一性约束、树形校验、删除守卫
/// </summary>
public class ChartOfAccountsTests : FinanceIntegrationTestBase
{
    [Fact]
    public async Task SeedDefault_CreatesTemplate_WithResolvableSystemRoles()
    {
        var result = await InScopeAsync<IChartOfAccountsService, Result<int>>(s => s.SeedDefaultAsync());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBeGreaterThan(20);

        var ar = await InScopeAsync<IChartOfAccountsService, Account?>(
            s => s.FindByRoleAsync(AccountSystemRole.AccountsReceivable));
        ar.ShouldNotBeNull();
        ar.Code.ShouldBe("1200");

        var tree = await InScopeAsync<IChartOfAccountsService, Result<List<AccountTreeDto>>>(s => s.GetTreeAsync());
        tree.Succeeded.ShouldBeTrue();
        tree.Data!.Count.ShouldBe(5); // 五大根类型分组
    }

    [Fact]
    public async Task SeedDefault_Twice_Fails()
    {
        await SeedCoaAsync();
        var second = await InScopeAsync<IChartOfAccountsService, Result<int>>(s => s.SeedDefaultAsync());

        second.Succeeded.ShouldBeFalse();
        second.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Create_DuplicateCode_Fails()
    {
        await SeedCoaAsync();

        var result = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.CreateAsync(new CreateAccountDto
        {
            Code = "4100",
            Name = "Duplicate",
            RootType = AccountRootType.Income
        }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Create_WithNonGroupParent_Fails()
    {
        await SeedCoaAsync();
        var leaf = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("4100"));

        var result = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.CreateAsync(new CreateAccountDto
        {
            Code = "4101",
            Name = "Child of leaf",
            RootType = AccountRootType.Income,
            ParentId = leaf!.Id
        }));

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("group");
    }

    [Fact]
    public async Task Create_DuplicateSystemRole_Fails()
    {
        await SeedCoaAsync();

        var result = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.CreateAsync(new CreateAccountDto
        {
            Code = "9999",
            Name = "Second AR",
            RootType = AccountRootType.Asset,
            SystemRole = AccountSystemRole.AccountsReceivable
        }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Update_ReparentCreatingCycle_Fails()
    {
        await SeedCoaAsync();

        // 1000 Assets 是 1100 Cash and Cash Equivalents 的父级；把 1000 挂到 1100 下会成环
        var assets = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("1000"));
        var cash = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("1100"));

        var result = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.UpdateAsync(assets!.Id, new UpdateAccountDto
        {
            Code = assets.Code,
            Name = assets.Name,
            ParentId = cash!.Id
        }));

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("cycle");
    }

    [Fact]
    public async Task Delete_WithChildren_Fails()
    {
        await SeedCoaAsync();
        var assets = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("1000"));

        var result = await InScopeAsync<IChartOfAccountsService, Result>(s => s.DeleteAsync(assets!.Id));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Delete_WithJournalLines_Fails()
    {
        await SeedCoaAsync();
        var posted = await PostLedgerAsync(SimpleSale(100m));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var revenue = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("4100"));
        var result = await InScopeAsync<IChartOfAccountsService, Result>(s => s.DeleteAsync(revenue!.Id));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Delete_UnusedLeaf_Succeeds()
    {
        await SeedCoaAsync();
        var other = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("4900"));

        var result = await InScopeAsync<IChartOfAccountsService, Result>(s => s.DeleteAsync(other!.Id));

        result.Succeeded.ShouldBeTrue(result.Message);
        (await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("4900"))).ShouldBeNull();
    }
}
