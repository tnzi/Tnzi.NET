namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 客户对账单与催收：两种形态、与账龄同源、催收分级。
/// </summary>
public class CustomerStatementTests : FinanceIntegrationTestBase
{
    private Task<Result<CustomerStatementDto>> StatementAsync(Guid customerId, CustomerStatementQueryDto query)
        => InScopeAsync<ICustomerStatementService, Result<CustomerStatementDto>>(
            s => s.GetAsync(FinancePartyType.Customer, customerId, query));

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
            Lines = [new CreateInvoiceLineDto { AccountId = revenue, Quantity = 1, UnitPrice = amount }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!.Id;
    }

    /// <summary>
    /// ★核心不变量：对账单上的应付金额与账龄报表逐分相等。
    /// </summary>
    /// <remarks>
    /// 寄出去的那张纸与自己账上的数对不上，比不寄更糟。
    /// </remarks>
    [Fact]
    public async Task Statement_AmountDue_TiesOutWithTheAgingReport()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;
        await PostedInvoiceAsync(customer, 1200m, today.AddDays(-10), today.AddDays(20));
        await PostedInvoiceAsync(customer, 840m, today.AddDays(-70), today.AddDays(-40));

        var statement = await StatementAsync(customer, new CustomerStatementQueryDto { To = today });
        var aging = await InScopeAsync<IFinancialReportService, Result<AgingReportDto>>(s => s.GetArAgingAsync(today));

        statement.Succeeded.ShouldBeTrue(statement.Message);
        var agingRow = aging.Data!.Rows.Single(r => r.PartyId == customer);
        statement.Data!.ClosingBalance.ShouldBe(agingRow.Total);
        statement.Data.Buckets.Total.ShouldBe(agingRow.Total);
        statement.Data.Overdue.ShouldBe(agingRow.Total - agingRow.Current);
    }

    [Fact]
    public async Task Statement_OpenItem_ListsOnlyUnsettledDocuments()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;
        await PostedInvoiceAsync(customer, 500m, today.AddDays(-5));

        // 一笔未核销的收款不该出现在"你还欠我哪几张"里。
        var cash = await AccountIdByCodeAsync("1110");
        var payment = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = customer,
            DocDate = today,
            Currency = "USD",
            Amount = 120m,
            DepositToAccountId = cash,
        }));
        await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(payment.Data!.Id));

        var statement = await StatementAsync(customer,
            new CustomerStatementQueryDto { Style = StatementStyle.OpenItem, To = today });

        statement.Data!.Lines.Count.ShouldBe(1);
        statement.Data.Lines[0].Outstanding.ShouldBe(500m);
        // Open Item 没有期初余额的概念。
        statement.Data.OpeningBalance.ShouldBe(0m);
    }

    /// <summary>
    /// Activity 形态：逐行累计余额，末行必须落在期末余额上。
    /// </summary>
    [Fact]
    public async Task Statement_Activity_RunningBalanceEndsAtTheClosingBalance()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;
        await PostedInvoiceAsync(customer, 300m, today.AddDays(-20));
        await PostedInvoiceAsync(customer, 200m, today.AddDays(-10));

        var statement = await StatementAsync(customer, new CustomerStatementQueryDto
        {
            Style = StatementStyle.Activity,
            From = today.AddDays(-30),
            To = today,
        });

        statement.Succeeded.ShouldBeTrue(statement.Message);
        statement.Data!.Lines.Count.ShouldBe(2);
        statement.Data.Lines[^1].Balance.ShouldBe(statement.Data.ClosingBalance);
    }

    [Fact]
    public async Task Statement_PeriodInverted_Rejected400()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;

        var result = await StatementAsync(customer, new CustomerStatementQueryDto
        {
            Style = StatementStyle.Activity,
            From = today,
            To = today.AddDays(-5),
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Statement_NoActivity_IsAnEmptyStatement_NotAnError()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();

        var statement = await StatementAsync(customer, new CustomerStatementQueryDto());

        statement.Succeeded.ShouldBeTrue(statement.Message);
        statement.Data!.Lines.ShouldBeEmpty();
        statement.Data.ClosingBalance.ShouldBe(0m);
        statement.Data.DunningLevel.ShouldBe(DunningLevel.None);
    }

    // ── 催收 ────────────────────────────────────────────────

    [Fact]
    public async Task Dunning_EscalatesWithDaysPastDue()
    {
        var policy = ServiceProvider.GetRequiredService<IDunningPolicy>();

        // 阈值默认 30 / 60 天，最小金额 1。
        policy.Evaluate(0, 0m).ShouldBe(DunningLevel.None);
        policy.Evaluate(5, 500m).ShouldBe(DunningLevel.Reminder);
        policy.Evaluate(35, 500m).ShouldBe(DunningLevel.Overdue);
        policy.Evaluate(90, 500m).ShouldBe(DunningLevel.FinalNotice);
    }

    /// <summary>
    /// 小额不惊动人：为了三块钱发最后通知，只会让对方不再认真看这类邮件。
    /// </summary>
    [Fact]
    public async Task Dunning_IgnoresTrivialAmounts()
    {
        var policy = ServiceProvider.GetRequiredService<IDunningPolicy>();

        policy.Evaluate(200, 0.40m).ShouldBe(DunningLevel.None);
    }

    [Fact]
    public async Task DunningCandidates_ExcludeAnyoneNotOverdue_AndSortWorstFirst()
    {
        await SeedCoaAsync();
        var today = DateTime.UtcNow.Date;

        var current = await CustomerAsync("Pays On Time Ltd");
        await PostedInvoiceAsync(current, 900m, today.AddDays(-3), today.AddDays(27));

        var late = await CustomerAsync("Very Late Ltd");
        await PostedInvoiceAsync(late, 700m, today.AddDays(-120), today.AddDays(-95));

        var slightly = await CustomerAsync("Slightly Late Ltd");
        await PostedInvoiceAsync(slightly, 400m, today.AddDays(-40), today.AddDays(-10));

        var candidates = await InScopeAsync<ICustomerStatementService, Result<List<DunningCandidateDto>>>(
            s => s.GetDunningCandidatesAsync(FinancePartyType.Customer, today));

        candidates.Succeeded.ShouldBeTrue(candidates.Message);
        // 没逾期的不进催收名单。
        candidates.Data!.ShouldNotContain(c => c.PartyId == current);
        candidates.Data!.Count.ShouldBe(2);
        // 最该催的排最前。
        candidates.Data[0].PartyId.ShouldBe(late);
        candidates.Data[0].Level.ShouldBe(DunningLevel.FinalNotice);
    }
}
