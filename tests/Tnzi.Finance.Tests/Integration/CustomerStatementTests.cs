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

    // ── 流水口径与账龄一致：末行余额必须落在同一张纸的期末余额上 ──────────

    /// <summary>
    /// ★作废的单据账龄视为从未存在，流水也不能把它的金额累进余额。
    /// </summary>
    /// <remarks>
    /// 此前流水只排除草稿：本月作废的 500 照样累进去，末行比同一张纸上的应付金额多 500。
    /// </remarks>
    [Fact]
    public async Task Statement_Activity_VoidedInvoice_DoesNotMoveTheRunningBalance()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;
        var voided = await PostedInvoiceAsync(customer, 500m, today.AddDays(-12));
        await PostedInvoiceAsync(customer, 200m, today.AddDays(-5));
        (await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.VoidAsync(voided))).Succeeded.ShouldBeTrue();

        var statement = await StatementAsync(customer, new CustomerStatementQueryDto
        {
            Style = StatementStyle.Activity,
            From = today.AddDays(-30),
            To = today,
        });

        statement.Succeeded.ShouldBeTrue(statement.Message);
        statement.Data!.ClosingBalance.ShouldBe(200m);
        statement.Data.Lines.ShouldNotContain(l => l.DocId == voided);
        statement.Data.Lines[^1].Balance.ShouldBe(statement.Data.ClosingBalance);
    }

    /// <summary>
    /// ★费用直接付清、从不挂应付：供应商对账单不能把它当成一笔欠款累进去。
    /// </summary>
    [Fact]
    public async Task Statement_Activity_VendorDirectExpense_DoesNotMoveTheRunningBalance()
    {
        await SeedCoaAsync();
        var today = DateTime.UtcNow.Date;
        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = "Paper Co" }));
        vendor.Succeeded.ShouldBeTrue(vendor.Message);
        var vendorId = vendor.Data!.Id;
        var supplies = await AccountIdByCodeAsync("5200");
        var bank = await AccountIdByCodeAsync("1120");

        var bill = await InScopeAsync<IBillService, Result<BillDto>>(s => s.CreateDraftAsync(new CreateBillDto
        {
            VendorId = vendorId,
            DocDate = today.AddDays(-10),
            Currency = "USD",
            Lines = [new CreateBillLineDto { AccountId = supplies, Quantity = 1, UnitPrice = 300m }]
        }));
        bill.Succeeded.ShouldBeTrue(bill.Message);
        (await InScopeAsync<IBillService, Result<BillDto>>(s => s.PostAsync(bill.Data!.Id))).Succeeded.ShouldBeTrue();

        var expense = await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.CreateDraftAsync(new CreateExpenseDto
        {
            VendorId = vendorId,
            PaidFromAccountId = bank,
            DocDate = today.AddDays(-4),
            Currency = "USD",
            Lines = [new CreateExpenseLineDto { AccountId = supplies, Amount = 200m }]
        }));
        expense.Succeeded.ShouldBeTrue(expense.Message);
        (await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.PostAsync(expense.Data!.Id))).Succeeded.ShouldBeTrue();

        var statement = await InScopeAsync<ICustomerStatementService, Result<CustomerStatementDto>>(
            s => s.GetAsync(FinancePartyType.Vendor, vendorId, new CustomerStatementQueryDto
            {
                Style = StatementStyle.Activity,
                From = today.AddDays(-30),
                To = today,
            }));

        statement.Succeeded.ShouldBeTrue(statement.Message);
        statement.Data!.ClosingBalance.ShouldBe(300m);
        statement.Data.Lines.ShouldNotContain(l => l.DocType == FinanceSourceTypes.Expense);
        statement.Data.Lines[^1].Balance.ShouldBe(statement.Data.ClosingBalance);
    }

    /// <summary>
    /// ★期初与期末都是本位币：把 EUR 金额加进 USD 余额的对账单拒绝出，而不是寄出一张自相矛盾的纸。
    /// </summary>
    [Fact]
    public async Task Statement_WithForeignCurrencyDocuments_Rejected400()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;
        var revenue = await AccountIdByCodeAsync("4100");
        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customer,
            DocDate = today.AddDays(-3),
            Currency = "EUR",
            ExchangeRate = 1.10m,
            Lines = [new CreateInvoiceLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 100m }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        (await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id))).Succeeded.ShouldBeTrue();

        foreach (var style in new[] { StatementStyle.Activity, StatementStyle.OpenItem })
        {
            var statement = await StatementAsync(customer, new CustomerStatementQueryDto
            {
                Style = style,
                From = today.AddDays(-30),
                To = today,
            });

            statement.Succeeded.ShouldBeFalse($"{style}: the lines would add EUR amounts to a base-currency balance");
            statement.Code.ShouldBe(400);
            statement.Message.ShouldNotBeNull().ShouldContain("EUR");
        }
    }

    /// <summary>
    /// ★过去日期的 Open Item：按那一天的核销重建未清额，之后才付清的单据照样列出。
    /// </summary>
    /// <remarks>
    /// 此前用单据上当前的已核销额：今天付清的发票从「截至上周」的对账单上消失，而同一张纸上的
    /// 期末余额（按时点重建的账龄）仍把它算在里面。
    /// </remarks>
    [Fact]
    public async Task Statement_OpenItem_AsOfAPastDate_ListsWhatWasOpenThen()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;
        var invoice = await PostedInvoiceAsync(customer, 400m, today.AddDays(-30), today.AddDays(-20));

        var cash = await AccountIdByCodeAsync("1110");
        var payment = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = customer,
            DocDate = today,
            Currency = "USD",
            Amount = 400m,
            DepositToAccountId = cash,
        }));
        payment.Succeeded.ShouldBeTrue(payment.Message);
        (await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(payment.Data!.Id))).Succeeded.ShouldBeTrue();
        (await InScopeAsync<ISettlementService, Result<List<PaymentApplicationDto>>>(s => s.ApplyAsync(new ApplySettlementDto
        {
            SourceType = SettlementDocType.PaymentEntry,
            SourceId = payment.Data!.Id,
            Targets = [new ApplySettlementTargetDto { TargetType = SettlementDocType.Invoice, TargetId = invoice, Amount = 400m }]
        }))).Succeeded.ShouldBeTrue();

        var asOf = today.AddDays(-10);
        var statement = await StatementAsync(customer, new CustomerStatementQueryDto { Style = StatementStyle.OpenItem, To = asOf });

        statement.Succeeded.ShouldBeTrue(statement.Message);
        statement.Data!.ClosingBalance.ShouldBe(400m, "premise: as of that date the invoice was still owed");
        var line = statement.Data.Lines.ShouldHaveSingleItem();
        line.DocId.ShouldBe(invoice);
        line.Outstanding.ShouldBe(400m);
        line.OverdueDays.ShouldBe(10, "overdue days are counted to the statement date, not to today");

        // 今天的 Open Item 照旧：它已付清，不再列出。
        var current = await StatementAsync(customer, new CustomerStatementQueryDto { Style = StatementStyle.OpenItem, To = today });
        current.Data!.Lines.ShouldBeEmpty();
    }

    /// <summary>
    /// ★超过一页的流水不得被静默截断。
    /// </summary>
    /// <remarks>
    /// 此前以 <c>PageSize = 500</c> 读分页接口，而分页 DTO 把每页条数静默钳到 100：第 101 条起
    /// 无声消失，累计余额停在一个与期末余额对不上的数上，而 <c>TotalCount</c> 的超限检查照样放行。
    /// </remarks>
    [Fact]
    public async Task Statement_Activity_MoreThanOnePageOfDocuments_IsNotTruncated()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var today = DateTime.UtcNow.Date;
        for (var i = 0; i < 105; i++)
            await PostedInvoiceAsync(customer, 10m, today.AddDays(-20));

        var statement = await StatementAsync(customer, new CustomerStatementQueryDto
        {
            Style = StatementStyle.Activity,
            From = today.AddDays(-30),
            To = today,
        });

        statement.Succeeded.ShouldBeTrue(statement.Message);
        statement.Data!.Lines.Count.ShouldBe(105);
        statement.Data.ClosingBalance.ShouldBe(1050m);
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
