namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 往来方工作面：概览数字与账龄报表同源、交易流水的符号与逾期口径。
/// </summary>
public class PartyLedgerTests : FinanceIntegrationTestBase
{
    private Task<Result<PartyLedgerSummaryDto>> SummaryAsync(FinancePartyType type, Guid id, DateTime? asOf = null)
        => InScopeAsync<IPartyLedgerService, Result<PartyLedgerSummaryDto>>(s => s.GetSummaryAsync(type, id, asOf));

    private Task<Result<IPagedList<PartyLedgerEntryDto>>> TransactionsAsync(
        FinancePartyType type, Guid id, PartyLedgerQueryDto? query = null)
        => InScopeAsync<IPartyLedgerService, Result<IPagedList<PartyLedgerEntryDto>>>(
            s => s.GetTransactionsAsync(type, id, query ?? new PartyLedgerQueryDto { PageIndex = 1, PageSize = 50 }));

    private async Task<Guid> CustomerAsync(string name = "Acme Supplies Ltd")
    {
        var r = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = name, Currency = "USD", PaymentTermsDays = 30 }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task<Guid> PostedInvoiceAsync(Guid customerId, decimal amount, DateTime docDate, DateTime? dueDate = null)
    {
        var revenue = await AccountIdByCodeAsync("4100");
        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customerId,
            DocDate = docDate,
            DueDate = dueDate,
            Currency = "USD",
            Lines = new List<CreateInvoiceLineDto> { new() { AccountId = revenue, Description = "work", Quantity = 1, UnitPrice = amount } }
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!.Id;
    }

    [Fact]
    public async Task Summary_UnknownParty_Returns404()
    {
        await SeedCoaAsync();
        var result = await SummaryAsync(FinancePartyType.Customer, Guid.NewGuid());
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Summary_NoActivity_IsAllZero_NotAnError()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();

        var summary = await SummaryAsync(FinancePartyType.Customer, customer);

        summary.Succeeded.ShouldBeTrue(summary.Message);
        summary.Data!.OpenBalance.ShouldBe(0m);
        summary.Data.Overdue.ShouldBe(0m);
        summary.Data.PeriodTotal.ShouldBe(0m);
        summary.Data.OpenDocumentCount.ShouldBe(0);
        summary.Data.LastTransactionDate.ShouldBeNull();
    }

    /// <summary>
    /// ★核心不变量：客户页的未清余额必须与账龄报表**逐分相等**。
    /// </summary>
    /// <remarks>
    /// 两个屏幕给出两个"他欠多少"是财务软件最伤信任的失败模式。本页的余额刻意复用账龄的同一段
    /// 计算而不是另写一遍，本例就是那条约束的可执行证明。
    /// </remarks>
    [Fact]
    public async Task Summary_OpenBalance_TiesOutWithTheAgingReport()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var asOf = DateTime.UtcNow.Date;

        await PostedInvoiceAsync(customer, 1200m, asOf.AddDays(-10), asOf.AddDays(20));   // 未到期
        await PostedInvoiceAsync(customer, 840m, asOf.AddDays(-70), asOf.AddDays(-40));   // 逾期 40 天

        var summary = await SummaryAsync(FinancePartyType.Customer, customer, asOf);
        var aging = await InScopeAsync<IFinancialReportService, Result<AgingReportDto>>(s => s.GetArAgingAsync(asOf));

        summary.Succeeded.ShouldBeTrue(summary.Message);
        aging.Succeeded.ShouldBeTrue(aging.Message);

        var agingRow = aging.Data!.Rows.Single(r => r.PartyId == customer);
        summary.Data!.OpenBalance.ShouldBe(agingRow.Total);
        summary.Data.Buckets.Current.ShouldBe(agingRow.Current);
        summary.Data.Buckets.Days31To60.ShouldBe(agingRow.Days31To60);
        summary.Data.Buckets.Total.ShouldBe(agingRow.Total);

