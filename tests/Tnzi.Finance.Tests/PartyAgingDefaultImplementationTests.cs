namespace Tnzi.Finance.Tests;

/// <summary>
/// <see cref="IFinancialReportService.GetPartyAgingAsync"/> 的默认接口实现：消费方自己实现了报表服务、
/// 没有重写这个后加的方法时，往来方工作面拿到的仍是全量账龄里那一行。
/// </summary>
public class PartyAgingDefaultImplementationTests
{
    private static readonly Guid Alpha = Guid.NewGuid();
    private static readonly Guid Beta = Guid.NewGuid();

    [Fact]
    public async Task DefaultImplementation_PicksThatPartysRowOutOfTheFullAging()
    {
        IFinancialReportService reports = new AgingOnlyReports();

        var party = await reports.GetPartyAgingAsync(FinancePartyType.Customer, Beta, new DateTime(2026, 6, 30));

        party.Succeeded.ShouldBeTrue(party.Message);
        var row = party.Data!.Rows.ShouldHaveSingleItem();
        row.PartyId.ShouldBe(Beta);
        party.Data.Totals.Total.ShouldBe(70m);
        party.Data.Totals.Days1To30.ShouldBe(70m);
        party.Data.BaseCurrency.ShouldBe("USD");
    }

    [Fact]
    public async Task DefaultImplementation_ForAPartyWithoutARow_IsEmpty()
    {
        IFinancialReportService reports = new AgingOnlyReports();

        var party = await reports.GetPartyAgingAsync(FinancePartyType.Vendor, Alpha, new DateTime(2026, 6, 30));

        party.Succeeded.ShouldBeTrue(party.Message);
        party.Data!.Rows.ShouldBeEmpty();
        party.Data.Totals.Total.ShouldBe(0m);
    }

    /// <summary>只实现账龄两项的报表服务（其余成员与本测试无关）。</summary>
    private sealed class AgingOnlyReports : IFinancialReportService
    {
        public Task<Result<AgingReportDto>> GetArAgingAsync(DateTime asOf, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<AgingReportDto>.Success(new AgingReportDto
            {
                AsOf = asOf,
                BaseCurrency = "USD",
                Rows =
                [
                    new AgingRowDto { PartyId = Alpha, PartyName = "Alpha", Current = 30m, Total = 30m },
                    new AgingRowDto { PartyId = Beta, PartyName = "Beta", Days1To30 = 70m, Total = 70m }
                ],
                Totals = new AgingBucketsDto { Current = 30m, Days1To30 = 70m, Total = 100m }
            }));

        public Task<Result<AgingReportDto>> GetApAgingAsync(DateTime asOf, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<AgingReportDto>.Success(new AgingReportDto { AsOf = asOf, BaseCurrency = "USD" }));

        public Task<Result<TrialBalanceReportDto>> GetTrialBalanceAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<BalanceSheetReportDto>> GetBalanceSheetAsync(DateTime asOf, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<ProfitAndLossReportDto>> GetProfitAndLossAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<GeneralLedgerReportDto>> GetGeneralLedgerAsync(Guid accountId, DateTime from, DateTime to, PagedQueryDto paging, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<TaxSummaryReportDto>> GetTaxSummaryAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<CashFlowReportDto>> GetCashFlowAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<string>> ExportTrialBalanceCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<string>> ExportBalanceSheetCsvAsync(DateTime asOf, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<string>> ExportProfitAndLossCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<string>> ExportGeneralLedgerCsvAsync(Guid accountId, DateTime from, DateTime to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<string>> ExportArAgingCsvAsync(DateTime asOf, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<string>> ExportApAgingCsvAsync(DateTime asOf, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<string>> ExportTaxSummaryCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<string>> ExportCashFlowCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
