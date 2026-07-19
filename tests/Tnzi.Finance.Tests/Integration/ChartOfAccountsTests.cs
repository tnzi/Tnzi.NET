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

    // ---- 系统角色科目守卫（过账按角色解析且要求启用 → 删/停用即让对应过账永久 400）----

    /// <summary>1130 Undeposited Funds：种子科目，一条分录都没有，正是"第一天就能删掉"的场景</summary>
    private Task<Account?> UndepositedAsync()
        => InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("1130"));

    private static UpdateAccountDto UpdateOf(Account a, AccountSystemRole? role, bool isActive) => new()
    {
        Code = a.Code,
        Name = a.Name,
        ParentId = a.ParentId,
        SystemRole = role,
        IsActive = isActive
    };

    [Fact]
    public async Task Delete_SystemRoleAccount_WithoutPostings_Fails()
    {
        await SeedCoaAsync();
        var undeposited = await UndepositedAsync();
        undeposited!.SystemRole.ShouldBe(AccountSystemRole.UndepositedFunds);

        var result = await InScopeAsync<IChartOfAccountsService, Result>(s => s.DeleteAsync(undeposited.Id));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("UndepositedFunds");
        (await ReloadAsync<Account>(undeposited.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Update_DeactivatingSystemRoleAccount_Fails()
    {
        await SeedCoaAsync();
        var undeposited = await UndepositedAsync();

        var result = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(
            s => s.UpdateAsync(undeposited!.Id, UpdateOf(undeposited, undeposited.SystemRole, isActive: false)));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        // 守卫必须在触碰实体前返回：失败的 Result 不得留下半写入的更改
        (await ReloadAsync<Account>(undeposited!.Id))!.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_ClearingRoleThenDeactivating_Succeeds()
    {
        // 判据是更新后的结果状态而非原状态 —— 角色迁移/退役路径必须保持畅通
        await SeedCoaAsync();
        var undeposited = await UndepositedAsync();

        var result = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(
            s => s.UpdateAsync(undeposited!.Id, UpdateOf(undeposited, role: null, isActive: false)));

        result.Succeeded.ShouldBeTrue(result.Message);
        var reloaded = await ReloadAsync<Account>(undeposited!.Id);
        reloaded!.SystemRole.ShouldBeNull();
        reloaded.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_AfterClearingSystemRole_Succeeds()
    {
        await SeedCoaAsync();
        var undeposited = await UndepositedAsync();

        var cleared = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(
            s => s.UpdateAsync(undeposited!.Id, UpdateOf(undeposited, role: null, isActive: true)));
        cleared.Succeeded.ShouldBeTrue(cleared.Message);

        var result = await InScopeAsync<IChartOfAccountsService, Result>(s => s.DeleteAsync(undeposited.Id));
        result.Succeeded.ShouldBeTrue(result.Message);
    }

    // ---- 科目余额 ----

    [Fact]
    public async Task GetBalances_ReflectsPostedLedger_AndExcludesFutureDatedPostings()
    {
        await SeedCoaAsync();
        var ar = await AccountIdByCodeAsync("1200");
        var revenue = await AccountIdByCodeAsync("4100");

        var today = new DateTime(2026, 3, 15);
        (await PostLedgerAsync(SimpleSale(100m, today))).Succeeded.ShouldBeTrue();
        // 未来日期的过账（RequireFiscalYearForPosting 默认关，这是允许的）
        (await PostLedgerAsync(SimpleSale(40m, today.AddDays(10)))).Succeeded.ShouldBeTrue();

        var asOfToday = await InScopeAsync<IChartOfAccountsService, Result<List<AccountBalanceDto>>>(
            s => s.GetBalancesAsync([ar, revenue], today));

        asOfToday.Succeeded.ShouldBeTrue(asOfToday.Message);
        // as-of 当日只含 100：未来那 40 不进 —— 与同日资产负债表恒等（口径 PostingDate < 次日）
        asOfToday.Data!.Single(b => b.AccountId == ar).Balance.ShouldBe(100m);
        asOfToday.Data.Single(b => b.AccountId == ar).Debit.ShouldBe(100m);
        // 有符号余额不做正负归一化：收入在贷方 → 负
        asOfToday.Data.Single(b => b.AccountId == revenue).Balance.ShouldBe(-100m);

        // 基准日推过去之后两笔都进
        var asOfLater = await InScopeAsync<IChartOfAccountsService, Result<List<AccountBalanceDto>>>(
            s => s.GetBalancesAsync([ar, revenue], today.AddDays(10)));
        asOfLater.Data!.Single(b => b.AccountId == ar).Balance.ShouldBe(140m);
    }

    [Fact]
    public async Task GetBalances_AccountWithoutLines_ReturnsZeroRow()
    {
        await SeedCoaAsync();
        var unused = await AccountIdByCodeAsync("4900");

        var result = await InScopeAsync<IChartOfAccountsService, Result<List<AccountBalanceDto>>>(
            s => s.GetBalancesAsync([unused], new DateTime(2026, 3, 15)));

        result.Succeeded.ShouldBeTrue(result.Message);
        // 结果与入参一一对应（缺省 0），呈现端不必处理稀疏字典
        var row = result.Data!.Single();
        row.AccountId.ShouldBe(unused);
        row.Balance.ShouldBe(0m);
    }

    [Fact]
    public async Task GetBalances_SummaryFastPath_MatchesDetailPath()
    {
        await SeedCoaAsync();
        var ar = await AccountIdByCodeAsync("1200");
        // 跨月过账：汇总路径要走"整月桶 + 头尾残月明细"的混合读（单月区间会退化为纯明细）
        (await PostLedgerAsync(SimpleSale(100m, new DateTime(2026, 1, 20)))).Succeeded.ShouldBeTrue();
        (await PostLedgerAsync(SimpleSale(30m, new DateTime(2026, 2, 10)))).Succeeded.ShouldBeTrue();
        (await PostLedgerAsync(SimpleSale(7m, new DateTime(2026, 3, 5)))).Succeeded.ShouldBeTrue();

        var asOf = new DateTime(2026, 3, 15);
        var detail = await InScopeAsync<IChartOfAccountsService, Result<List<AccountBalanceDto>>>(
            s => s.GetBalancesAsync([ar], asOf));

        UseBalanceSummaryOption = true;
        var summary = await InScopeAsync<IChartOfAccountsService, Result<List<AccountBalanceDto>>>(
            s => s.GetBalancesAsync([ar], asOf));

        detail.Data!.Single().Balance.ShouldBe(137m);
        summary.Data!.Single().Balance.ShouldBe(137m);
    }
}
