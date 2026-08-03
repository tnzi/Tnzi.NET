namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 批量结算（Pay Bills）、结算原子性回归、过账前钩子、结算方式字段与扩展点冲销
/// </summary>
public class BatchPaymentAndGuardTests : FinanceIntegrationTestBase
{
    private readonly TestPostingGuard _guard = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<IFinancePostingGuard>(_ => _guard);
    }

    /// <summary>可开关的测试钩子：Veto 时否决并记录看到的操作</summary>
    private sealed class TestPostingGuard : IFinancePostingGuard
    {
        public bool Veto { get; set; }
        public List<(string DocType, FinancePostingOperation Operation)> Seen { get; } = new();

        public Task<Result> CheckAsync(FinancePostingGuardContext context, CancellationToken cancellationToken = default)
        {
            Seen.Add((context.DocType, context.Operation));
            return Task.FromResult(Veto
                ? Result.Failure("Approval required before posting.", 403)
                : Result.Success());
        }
    }

    // ── 测试数据辅助 ─────────────────────────────────────────

    private async Task<Guid> AccountIdAsync(string code)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var account = await repo.FirstOrDefaultAsync(a => a.Code == code);
        account.ShouldNotBeNull($"account {code}");
        return account.Id;
    }

    private async Task<Guid> CreateVendorAsync(string name)
    {
        var result = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = name }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    private async Task<Guid> CreateCustomerAsync(string name)
    {
        var result = await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = name }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    private async Task<BillDto> PostBillAsync(Guid vendorId, decimal amount, string? currency = null, decimal? rate = null)
    {
        var expenseAccount = await AccountIdAsync("5200");
        var draft = await InScopeAsync<IBillService, Result<BillDto>>(s => s.CreateDraftAsync(new CreateBillDto
        {
            VendorId = vendorId,
            DocDate = new DateTime(2026, 4, 1),
            Currency = currency,
            ExchangeRate = rate,
            Lines = [new CreateBillLineDto { Description = "Service", AccountId = expenseAccount, Quantity = 1, UnitPrice = amount }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IBillService, Result<BillDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!;
    }

    private async Task<InvoiceDto> PostInvoiceAsync(Guid customerId, decimal amount, string? currency = null, decimal? rate = null)
    {
        var income = await AccountIdAsync("4100");
        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customerId,
            DocDate = new DateTime(2026, 4, 1),
            Currency = currency,
            ExchangeRate = rate,
            Lines = [new CreateInvoiceLineDto { AccountId = income, Quantity = 1, UnitPrice = amount }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!;
    }

    private Task<Result<BatchPaymentResultDto>> PayAsync(BatchPaymentDto input)
        => InScopeAsync<ISettlementService, Result<BatchPaymentResultDto>>(s => s.PayAsync(input));

    // ── 批量结算 ─────────────────────────────────────────────

    [Fact]
    public async Task Pay_CrossVendorBills_GroupsByParty_PostsAndSettles()
    {
        await SeedCoaAsync();
        var vendorA = await CreateVendorAsync("Batch Vendor A");
        var vendorB = await CreateVendorAsync("Batch Vendor B");
        var billA1 = await PostBillAsync(vendorA, 100m);
        var billA2 = await PostBillAsync(vendorA, 50m);
        var billB1 = await PostBillAsync(vendorB, 80m);
        var bank = await AccountIdAsync("1120");

        var result = await PayAsync(new BatchPaymentDto
        {
            DocDate = new DateTime(2026, 4, 10),
            FundsAccountId = bank,
            PaymentMethod = PaymentMethods.Check,
            Memo = "April pay run",
            Targets =
            [
                new BatchPaymentTargetDto { DocType = SettlementDocType.Bill, DocId = billA1.Id, Amount = 100m },
                new BatchPaymentTargetDto { DocType = SettlementDocType.Bill, DocId = billA2.Id, Amount = 50m },
                new BatchPaymentTargetDto { DocType = SettlementDocType.Bill, DocId = billB1.Id, Amount = 80m }
            ]
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        // 每（往来方 + 币种）一张付款单：A 合并 150、B 单独 80
        result.Data!.Payments.Count.ShouldBe(2);
        result.Data.Applications.Count.ShouldBe(3);
        result.Data.Payments.Select(p => p.Amount).OrderBy(a => a).ShouldBe([80m, 150m]);
        result.Data.Payments.ShouldAllBe(p => p.Direction == PaymentDirection.Outbound);
        result.Data.Payments.ShouldAllBe(p => p.Status == FinanceDocumentStatus.Posted);
        result.Data.Payments.ShouldAllBe(p => p.PaymentMethod == PaymentMethods.Check);
        result.Data.Payments.ShouldAllBe(p => p.AppliedTotal == p.Amount);

        foreach (var billId in new[] { billA1.Id, billA2.Id, billB1.Id })
        {
            var bill = await InScopeAsync<IBillService, Result<BillDto>>(s => s.GetAsync(billId));
            bill.Data!.Status.ShouldBe(FinanceDocumentStatus.Paid);
        }
    }

    [Fact]
    public async Task Pay_PartialAmounts_LeaveBillsPartiallyPaid()
    {
        await SeedCoaAsync();
        var vendor = await CreateVendorAsync("Batch Vendor C");
        var bill = await PostBillAsync(vendor, 200m);
        var bank = await AccountIdAsync("1120");

        var result = await PayAsync(new BatchPaymentDto
        {
            DocDate = new DateTime(2026, 4, 10),
            FundsAccountId = bank,
            Targets = [new BatchPaymentTargetDto { DocType = SettlementDocType.Bill, DocId = bill.Id, Amount = 120m }]
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        var after = await InScopeAsync<IBillService, Result<BillDto>>(s => s.GetAsync(bill.Id));
        after.Data!.Status.ShouldBe(FinanceDocumentStatus.PartiallyPaid);
        after.Data.AppliedTotal.ShouldBe(120m);
    }

    [Fact]
    public async Task Pay_Invoices_CreatesInboundReceipts()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Batch Customer");
        var invoice = await PostInvoiceAsync(customer, 90m);
        var bank = await AccountIdAsync("1120");

        var result = await PayAsync(new BatchPaymentDto
        {
            DocDate = new DateTime(2026, 4, 11),
            FundsAccountId = bank,
            PaymentMethod = PaymentMethods.BankTransfer,
            Targets = [new BatchPaymentTargetDto { DocType = SettlementDocType.Invoice, DocId = invoice.Id, Amount = 90m }]
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Payments.Single().Direction.ShouldBe(PaymentDirection.Inbound);
        (await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.GetAsync(invoice.Id)))
            .Data!.Status.ShouldBe(FinanceDocumentStatus.Paid);
    }

    [Fact]
    public async Task Pay_InputValidation_RejectsBadBatches()
    {
        await SeedCoaAsync();
        var vendor = await CreateVendorAsync("Batch Vendor D");
        var bill = await PostBillAsync(vendor, 100m);
        var customer = await CreateCustomerAsync("Batch Customer D");
        var invoice = await PostInvoiceAsync(customer, 100m);
        var bank = await AccountIdAsync("1120");

        // 混合 Invoice + Bill
        (await PayAsync(new BatchPaymentDto
        {
            DocDate = new DateTime(2026, 4, 10),
            FundsAccountId = bank,
            Targets =
            [
                new BatchPaymentTargetDto { DocType = SettlementDocType.Bill, DocId = bill.Id, Amount = 50m },
                new BatchPaymentTargetDto { DocType = SettlementDocType.Invoice, DocId = invoice.Id, Amount = 50m }
            ]
        })).Succeeded.ShouldBeFalse();

        // 重复目标
        (await PayAsync(new BatchPaymentDto
        {
            DocDate = new DateTime(2026, 4, 10),
            FundsAccountId = bank,
            Targets =
            [
                new BatchPaymentTargetDto { DocType = SettlementDocType.Bill, DocId = bill.Id, Amount = 40m },
                new BatchPaymentTargetDto { DocType = SettlementDocType.Bill, DocId = bill.Id, Amount = 40m }
            ]
        })).Succeeded.ShouldBeFalse();

        // 超未清
        (await PayAsync(new BatchPaymentDto
        {
            DocDate = new DateTime(2026, 4, 10),
            FundsAccountId = bank,
            Targets = [new BatchPaymentTargetDto { DocType = SettlementDocType.Bill, DocId = bill.Id, Amount = 150m }]
        })).Succeeded.ShouldBeFalse();

        // 全部拒绝后无任何付款单产生
        var payments = await InScopeAsync<IPaymentEntryService, Result<IPagedList<PaymentEntryDto>>>(
            s => s.GetPagedAsync(new PaymentEntryQueryDto()));
        payments.Data!.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Pay_MidBatchFailure_RollsBackWholeBatch()
    {
        await SeedCoaAsync();
        var vendorA = await CreateVendorAsync("Batch Vendor E");
        var vendorB = await CreateVendorAsync("Batch Vendor F");
        var billUsd = await PostBillAsync(vendorA, 100m);
        // EUR 账单以显式汇率过账；汇率表无 EUR 记录 → 批量付款为它生成的 EUR 付款单过账时解析汇率失败
        var billEur = await PostBillAsync(vendorB, 80m, currency: "EUR", rate: 1.1m);
        var bank = await AccountIdAsync("1120");

        var result = await PayAsync(new BatchPaymentDto
        {
            DocDate = new DateTime(2026, 4, 10),
            FundsAccountId = bank,
            Targets =
            [
                new BatchPaymentTargetDto { DocType = SettlementDocType.Bill, DocId = billUsd.Id, Amount = 100m },
                new BatchPaymentTargetDto { DocType = SettlementDocType.Bill, DocId = billEur.Id, Amount = 80m }
            ]
        });

        result.Succeeded.ShouldBeFalse();

        // 第一组（USD）已在事务内完成过账+核销，但整批回滚后不留任何痕迹
        var usdBill = await InScopeAsync<IBillService, Result<BillDto>>(s => s.GetAsync(billUsd.Id));
        usdBill.Data!.Status.ShouldBe(FinanceDocumentStatus.Posted);
        usdBill.Data.AppliedTotal.ShouldBe(0m);

        var payments = await InScopeAsync<IPaymentEntryService, Result<IPagedList<PaymentEntryDto>>>(
            s => s.GetPagedAsync(new PaymentEntryQueryDto()));
        payments.Data!.TotalCount.ShouldBe(0);

        var applications = await InScopeAsync<ISettlementService, Result<List<PaymentApplicationDto>>>(
            s => s.GetApplicationsAsync(SettlementDocType.Bill, billUsd.Id));
        applications.Data!.ShouldBeEmpty();
    }

    // ── ApplyAsync 原子性回归（部分提交防护）────────────────────

    [Fact]
    public async Task Apply_MidLoopFailure_DoesNotPersistEarlierAllocations()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Atomic Customer");
        var invoiceUsd = await PostInvoiceAsync(customer, 100m);
        var invoiceEur = await PostInvoiceAsync(customer, 50m, currency: "EUR", rate: 1.1m);

        var bank = await AccountIdAsync("1120");
        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = customer,
            DocDate = new DateTime(2026, 4, 5),
            Amount = 150m,
            DepositToAccountId = bank
        }));
        var payment = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        payment.Succeeded.ShouldBeTrue(payment.Message);

        // 目标 1（USD 发票）在循环内先被处理并写入，目标 2（EUR 发票）币种不匹配失败
        var applied = await InScopeAsync<ISettlementService, Result<List<PaymentApplicationDto>>>(s => s.ApplyAsync(new ApplySettlementDto
        {
            SourceType = SettlementDocType.PaymentEntry,
            SourceId = payment.Data!.Id,
            Targets =
            [
                new ApplySettlementTargetDto { TargetType = SettlementDocType.Invoice, TargetId = invoiceUsd.Id, Amount = 100m },
                new ApplySettlementTargetDto { TargetType = SettlementDocType.Invoice, TargetId = invoiceEur.Id, Amount = 50m }
            ]
        }));

        applied.Succeeded.ShouldBeFalse();

        // 目标 1 的核销必须随事务整体回滚
        var usdInvoice = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.GetAsync(invoiceUsd.Id));
        usdInvoice.Data!.Status.ShouldBe(FinanceDocumentStatus.Posted);
        usdInvoice.Data.AppliedTotal.ShouldBe(0m);

        var paymentAfter = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.GetAsync(payment.Data!.Id));
        paymentAfter.Data!.AppliedTotal.ShouldBe(0m);

        var applications = await InScopeAsync<ISettlementService, Result<List<PaymentApplicationDto>>>(
            s => s.GetApplicationsAsync(SettlementDocType.PaymentEntry, payment.Data!.Id));
        applications.Data!.ShouldBeEmpty();
    }

    // ── 过账前钩子 ───────────────────────────────────────────

    [Fact]
    public async Task PostingGuard_VetoBlocksPostVoidAndReverse()
    {
        await SeedCoaAsync();
        var vendor = await CreateVendorAsync("Guard Vendor");
        var expenseAccount = await AccountIdAsync("5200");

        var draft = await InScopeAsync<IBillService, Result<BillDto>>(s => s.CreateDraftAsync(new CreateBillDto
        {
            VendorId = vendor,
            DocDate = new DateTime(2026, 4, 1),
            Lines = [new CreateBillLineDto { AccountId = expenseAccount, Quantity = 1, UnitPrice = 100m }]
        }));

        // 否决过账：单据保持草稿，返回钩子给出的 403 与原因
        _guard.Veto = true;
        var vetoed = await InScopeAsync<IBillService, Result<BillDto>>(s => s.PostAsync(draft.Data!.Id));
        vetoed.Succeeded.ShouldBeFalse();
        vetoed.Code.ShouldBe(403);
        vetoed.Message.ShouldBe("Approval required before posting.");
        (await ReloadAsync<Bill>(draft.Data!.Id))!.Status.ShouldBe(FinanceDocumentStatus.Draft);
        _guard.Seen.ShouldContain((nameof(Bill), FinancePostingOperation.Post));

        // 放行后过账成功
        _guard.Veto = false;
        var posted = await InScopeAsync<IBillService, Result<BillDto>>(s => s.PostAsync(draft.Data.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        // 否决作废
        _guard.Veto = true;
        var voidVetoed = await InScopeAsync<IBillService, Result<BillDto>>(s => s.VoidAsync(draft.Data.Id));
        voidVetoed.Code.ShouldBe(403);
        _guard.Seen.ShouldContain((nameof(Bill), FinancePostingOperation.Void));

        // 否决手工凭证冲销
        _guard.Veto = false;
        var entry = await PostLedgerAsync(SimpleSale(60m, new DateTime(2026, 4, 2)));
        entry.Succeeded.ShouldBeTrue(entry.Message);
        _guard.Seen.ShouldContain(("Test.Sale", FinancePostingOperation.Post));

        _guard.Veto = true;
        var reverseVetoed = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(entry.Data!.Id, new ReverseJournalEntryDto()));
        reverseVetoed.Code.ShouldBe(403);
        _guard.Seen.ShouldContain((nameof(JournalEntry), FinancePostingOperation.Reverse));
    }

    // ── 结算方式字段 ─────────────────────────────────────────

    [Fact]
    public async Task PaymentMethod_RoundTripsAndFilters()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Method Customer");
        var bank = await AccountIdAsync("1120");

        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = customer,
            DocDate = new DateTime(2026, 4, 3),
            Amount = 30m,
            DepositToAccountId = bank,
            PaymentMethod = PaymentMethods.Check,
            Reference = "CHK-1001"
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.PaymentMethod.ShouldBe(PaymentMethods.Check);

        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data.Id));
        posted.Data!.PaymentMethod.ShouldBe(PaymentMethods.Check);

        // 查询维度过滤
        var byCheck = await InScopeAsync<IPaymentEntryService, Result<IPagedList<PaymentEntryDto>>>(
            s => s.GetPagedAsync(new PaymentEntryQueryDto { PaymentMethod = PaymentMethods.Check }));
        byCheck.Data!.TotalCount.ShouldBe(1);
        var byCash = await InScopeAsync<IPaymentEntryService, Result<IPagedList<PaymentEntryDto>>>(
            s => s.GetPagedAsync(new PaymentEntryQueryDto { PaymentMethod = PaymentMethods.Cash }));
        byCash.Data!.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Expense_PaymentMethod_RoundTrips()
    {
        await SeedCoaAsync();
        var bank = await AccountIdAsync("1120");
        var opex = await AccountIdAsync("5200");

        var draft = await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.CreateDraftAsync(new CreateExpenseDto
        {
            PaidFromAccountId = bank,
            PaymentMethod = PaymentMethods.CreditCard,
            DocDate = new DateTime(2026, 4, 4),
            Lines = [new CreateExpenseLineDto { Description = "Subscription", AccountId = opex, Amount = 25m }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.PaymentMethod.ShouldBe(PaymentMethods.CreditCard);

        var list = await InScopeAsync<IExpenseService, Result<IPagedList<ExpenseDto>>>(
            s => s.GetPagedAsync(new ExpenseQueryDto { PaymentMethod = PaymentMethods.CreditCard }));
        list.Data!.TotalCount.ShouldBe(1);
        list.Data.Items[0].PaymentMethod.ShouldBe(PaymentMethods.CreditCard);
    }

    // ── 扩展点冲销 ───────────────────────────────────────────

    [Fact]
    public async Task LedgerPosting_ReverseAsync_ReversesConsumerDocumentEntry()
    {
        await SeedCoaAsync();
        var posted = await PostLedgerAsync(SimpleSale(200m, new DateTime(2026, 4, 6), sourceId: "order-42"));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        // 消费应用作废自定义单据的推荐路径：按来源反查 → 冲销
        var bySource = await InScopeAsync<ILedgerPostingService, Result<List<JournalEntryDto>>>(
            s => s.GetBySourceAsync("Test.Sale", "order-42"));
        var entryId = bySource.Data!.Single().Id;

        var reversed = await InScopeAsync<ILedgerPostingService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(entryId));
        reversed.Succeeded.ShouldBeTrue(reversed.Message);
        reversed.Data!.TotalDebit.ShouldBe(posted.Data!.TotalDebit);
        reversed.Data.ReversalOfEntryId.ShouldBe(entryId);

        var original = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(entryId));
        original.Data!.Status.ShouldBe(JournalEntryStatus.Reversed);

        // 重复冲销拒绝
        (await InScopeAsync<ILedgerPostingService, Result<JournalEntryDto>>(s => s.ReverseAsync(entryId)))
            .Code.ShouldBe(409);
    }
}
