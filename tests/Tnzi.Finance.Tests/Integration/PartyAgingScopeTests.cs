using Microsoft.Extensions.Logging;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 往来方工作面只算这一个往来方的账龄，而且算出来与全量账龄报表里他那一行逐字段相同。
/// </summary>
/// <remarks>
/// 被保护的缺陷：客户 / 供应商详情页的概览（以及对账单，Activity 形态还要算两遍）为了读一行，
/// 先算整本账的账龄：全部发票（含早已付清的）、全部贷项、全部收付款、全部核销分组、全部客户名，
/// 开销随账本寿命线性增长，而不是随这个往来方的活动量。
/// </remarks>
public class PartyAgingScopeTests : FinanceIntegrationTestBase
{
    /// <summary>字段初始化器先于基类构造函数体运行，故 ConfigureServices 被回调时它已就绪。</summary>
    private readonly SqlCapture _sql = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<ILoggerFactory>(_sql);
    }

    private static readonly DateTime AsOf = new(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>与单据有关、按往来方就能缩小范围的四张表。</summary>
    private static readonly string[] PartyScopedTables =
        ["Finance_Invoice\"", "Finance_CreditMemo\"", "Finance_PaymentEntry\"", "Finance_PaymentApplication\"", "Finance_Bill\""];

    private async Task<Guid> CustomerAsync(string name)
    {
        var r = await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = name, Currency = "USD" }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task<Guid> VendorAsync(string name)
    {
        var r = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = name, Currency = "USD" }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task<Guid> InvoiceAsync(Guid customerId, decimal amount, DateTime docDate)
    {
        var revenue = await AccountIdByCodeAsync("4100");
        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customerId,
            DocDate = docDate,
            DueDate = docDate.AddDays(30),
            Lines = [new CreateInvoiceLineDto { AccountId = revenue, Quantity = 1, UnitPrice = amount }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!.Id;
    }

    private async Task<Guid> BillAsync(Guid vendorId, decimal amount, DateTime docDate)
    {
        var expense = await AccountIdByCodeAsync("5200");
        var draft = await InScopeAsync<IBillService, Result<BillDto>>(s => s.CreateDraftAsync(new CreateBillDto
        {
            VendorId = vendorId,
            DocDate = docDate,
            DueDate = docDate.AddDays(30),
            Lines = [new CreateBillLineDto { AccountId = expense, Quantity = 1, UnitPrice = amount }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IBillService, Result<BillDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!.Id;
    }

    private async Task<Guid> CreditMemoAsync(Guid customerId, decimal amount, DateTime docDate)
    {
        var revenue = await AccountIdByCodeAsync("4100");
        var draft = await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.CreateDraftAsync(new CreateCreditMemoDto
        {
            CustomerId = customerId,
            DocDate = docDate,
            Lines = [new CreateCreditMemoLineDto { AccountId = revenue, Quantity = 1, UnitPrice = amount }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!.Id;
    }

    private async Task<Guid> PaymentAsync(PaymentDirection direction, FinancePartyType partyType, Guid partyId, decimal amount, DateTime docDate)
    {
        var bank = await AccountIdByCodeAsync("1120");
        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = direction,
            PartyType = partyType,
            PartyId = partyId,
            DocDate = docDate,
            Amount = amount,
            DepositToAccountId = bank
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!.Id;
    }

    private async Task ApplyAsync(SettlementDocType sourceType, Guid sourceId, SettlementDocType targetType, Guid targetId, decimal amount)
    {
        var applied = await InScopeAsync<ISettlementService, Result<List<PaymentApplicationDto>>>(s => s.ApplyAsync(new ApplySettlementDto
        {
            SourceType = sourceType,
            SourceId = sourceId,
            Targets = [new ApplySettlementTargetDto { TargetType = targetType, TargetId = targetId, Amount = amount }]
        }));
        applied.Succeeded.ShouldBeTrue(applied.Message);
    }

    /// <summary>
    /// 两个客户、两个供应商，各自带着已付清 / 部分核销 / 未核销预收 / 贷项信用 / 跨期的单据。
    /// </summary>
    private async Task<(Guid Alpha, Guid Beta, Guid North, Guid South)> SeedAsync()
    {
        await SeedCoaAsync();
        var alpha = await CustomerAsync("Alpha Co");
        var beta = await CustomerAsync("Beta Co");
        var north = await VendorAsync("North Supply");
        var south = await VendorAsync("South Supply");

        // Alpha：一张部分收款、一张逾期、一张贷项部分核销、一笔未核销的预收。
        var a1 = await InvoiceAsync(alpha, 500m, new DateTime(2026, 3, 1));
        var a2 = await InvoiceAsync(alpha, 300m, new DateTime(2026, 5, 20));
        var alphaPay = await PaymentAsync(PaymentDirection.Inbound, FinancePartyType.Customer, alpha, 400m, new DateTime(2026, 4, 1));
        await ApplyAsync(SettlementDocType.PaymentEntry, alphaPay, SettlementDocType.Invoice, a1, 350m);
        var alphaMemo = await CreditMemoAsync(alpha, 80m, new DateTime(2026, 5, 25));
        await ApplyAsync(SettlementDocType.CreditMemo, alphaMemo, SettlementDocType.Invoice, a2, 30m);

        // Beta：一张已付清、一张未付。
        var b1 = await InvoiceAsync(beta, 200m, new DateTime(2026, 2, 1));
        var betaPay = await PaymentAsync(PaymentDirection.Inbound, FinancePartyType.Customer, beta, 200m, new DateTime(2026, 2, 15));
        await ApplyAsync(SettlementDocType.PaymentEntry, betaPay, SettlementDocType.Invoice, b1, 200m);
        await InvoiceAsync(beta, 120m, new DateTime(2026, 6, 10));

        // 供应商侧：North 部分付款 + 一笔超付，South 只有一张未付账单。
        var n1 = await BillAsync(north, 700m, new DateTime(2026, 4, 5));
        var northPay = await PaymentAsync(PaymentDirection.Outbound, FinancePartyType.Vendor, north, 900m, new DateTime(2026, 5, 5));
        await ApplyAsync(SettlementDocType.PaymentEntry, northPay, SettlementDocType.Bill, n1, 600m);
        await BillAsync(south, 250m, new DateTime(2026, 6, 1));

        return (alpha, beta, north, south);
    }

    private static void ShouldMatch(AgingBucketsDto actual, AgingBucketsDto expected)
    {
        actual.Current.ShouldBe(expected.Current);
        actual.Days1To30.ShouldBe(expected.Days1To30);
        actual.Days31To60.ShouldBe(expected.Days31To60);
        actual.Days61To90.ShouldBe(expected.Days61To90);
        actual.Over90.ShouldBe(expected.Over90);
        actual.Total.ShouldBe(expected.Total);
        actual.AgingBucketDays.ShouldBe(expected.AgingBucketDays);
    }

    /// <summary>
    /// 同一段计算两种范围：单个往来方的账龄与全量账龄里他那一行逐字段相同，时点取今天与过去各一次
    /// （过去的时点早于全部核销的记账时刻，核销一概不算）。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartyAging_EqualsThatPartysRowOfTheFullAging(bool pastAsOf)
    {
        var (alpha, beta, north, south) = await SeedAsync();
        var asOf = pastAsOf ? AsOf : DateTime.UtcNow.Date.AddDays(1);

        var ar = await InScopeAsync<IFinancialReportService, Result<AgingReportDto>>(s => s.GetArAgingAsync(asOf));
        var ap = await InScopeAsync<IFinancialReportService, Result<AgingReportDto>>(s => s.GetApAgingAsync(asOf));
        ar.Succeeded.ShouldBeTrue(ar.Message);
        ap.Succeeded.ShouldBeTrue(ap.Message);

        foreach (var (type, id, full) in new[]
                 {
                     (FinancePartyType.Customer, alpha, ar.Data!), (FinancePartyType.Customer, beta, ar.Data!),
                     (FinancePartyType.Vendor, north, ap.Data!), (FinancePartyType.Vendor, south, ap.Data!)
                 })
        {
            var party = await InScopeAsync<IFinancialReportService, Result<AgingReportDto>>(s => s.GetPartyAgingAsync(type, id, asOf));
            party.Succeeded.ShouldBeTrue(party.Message);

            var expected = full.Rows.Single(r => r.PartyId == id);
            var row = party.Data!.Rows.ShouldHaveSingleItem();
            row.PartyId.ShouldBe(id);
            row.PartyName.ShouldBe(expected.PartyName);
            ShouldMatch(row, expected);
            ShouldMatch(party.Data.Totals, expected);
        }
    }

    [Fact]
    public async Task PartyAging_ForAPartyWithNothingOpen_HasNoRows()
    {
        await SeedCoaAsync();
        var idle = await CustomerAsync("Idle Co");

        var party = await InScopeAsync<IFinancialReportService, Result<AgingReportDto>>(
            s => s.GetPartyAgingAsync(FinancePartyType.Customer, idle, AsOf));

        party.Succeeded.ShouldBeTrue(party.Message);
        party.Data!.Rows.ShouldBeEmpty();
        party.Data.Totals.Total.ShouldBe(0m);
    }

    [Fact]
    public async Task PartySummary_ReadsOnlyThatPartysDocuments()
    {
        var (alpha, _, north, _) = await SeedAsync();

        foreach (var (type, id) in new[] { (FinancePartyType.Customer, alpha), (FinancePartyType.Vendor, north) })
        {
            _sql.Clear();
            var summary = await InScopeAsync<IPartyLedgerService, Result<PartyLedgerSummaryDto>>(s => s.GetSummaryAsync(type, id, AsOf));
            summary.Succeeded.ShouldBeTrue(summary.Message);

            // 读单据表与核销表的每一条命令都必须带着这个往来方：一旦退化成「先算整本账再挑一行」，
            // 这里就会出现不带往来方谓词的全表聚合。
            var documentReads = _sql.Commands
                .Where(c => PartyScopedTables.Any(t => c.Contains(t, StringComparison.Ordinal)))
                .ToList();
            documentReads.ShouldNotBeEmpty();
            documentReads.ShouldAllBe(c => c.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase),
                $"every document or settlement read for {type} {id} must be scoped to that party");
        }
    }
}
