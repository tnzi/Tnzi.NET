namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 现金流量表（间接法）：种子默认分类、活动分桶、恒等式校验行、Unclassified 桶与 CSV 导出
/// </summary>
public class CashFlowReportTests : FinanceIntegrationTestBase
{
    private static LedgerPostingRequest Posting(DateTime date, string sourceId, params LedgerPostingLine[] lines)
        => new()
        {
            PostingDate = date,
            SourceType = "Test.CashFlow",
            SourceId = sourceId,
            Lines = [.. lines]
        };

    private Task<Result<CashFlowReportDto>> RunAsync(DateTime from, DateTime to)
        => InScopeAsync<IFinancialReportService, Result<CashFlowReportDto>>(s => s.GetCashFlowAsync(from, to));

    [Fact]
    public async Task SeedDefault_AssignsCashFlowActivityDefaults()
    {
        await SeedCoaAsync();
        var repo = ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var byCode = (await repo.ToListAsync(a => !a.IsGroup)).ToDictionary(a => a.Code);

        byCode["1110"].CashFlowActivity.ShouldBe(CashFlowActivity.CashEquivalent);
        byCode["1120"].CashFlowActivity.ShouldBe(CashFlowActivity.CashEquivalent);
        byCode["1130"].CashFlowActivity.ShouldBe(CashFlowActivity.CashEquivalent);
        byCode["1500"].CashFlowActivity.ShouldBe(CashFlowActivity.Investing);
        byCode["3100"].CashFlowActivity.ShouldBe(CashFlowActivity.Financing);
        byCode["3200"].CashFlowActivity.ShouldBe(CashFlowActivity.Financing);
        byCode["1200"].CashFlowActivity.ShouldBe(CashFlowActivity.Operating);
        byCode["2100"].CashFlowActivity.ShouldBe(CashFlowActivity.Operating);
        // 损益科目不设分类（报表经净利润整体归入经营活动）
        byCode["4100"].CashFlowActivity.ShouldBeNull();
    }

    [Fact]
    public async Task CashFlow_BucketsActivities_AndCheckIsZero()
    {
        await SeedCoaAsync();
        var march = new DateTime(2026, 3, 10);

        // 赊销 1000：净利润 +1000，AR +1000（经营调整 -1000）
        (await PostLedgerAsync(SimpleSale(1000m, march, "cf-sale"))).Succeeded.ShouldBeTrue();
        // 收款 600：Dr Bank / Cr AR
        (await PostLedgerAsync(Posting(march.AddDays(1), "cf-receipt",
            new LedgerPostingLine { AccountCode = "1120", Debit = 600m },
            new LedgerPostingLine { AccountRole = AccountSystemRole.AccountsReceivable, Credit = 600m }))).Succeeded.ShouldBeTrue();
        // 购固定资产 200：Dr 1500 / Cr Bank（投资活动流出）
        (await PostLedgerAsync(Posting(march.AddDays(2), "cf-capex",
            new LedgerPostingLine { AccountCode = "1500", Debit = 200m },
            new LedgerPostingLine { AccountCode = "1120", Credit = 200m }))).Succeeded.ShouldBeTrue();
        // 股东注资 500：Dr Bank / Cr 3100（筹资活动流入）
        (await PostLedgerAsync(Posting(march.AddDays(3), "cf-equity",
            new LedgerPostingLine { AccountCode = "1120", Debit = 500m },
            new LedgerPostingLine { AccountCode = "3100", Credit = 500m }))).Succeeded.ShouldBeTrue();

        var result = await RunAsync(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        result.Succeeded.ShouldBeTrue(result.Message);
        var report = result.Data!;

        report.NetProfit.ShouldBe(1000m);
        // 经营调整：AR 净增 400（1000 赊销 - 600 回款）→ 现金流贡献 -400
        report.Operating.ShouldHaveSingleItem().Balance.ShouldBe(-400m);
        report.TotalOperating.ShouldBe(600m);
        report.TotalInvesting.ShouldBe(-200m);
        report.TotalFinancing.ShouldBe(500m);
        report.TotalUnclassified.ShouldBe(0m);
        report.NetCashFlow.ShouldBe(900m);
        report.OpeningCash.ShouldBe(0m);
        report.CashMovement.ShouldBe(900m);
        report.ClosingCash.ShouldBe(900m);
        report.CheckDifference.ShouldBe(0m);

        // 次期区间：期初现金结转，期间零流动
        var next = await RunAsync(new DateTime(2026, 4, 1), new DateTime(2026, 4, 30));
        next.Data!.OpeningCash.ShouldBe(900m);
        next.Data.NetCashFlow.ShouldBe(0m);
        next.Data.ClosingCash.ShouldBe(900m);
        next.Data.CheckDifference.ShouldBe(0m);
    }

    [Fact]
    public async Task CashFlow_UnclassifiedAccounts_LandInExplicitBucket_AndCheckStaysZero()
    {
        await SeedCoaAsync();
        // 无分类的借款科目（模拟存量库科目 CashFlowActivity = null；用 2900 避开模板已占的 2400 Wages Payable）
        var loan = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.CreateAsync(new CreateAccountDto
        {
            Code = "2900",
            Name = "Bank Loan",
            RootType = AccountRootType.Liability
        }));
        loan.Succeeded.ShouldBeTrue(loan.Message);

        // 提款 300：Dr Bank / Cr 借款
        (await PostLedgerAsync(Posting(new DateTime(2026, 5, 5), "cf-loan",
            new LedgerPostingLine { AccountCode = "1120", Debit = 300m },
            new LedgerPostingLine { AccountCode = "2900", Credit = 300m }))).Succeeded.ShouldBeTrue();

        var result = await RunAsync(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31));
        var report = result.Data!;