        // 逾期 = 总额 − Current（对分桶方案免疫，将来分桶参数化后仍然成立）
        summary.Data.Overdue.ShouldBe(agingRow.Total - agingRow.Current);
        summary.Data.Overdue.ShouldBe(840m);
    }

    [Fact]
    public async Task Summary_PeriodTotal_CountsPostedSalesOnly()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;

        await PostedInvoiceAsync(customer, 500m, today.AddDays(-5));
        // 草稿不该计入"这期做了多少生意"：它还不是事实
        var revenue = await AccountIdByCodeAsync("4100");
        await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customer,
            DocDate = today,
            Currency = "USD",
            Lines = new List<CreateInvoiceLineDto> { new() { AccountId = revenue, Quantity = 1, UnitPrice = 9999m } }
        }));

        var summary = await SummaryAsync(FinancePartyType.Customer, customer, today);

        summary.Data!.PeriodTotal.ShouldBe(500m);
    }

    [Fact]
    public async Task Transactions_AreSignedByDirection_AndSortedNewestFirst()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;
        var cash = await AccountIdByCodeAsync("1110");

        await PostedInvoiceAsync(customer, 1000m, today.AddDays(-20));
        var payment = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = customer,
            DocDate = today.AddDays(-2),
            Currency = "USD",
            Amount = 400m,
            DepositToAccountId = cash,
        }));
        payment.Succeeded.ShouldBeTrue(payment.Message);
        (await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(payment.Data!.Id))).Succeeded.ShouldBeTrue();

        var txns = await TransactionsAsync(FinancePartyType.Customer, customer);

        txns.Succeeded.ShouldBeTrue(txns.Message);
        var items = txns.Data!.Items;
        items.Count.ShouldBe(2);

        // 最近的在最上（网银式）
        items[0].DocType.ShouldBe(FinanceSourceTypes.PaymentEntry);
        // 收款减少欠款 → 负号。呈现端读符号即知方向，不必按 DocType 分支猜。
        items[0].Amount.ShouldBe(-400m);
        items[1].DocType.ShouldBe(FinanceSourceTypes.Invoice);
        items[1].Amount.ShouldBe(1000m);
    }

    [Fact]
    public async Task Transactions_OverdueDays_OnlyCountForStillOpenDocuments()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;

        var overdue = await PostedInvoiceAsync(customer, 300m, today.AddDays(-60), today.AddDays(-30));
        var paidLate = await PostedInvoiceAsync(customer, 200m, today.AddDays(-60), today.AddDays(-30));

        // 把第二张付清：它当初拖过期，但今天不再是逾期
        var cash = await AccountIdByCodeAsync("1110");
        var payment = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = customer,
            DocDate = today,
            Currency = "USD",
            Amount = 200m,
            DepositToAccountId = cash,
        }));
        await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(payment.Data!.Id));
        (await InScopeAsync<ISettlementService, Result<List<PaymentApplicationDto>>>(s => s.ApplyAsync(new ApplySettlementDto
        {
            SourceType = SettlementDocType.PaymentEntry,
            SourceId = payment.Data!.Id,
            Targets = new List<ApplySettlementTargetDto>
            {
                new() { TargetType = SettlementDocType.Invoice, TargetId = paidLate, Amount = 200m }
            }
        }))).Succeeded.ShouldBeTrue();

        var txns = await TransactionsAsync(FinancePartyType.Customer, customer);
        var byId = txns.Data!.Items.Where(i => i.DocType == FinanceSourceTypes.Invoice).ToDictionary(i => i.DocId);

        byId[overdue].OverdueDays.ShouldBeGreaterThan(0);
        byId[overdue].Outstanding.ShouldBe(300m);
        byId[paidLate].OverdueDays.ShouldBe(0);
        byId[paidLate].Outstanding.ShouldBe(0m);
    }

    [Fact]
    public async Task Transactions_OpenOnly_FiltersToUnsettledDocuments()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;
        await PostedInvoiceAsync(customer, 700m, today.AddDays(-3));

        var open = await TransactionsAsync(FinancePartyType.Customer, customer,
            new PartyLedgerQueryDto { PageIndex = 1, PageSize = 50, OpenOnly = true });

        open.Data!.Items.Count.ShouldBe(1);
        open.Data.Items[0].Outstanding.ShouldBe(700m);
    }

    [Fact]
    public async Task Transactions_ExcludeDrafts()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var revenue = await AccountIdByCodeAsync("4100");
        await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customer,
            DocDate = DateTime.UtcNow.Date,
            Currency = "USD",
            Lines = new List<CreateInvoiceLineDto> { new() { AccountId = revenue, Quantity = 1, UnitPrice = 100m } }
        }));

        // 草稿还没发生，不该出现在"这个客户发生了什么"的流水里
        var txns = await TransactionsAsync(FinancePartyType.Customer, customer);
        txns.Data!.Items.ShouldBeEmpty();
    }
}
