namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 财务报表：试算平衡恒等式、资产负债表配平（本年利润计算行）、利润表、总账明细
/// </summary>
public class FinancialReportTests : FinanceIntegrationTestBase
{
    /// <summary>
    /// 播种并过账两笔业务：
    /// 1 月 10 日销售 1000（借 应收 / 贷 销售收入）；
    /// 2 月 10 日费用 300（借 运营费用 / 贷 银行）
    /// </summary>
    private async Task SeedLedgerAsync()
    {
        await SeedCoaAsync();

        var sale = await PostLedgerAsync(SimpleSale(1000m, new DateTime(2026, 1, 10), "sale-1"));
        sale.Succeeded.ShouldBeTrue(sale.Message);

        var expense = await PostLedgerAsync(new LedgerPostingRequest
        {
            PostingDate = new DateTime(2026, 2, 10),
            Memo = "Office rent",
            SourceType = "Test.Expense",
            SourceId = "exp-1",
            Lines =
            [
                new LedgerPostingLine { AccountCode = "5200", Debit = 300m },
                new LedgerPostingLine { AccountCode = "1120", Credit = 300m }
            ]
        });
        expense.Succeeded.ShouldBeTrue(expense.Message);
    }

    [Fact]
    public async Task TrialBalance_DebitsEqualCredits_AndClosingNetsToZero()
    {
        await SeedLedgerAsync();

        var result = await InScopeAsync<IFinancialReportService, Result<TrialBalanceReportDto>>(
            s => s.GetTrialBalanceAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));

        result.Succeeded.ShouldBeTrue(result.Message);
        var report = result.Data!;
        report.TotalPeriodDebit.ShouldBe(1300m);
        report.TotalPeriodCredit.ShouldBe(1300m);
        report.TotalOpeningBalance.ShouldBe(0m);
        report.TotalClosingBalance.ShouldBe(0m);

        var arRow = report.Rows.Single(r => r.Code == "1200");
        arRow.ClosingBalance.ShouldBe(1000m);
    }

    [Fact]
    public async Task TrialBalance_SplitsOpeningAndPeriod()
    {
        await SeedLedgerAsync();

        // 从 2 月起：1 月销售落入期初
        var result = await InScopeAsync<IFinancialReportService, Result<TrialBalanceReportDto>>(
            s => s.GetTrialBalanceAsync(new DateTime(2026, 2, 1), new DateTime(2026, 12, 31)));

        var report = result.Data!;
        var arRow = report.Rows.Single(r => r.Code == "1200");
        arRow.OpeningBalance.ShouldBe(1000m);
        arRow.PeriodDebit.ShouldBe(0m);

        report.TotalPeriodDebit.ShouldBe(300m);
        report.TotalOpeningBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task BalanceSheet_Balances_WithComputedCurrentEarnings()
    {
        await SeedLedgerAsync();

        var result = await InScopeAsync<IFinancialReportService, Result<BalanceSheetReportDto>>(
            s => s.GetBalanceSheetAsync(new DateTime(2026, 12, 31)));

        result.Succeeded.ShouldBeTrue(result.Message);
        var report = result.Data!;

        report.TotalAssets.ShouldBe(700m); // 应收 1000 + 银行 -300
        report.TotalLiabilities.ShouldBe(0m);
        report.CurrentEarnings.ShouldBe(700m); // 收入 1000 - 费用 300
        report.TotalEquity.ShouldBe(700m);
        report.BalanceCheck.ShouldBe(0m);
    }

    [Fact]
    public async Task ProfitAndLoss_ComputesNetProfit()
    {
        await SeedLedgerAsync();

        var result = await InScopeAsync<IFinancialReportService, Result<ProfitAndLossReportDto>>(
            s => s.GetProfitAndLossAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));

        var report = result.Data!;
        report.TotalIncome.ShouldBe(1000m);
        report.TotalExpenses.ShouldBe(300m);
        report.NetProfit.ShouldBe(700m);
        report.Income.Single().Code.ShouldBe("4100");
        report.Expenses.Single().Code.ShouldBe("5200");
    }

    [Fact]
    public async Task ProfitAndLoss_RespectsDateRange()
    {
        await SeedLedgerAsync();

        // 仅 1 月：只有销售
        var january = await InScopeAsync<IFinancialReportService, Result<ProfitAndLossReportDto>>(
            s => s.GetProfitAndLossAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)));

        january.Data!.TotalIncome.ShouldBe(1000m);
        january.Data.TotalExpenses.ShouldBe(0m);
    }

    [Fact]
    public async Task Reversal_IsReflectedInReports()
    {
        await SeedLedgerAsync();

        // 冲销 2 月的费用凭证
        var expenseEntries = await InScopeAsync<ILedgerPostingService, Result<List<JournalEntryDto>>>(
            s => s.GetBySourceAsync("Test.Expense", "exp-1"));
        var entryId = expenseEntries.Data!.Single().Id;

        var reversed = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(entryId, new ReverseJournalEntryDto()));
        reversed.Succeeded.ShouldBeTrue(reversed.Message);

        var pnl = await InScopeAsync<IFinancialReportService, Result<ProfitAndLossReportDto>>(
            s => s.GetProfitAndLossAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));

        pnl.Data!.TotalExpenses.ShouldBe(0m);
        pnl.Data.NetProfit.ShouldBe(1000m);
    }

    [Fact]
    public async Task GeneralLedger_ReportsOpeningClosingAndPagedLines()
    {
        await SeedLedgerAsync();

        var ar = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("1200"));

        var result = await InScopeAsync<IFinancialReportService, Result<GeneralLedgerReportDto>>(
            s => s.GetGeneralLedgerAsync(ar!.Id, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), new PagedQueryDto()));

        result.Succeeded.ShouldBeTrue(result.Message);
        var report = result.Data!;
        report.OpeningBalance.ShouldBe(0m);
        report.ClosingBalance.ShouldBe(1000m);
        report.Lines.TotalCount.ShouldBe(1);
        var line = report.Lines.Items.Single();
        line.Debit.ShouldBe(1000m);
        line.EntryNumber.ShouldNotBeNull();
    }
}