        var row = report.Unclassified.ShouldHaveSingleItem();
        row.Code.ShouldBe("2900");
        row.Balance.ShouldBe(300m);
        report.TotalUnclassified.ShouldBe(300m);
        report.NetCashFlow.ShouldBe(300m);
        report.CashMovement.ShouldBe(300m);
        report.CheckDifference.ShouldBe(0m);
    }

    [Fact]
    public async Task CashFlow_MislabeledPnlAccount_StaysInNetProfit_NotInCash()
    {
        await SeedCoaAsync();
        // 把收入科目误标为 CashEquivalent：报表必须忽略其分类（净额仍经净利润），不得流入现金桶
        var repo = ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var income = await repo.FirstOrDefaultAsync(a => a.Code == "4100");
        income.ShouldNotBeNull();
        income.CashFlowActivity = CashFlowActivity.CashEquivalent;
        await repo.UpdateAsync(income);
        await repo.SaveChangesAsync();

        // 现销 250：Dr Bank / Cr 4100
        (await PostLedgerAsync(Posting(new DateTime(2026, 6, 5), "cf-mislabel",
            new LedgerPostingLine { AccountCode = "1120", Debit = 250m },
            new LedgerPostingLine { AccountCode = "4100", Credit = 250m }))).Succeeded.ShouldBeTrue();

        var result = await RunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));
        var report = result.Data!;

        report.NetProfit.ShouldBe(250m);
        report.CashMovement.ShouldBe(250m); // 只有真正的现金科目（1120）计入现金变动
        report.NetCashFlow.ShouldBe(250m);
        report.CheckDifference.ShouldBe(0m);
    }

    [Fact]
    public async Task CashFlowExport_EmitsSectionsAndCheckRow()
    {
        await SeedCoaAsync();
        (await PostLedgerAsync(Posting(new DateTime(2026, 3, 10), "cf-csv",
            new LedgerPostingLine { AccountCode = "1120", Debit = 250m },
            new LedgerPostingLine { AccountCode = "3100", Credit = 250m }))).Succeeded.ShouldBeTrue();

        var csv = await InScopeAsync<IFinancialReportService, Result<string>>(
            s => s.ExportCashFlowCsvAsync(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31)));

        csv.Succeeded.ShouldBeTrue(csv.Message);
        csv.Data!.ShouldStartWith("Section,Code,Name,SubType,Amount");
        csv.Data.ShouldContain("Net profit");
        csv.Data.ShouldContain("Net cash from financing activities");
        csv.Data.ShouldContain("Net cash flow");
        var lines = csv.Data.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        lines[^1].ShouldStartWith(",,Check,");
        decimal.Parse(lines[^1][(lines[^1].LastIndexOf(',') + 1)..], CultureInfo.InvariantCulture).ShouldBe(0m);
    }
}
