namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 业务单据工作流：草稿 → 过账（GL 投影恒等式 + 税拆行 + 编号）→ 作废（冲销归零）
/// </summary>
public class DocumentWorkflowTests : FinanceIntegrationTestBase
{
    private async Task<Guid> AccountIdAsync(string code)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var account = await repo.FirstOrDefaultAsync(a => a.Code == code);
        account.ShouldNotBeNull($"account {code}");
        return account.Id;
    }

    private Task<Result<CustomerDto>> CreateCustomerAsync(string name = "Doc Customer")
        => InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = name }));

    private Task<Result<VendorDto>> CreateVendorAsync(string name = "Doc Vendor")
        => InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = name }));

    private async Task<Guid> CreateTaxCodeAsync(decimal rate = 5m)
    {
        var agency = await InScopeAsync<ITaxService, Result<TaxAgencyDto>>(s => s.CreateAgencyAsync(new UpsertTaxAgencyDto { Name = $"Agency {Guid.NewGuid():N}" }));
        var taxRate = await InScopeAsync<ITaxService, Result<TaxRateDto>>(s => s.CreateRateAsync(new UpsertTaxRateDto
        {
            AgencyId = agency.Data!.Id,
            Name = $"Tax {rate}%",
            Rate = rate
        }));
        var code = await InScopeAsync<ITaxService, Result<TaxCodeDto>>(s => s.CreateCodeAsync(new UpsertTaxCodeDto
        {
            Name = $"Code {Guid.NewGuid():N}",
            Components = [new UpsertTaxCodeComponentDto { TaxRateId = taxRate.Data!.Id, Order = 1 }]
        }));
        code.Succeeded.ShouldBeTrue(code.Message);
        return code.Data!.Id;
    }

    [Fact]
    public async Task Invoice_FullLifecycle_PostsBalancedGlAndVoids()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync();
        var taxCodeId = await CreateTaxCodeAsync(5m);

        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customer.Data!.Id,
            DocDate = new DateTime(2026, 3, 10),
            Lines =
            [
                new CreateInvoiceLineDto { Description = "Consulting", AccountId = null, ItemId = null, Quantity = 2, UnitPrice = 100m, TaxCodeId = taxCodeId },
            ]
        }));
        // 行无科目且无目录项 → 草稿允许，过账拒绝
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var postNoAccount = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id));
        postNoAccount.Succeeded.ShouldBeFalse();

        // 指定收入科目后过账
        var income = await AccountIdAsync("4100");
        var updated = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.UpdateDraftAsync(draft.Data!.Id, new CreateInvoiceDto
        {
            CustomerId = customer.Data!.Id,
            DocDate = new DateTime(2026, 3, 10),
            Lines =
            [
                new CreateInvoiceLineDto { Description = "Consulting", AccountId = income, Quantity = 2, UnitPrice = 100m, TaxCodeId = taxCodeId },
            ]
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.SubTotal.ShouldBe(200m);
        updated.Data.TaxTotal.ShouldBe(10m);
        updated.Data.Total.ShouldBe(210m);

        var posted = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.Status.ShouldBe(FinanceDocumentStatus.Posted);
        posted.Data.Number.ShouldBe("INV-000001");
        posted.Data.BaseTotal.ShouldBe(210m);
        posted.Data.DueDate.ShouldNotBeNull();
        posted.Data.JournalEntryId.ShouldNotBeNull();

        // GL 投影恒等式：AR 借 210 = 收入贷 200 + 税贷 10
        var entry = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(posted.Data.JournalEntryId!.Value));
        entry.Succeeded.ShouldBeTrue(entry.Message);
        entry.Data!.TotalDebit.ShouldBe(210m);
        entry.Data.TotalCredit.ShouldBe(210m);
        entry.Data.SourceType.ShouldBe("Invoice");
        entry.Data.Lines.Count.ShouldBe(3);

        // 过账后不可编辑/删除
        (await InScopeAsync<IInvoiceService, Result>(s => s.DeleteDraftAsync(draft.Data!.Id))).Code.ShouldBe(409);

        // 作废：冲销后 TB 归零
        var voided = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.VoidAsync(draft.Data!.Id));
        voided.Succeeded.ShouldBeTrue(voided.Message);
        voided.Data!.Status.ShouldBe(FinanceDocumentStatus.Voided);
        voided.Data.VoidJournalEntryId.ShouldNotBeNull();

        var tb = await InScopeAsync<IFinancialReportService, Result<TrialBalanceReportDto>>(
            s => s.GetTrialBalanceAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
        tb.Data!.TotalClosingBalance.ShouldBe(0m);
        tb.Data.Rows.All(r => r.ClosingBalance == 0m).ShouldBeTrue();
    }

    [Fact]
    public async Task Bill_Post_DebitsExpenseAndTaxReceivable_CreditsAp()
    {
        await SeedCoaAsync();
        var vendor = await CreateVendorAsync();
        var taxCodeId = await CreateTaxCodeAsync(10m);
        var expenseAccount = await AccountIdAsync("5200");

        var draft = await InScopeAsync<IBillService, Result<BillDto>>(s => s.CreateDraftAsync(new CreateBillDto
        {
            VendorId = vendor.Data!.Id,
            DocDate = new DateTime(2026, 4, 1),
            Lines = [new CreateBillLineDto { Description = "Rent", AccountId = expenseAccount, Quantity = 1, UnitPrice = 1000m, TaxCodeId = taxCodeId }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<IBillService, Result<BillDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.Number.ShouldBe("BILL-000001");
        posted.Data.Total.ShouldBe(1100m);

        var entry = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(posted.Data.JournalEntryId!.Value));
        entry.Data!.TotalDebit.ShouldBe(1100m);
        // AP 控制科目在贷方
        var apId = await AccountIdAsync("2100");
        entry.Data.Lines.Single(l => l.AccountId == apId).Credit.ShouldBe(1100m);
        // 进项税在借方
        var taxRecvId = await AccountIdAsync("1300");
        entry.Data.Lines.Single(l => l.AccountId == taxRecvId).Debit.ShouldBe(100m);
    }

    [Fact]
    public async Task Expense_Post_CreditsPaidFromAccount()
    {
        await SeedCoaAsync();
        var vendor = await CreateVendorAsync("Expense Vendor");
        var bank = await AccountIdAsync("1120");
        var opex = await AccountIdAsync("5200");

        var draft = await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.CreateDraftAsync(new CreateExpenseDto
        {
            VendorId = vendor.Data!.Id,
            PaidFromAccountId = bank,
            DocDate = new DateTime(2026, 4, 2),
            Lines = [new CreateExpenseLineDto { Description = "Office supplies", AccountId = opex, Amount = 50m }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.Number.ShouldBe("EXP-000001");
        // 详情投影解析供应商名与付款科目名（镜像 Bill）
        posted.Data.VendorName.ShouldBe("Expense Vendor");
        posted.Data.PaidFromAccountName.ShouldNotBeNullOrWhiteSpace();

        var entry = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(posted.Data.JournalEntryId!.Value));
        entry.Data!.Lines.Single(l => l.AccountId == bank).Credit.ShouldBe(50m);
        entry.Data.Lines.Single(l => l.AccountId == opex).Debit.ShouldBe(50m);

        // 作废
        var voided = await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.VoidAsync(draft.Data!.Id));
        voided.Succeeded.ShouldBeTrue(voided.Message);
    }

    [Fact]
    public async Task CreditMemo_Post_MirrorsInvoiceDirections()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("CM Customer");
        var income = await AccountIdAsync("4100");

        var draft = await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.CreateDraftAsync(new CreateCreditMemoDto
        {
            CustomerId = customer.Data!.Id,
            DocDate = new DateTime(2026, 4, 3),
            Lines = [new CreateCreditMemoLineDto { Description = "Refund", AccountId = income, Quantity = 1, UnitPrice = 80m }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.Number.ShouldBe("CM-000001");

        var entry = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(posted.Data.JournalEntryId!.Value));
        var arId = await AccountIdAsync("1200");
        // 镜像：AR 在贷方、收入在借方
        entry.Data!.Lines.Single(l => l.AccountId == arId).Credit.ShouldBe(80m);
        entry.Data.Lines.Single(l => l.AccountId == income).Debit.ShouldBe(80m);
    }

    [Fact]
    public async Task Payment_InboundAndOutbound_PostToControlAccounts()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Pay Customer");
        var vendor = await CreateVendorAsync("Pay Vendor");
        var bank = await AccountIdAsync("1120");
        var arId = await AccountIdAsync("1200");
        var apId = await AccountIdAsync("2100");

        // 收款
        var inbound = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = customer.Data!.Id,
            DocDate = new DateTime(2026, 4, 5),
            Amount = 210m,
            DepositToAccountId = bank
        }));
        inbound.Succeeded.ShouldBeTrue(inbound.Message);

        var postedIn = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(inbound.Data!.Id));
        postedIn.Succeeded.ShouldBeTrue(postedIn.Message);
        postedIn.Data!.Number.ShouldBe("PMT-000001");
        postedIn.Data.BaseAmount.ShouldBe(210m);

        var entryIn = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(postedIn.Data.JournalEntryId!.Value));
        entryIn.Data!.Lines.Single(l => l.AccountId == bank).Debit.ShouldBe(210m);
        entryIn.Data.Lines.Single(l => l.AccountId == arId).Credit.ShouldBe(210m);

        // 付款
        var outbound = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Outbound,
            PartyType = FinancePartyType.Vendor,
            PartyId = vendor.Data!.Id,
            DocDate = new DateTime(2026, 4, 6),
            Amount = 300m,
            DepositToAccountId = bank
        }));
        var postedOut = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(outbound.Data!.Id));
        postedOut.Succeeded.ShouldBeTrue(postedOut.Message);

        var entryOut = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(postedOut.Data!.JournalEntryId!.Value));
        entryOut.Data!.Lines.Single(l => l.AccountId == apId).Debit.ShouldBe(300m);
        entryOut.Data.Lines.Single(l => l.AccountId == bank).Credit.ShouldBe(300m);

        // 方向与往来方类型不一致
        var mismatch = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Vendor,
            PartyId = vendor.Data!.Id,
            DocDate = new DateTime(2026, 4, 7),
            Amount = 10m,
            DepositToAccountId = bank
        }));
        mismatch.Succeeded.ShouldBeFalse();

        // Inbound 未指定存款科目且未启用 UndepositedFunds 回退 → 拒绝
        var noAccount = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = customer.Data!.Id,
            DocDate = new DateTime(2026, 4, 7),
            Amount = 10m
        }));
        noAccount.Succeeded.ShouldBeTrue(noAccount.Message);
        var postNoAccount = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(noAccount.Data!.Id));
        postNoAccount.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task DocumentNumbers_UseIndependentScopes()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Seq Customer");
        var income = await AccountIdAsync("4100");

        foreach (var expected in new[] { "INV-000001", "INV-000002" })
        {
            var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
            {
                CustomerId = customer.Data!.Id,
                DocDate = new DateTime(2026, 5, 1),
                Lines = [new CreateInvoiceLineDto { AccountId = income, Quantity = 1, UnitPrice = 10m }]
            }));
            var posted = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id));
            posted.Data!.Number.ShouldBe(expected);
        }

        // 凭证号（JE-）独立于单据号（INV-）各自连续
        var entries = await InScopeAsync<IJournalEntryService, Result<IPagedList<JournalEntryDto>>>(
            s => s.GetListAsync(new JournalEntryQueryDto { PageSize = 10 }));
        entries.Data!.Items.Select(e => e.Number).ShouldContain("JE-000001");
        entries.Data.Items.Select(e => e.Number).ShouldContain("JE-000002");
    }
}
