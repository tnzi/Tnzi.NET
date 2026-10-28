namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 结算：核销/撤销、超额拒绝、状态派生、realized FX、账龄、外部摄取幂等
/// </summary>
public class SettlementWorkflowTests : FinanceIntegrationTestBase
{
    private async Task<Guid> AccountIdAsync(string code)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var account = await repo.FirstOrDefaultAsync(a => a.Code == code);
        account.ShouldNotBeNull($"account {code}");
        return account.Id;
    }

    private Task<Result<CustomerDto>> CreateCustomerAsync(string name)
        => InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = name }));

    private async Task<InvoiceDto> PostInvoiceAsync(Guid customerId, decimal amount, string? currency = null, decimal? rate = null, DateTime? docDate = null, DateTime? dueDate = null)
    {
        var income = await AccountIdAsync("4100");
        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customerId,
            DocDate = docDate ?? new DateTime(2026, 3, 1),
            DueDate = dueDate,
            Currency = currency,
            ExchangeRate = rate,
            Lines = [new CreateInvoiceLineDto { AccountId = income, Quantity = 1, UnitPrice = amount }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!;
    }

    private async Task<PaymentEntryDto> PostInboundPaymentAsync(Guid customerId, decimal amount, string? currency = null, decimal? rate = null)
    {
        var bank = await AccountIdAsync("1120");
        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = customerId,
            DocDate = new DateTime(2026, 3, 20),
            Amount = amount,
            Currency = currency,
            ExchangeRate = rate,
            DepositToAccountId = bank
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!;
    }

    private Task<Result<List<PaymentApplicationDto>>> ApplyAsync(SettlementDocType sourceType, Guid sourceId, SettlementDocType targetType, Guid targetId, decimal amount)
        => InScopeAsync<ISettlementService, Result<List<PaymentApplicationDto>>>(s => s.ApplyAsync(new ApplySettlementDto
        {
            SourceType = sourceType,
            SourceId = sourceId,
            Targets = [new ApplySettlementTargetDto { TargetType = targetType, TargetId = targetId, Amount = amount }]
        }));

    [Fact]
    public async Task Apply_FullAmount_MarksInvoicePaid_AndUnapplyRestores()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Settle A");
        var invoice = await PostInvoiceAsync(customer.Data!.Id, 100m);
        var payment = await PostInboundPaymentAsync(customer.Data.Id, 100m);

        var applied = await ApplyAsync(SettlementDocType.PaymentEntry, payment.Id, SettlementDocType.Invoice, invoice.Id, 100m);
        applied.Succeeded.ShouldBeTrue(applied.Message);
        applied.Data!.Single().RealizedFxJournalEntryId.ShouldBeNull();

        var invoiceAfter = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.GetAsync(invoice.Id));
        invoiceAfter.Data!.Status.ShouldBe(FinanceDocumentStatus.Paid);
        invoiceAfter.Data.AppliedTotal.ShouldBe(100m);

        var paymentAfter = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.GetAsync(payment.Id));
        paymentAfter.Data!.AppliedTotal.ShouldBe(100m);

        // 已核销的收款不可作废
        (await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.VoidAsync(payment.Id))).Code.ShouldBe(409);

        // 撤销核销
        var unapply = await InScopeAsync<ISettlementService, Result>(s => s.UnapplyAsync(applied.Data!.Single().Id));
        unapply.Succeeded.ShouldBeTrue(unapply.Message);

        var invoiceRestored = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.GetAsync(invoice.Id));
        invoiceRestored.Data!.Status.ShouldBe(FinanceDocumentStatus.Posted);
        invoiceRestored.Data.AppliedTotal.ShouldBe(0m);
    }

    [Fact]
    public async Task Apply_PartialAndOverallocation()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Settle B");
        var invoice = await PostInvoiceAsync(customer.Data!.Id, 200m);
        var payment = await PostInboundPaymentAsync(customer.Data.Id, 150m);

        // 部分核销 → PartiallyPaid
        var applied = await ApplyAsync(SettlementDocType.PaymentEntry, payment.Id, SettlementDocType.Invoice, invoice.Id, 120m);
        applied.Succeeded.ShouldBeTrue(applied.Message);

        var invoiceAfter = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.GetAsync(invoice.Id));
        invoiceAfter.Data!.Status.ShouldBe(FinanceDocumentStatus.PartiallyPaid);

        // 超过源剩余（150-120=30）
        (await ApplyAsync(SettlementDocType.PaymentEntry, payment.Id, SettlementDocType.Invoice, invoice.Id, 40m))
            .Succeeded.ShouldBeFalse();

        // 未清单据列表反映剩余 80
        var open = await InScopeAsync<ISettlementService, Result<List<OpenDocumentDto>>>(
            s => s.GetOpenDocumentsAsync(FinancePartyType.Customer, customer.Data.Id));
        open.Data!.Single(d => d.DocId == invoice.Id).Outstanding.ShouldBe(80m);
    }

    /// <summary>
    /// 回归（子账↔GL tie-out）：未核销的超收现金必须作为负行进 AR 账龄，
    /// 使账龄合计 = GL AR 控制科目余额。旧实现只查发票→超收后账龄错报 0（应为 −50）。
    /// </summary>
    [Fact]
    public async Task ArAging_TiesToControl_WithUnappliedOverpayment()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("TieOut Co");
        var invoice = await PostInvoiceAsync(customer.Data!.Id, 100m, docDate: new DateTime(2026, 3, 1));
        var payment = await PostInboundPaymentAsync(customer.Data.Id, 150m);
        (await ApplyAsync(SettlementDocType.PaymentEntry, payment.Id, SettlementDocType.Invoice, invoice.Id, 100m))
            .Succeeded.ShouldBeTrue();

        // GL AR = Dr 100 (发票) − Cr 150 (收款) = −50；发票已付清出账龄，未核销 50 现金入负行
        var aging = await InScopeAsync<IFinancialReportService, Result<AgingReportDto>>(
            s => s.GetArAgingAsync(new DateTime(2026, 12, 31)));
        aging.Succeeded.ShouldBeTrue(aging.Message);
        aging.Data!.Totals.Total.ShouldBe(-50m);
    }

    /// <summary>
    /// 回归（时点口径）：账龄以 asOf 之前发生的核销为准；asOf 之后才付清的发票在该日仍显示全额未清。
    /// 旧实现读当前 AppliedTotal/Status → 把后付清的单追溯抹掉（错报 0，应为 100）。
    /// </summary>
    [Fact]
    public async Task ArAging_PointInTime_IgnoresLaterSettlement()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("PIT Co");
        var invoice = await PostInvoiceAsync(customer.Data!.Id, 100m, docDate: new DateTime(2026, 3, 1));
        var payment = await PostInboundPaymentAsync(customer.Data.Id, 100m); // DocDate 2026-03-20
        (await ApplyAsync(SettlementDocType.PaymentEntry, payment.Id, SettlementDocType.Invoice, invoice.Id, 100m))
            .Succeeded.ShouldBeTrue();

        // 发票现为 Paid，但 2026-03-10 时收款(DocDate 03-20)与核销(记账于运行时刻)都尚未发生 → 账龄仍 100
        var aging = await InScopeAsync<IFinancialReportService, Result<AgingReportDto>>(
            s => s.GetArAgingAsync(new DateTime(2026, 3, 10)));
        aging.Succeeded.ShouldBeTrue(aging.Message);
        aging.Data!.Totals.Total.ShouldBe(100m);
    }

    [Fact]
    public async Task Apply_CreditMemo_OffsetsInvoice()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Settle C");
        var invoice = await PostInvoiceAsync(customer.Data!.Id, 100m);

        var income = await AccountIdAsync("4100");
        var memoDraft = await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.CreateDraftAsync(new CreateCreditMemoDto
        {
            CustomerId = customer.Data.Id,
            DocDate = new DateTime(2026, 3, 5),
            Lines = [new CreateCreditMemoLineDto { AccountId = income, Quantity = 1, UnitPrice = 30m }]
        }));
        var memo = await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.PostAsync(memoDraft.Data!.Id));
        memo.Succeeded.ShouldBeTrue(memo.Message);

        var applied = await ApplyAsync(SettlementDocType.CreditMemo, memo.Data!.Id, SettlementDocType.Invoice, invoice.Id, 30m);
        applied.Succeeded.ShouldBeTrue(applied.Message);

        var invoiceAfter = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.GetAsync(invoice.Id));
        invoiceAfter.Data!.AppliedTotal.ShouldBe(30m);
        invoiceAfter.Data.Status.ShouldBe(FinanceDocumentStatus.PartiallyPaid);

        var memoAfter = await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.GetAsync(memo.Data.Id));
        memoAfter.Data!.AppliedTotal.ShouldBe(30m);
        memoAfter.Data.Status.ShouldBe(FinanceDocumentStatus.Paid);
    }

    private async Task<CreditMemoDto> PostCreditMemoAsync(Guid customerId, decimal amount)
    {
        var income = await AccountIdAsync("4100");
        var draft = await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.CreateDraftAsync(new CreateCreditMemoDto
        {
            CustomerId = customerId,
            DocDate = new DateTime(2026, 3, 5),
            Lines = [new CreateCreditMemoLineDto { AccountId = income, Quantity = 1, UnitPrice = amount }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var posted = await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!;
    }

    /// <summary>
    /// 全额核销的贷项（状态 Paid）照样能撤销核销。
    /// </summary>
    /// <remarks>
    /// 被保护的缺陷：撤销核销第一步按「能不能作为源去核销」加载贷项，只认 Posted / PartiallyPaid，
    /// 于是全额核销的贷项撤销时答 409「Only posted credit memos can be applied.」。贷项作废要求未核销，
    /// 发票作废也要求未核销，三条路都堵死，核销错了发票只能手工做凭证，子账与总账从此对不上。
    /// </remarks>
    [Fact]
    public async Task Unapply_FullyAppliedCreditMemo_RestoresBothDocuments_AndBothCanThenBeVoided()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Settle CM full");
        var invoice = await PostInvoiceAsync(customer.Data!.Id, 100m);
        var memo = await PostCreditMemoAsync(customer.Data.Id, 30m);

        var applied = await ApplyAsync(SettlementDocType.CreditMemo, memo.Id, SettlementDocType.Invoice, invoice.Id, 30m);
        applied.Succeeded.ShouldBeTrue(applied.Message);
        (await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.GetAsync(memo.Id))).Data!.Status
            .ShouldBe(FinanceDocumentStatus.Paid);

        var unapply = await InScopeAsync<ISettlementService, Result>(s => s.UnapplyAsync(applied.Data!.Single().Id));
        unapply.Succeeded.ShouldBeTrue(unapply.Message);

        var memoAfter = (await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.GetAsync(memo.Id))).Data!;
        memoAfter.Status.ShouldBe(FinanceDocumentStatus.Posted);
        memoAfter.AppliedTotal.ShouldBe(0m);
        var invoiceAfter = (await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.GetAsync(invoice.Id))).Data!;
        invoiceAfter.Status.ShouldBe(FinanceDocumentStatus.Posted);
        invoiceAfter.AppliedTotal.ShouldBe(0m);

        (await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.VoidAsync(memo.Id))).Succeeded.ShouldBeTrue();
        (await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.VoidAsync(invoice.Id))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Unapply_PartiallyAppliedCreditMemo_StillWorks()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Settle CM part");
        var invoice = await PostInvoiceAsync(customer.Data!.Id, 100m);
        var memo = await PostCreditMemoAsync(customer.Data.Id, 30m);

        var applied = await ApplyAsync(SettlementDocType.CreditMemo, memo.Id, SettlementDocType.Invoice, invoice.Id, 10m);
        applied.Succeeded.ShouldBeTrue(applied.Message);

        var unapply = await InScopeAsync<ISettlementService, Result>(s => s.UnapplyAsync(applied.Data!.Single().Id));
        unapply.Succeeded.ShouldBeTrue(unapply.Message);
        (await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.GetAsync(memo.Id))).Data!.AppliedTotal.ShouldBe(0m);
    }

    /// <summary>撤销放宽的只是撤销：已用完的贷项不能再作为源去核销。</summary>
    [Fact]
    public async Task Apply_FromAFullyAppliedCreditMemo_IsStillRejected()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Settle CM reuse");
        var first = await PostInvoiceAsync(customer.Data!.Id, 100m);
        var second = await PostInvoiceAsync(customer.Data.Id, 100m);
        var memo = await PostCreditMemoAsync(customer.Data.Id, 30m);
        (await ApplyAsync(SettlementDocType.CreditMemo, memo.Id, SettlementDocType.Invoice, first.Id, 30m)).Succeeded.ShouldBeTrue();

        var again = await ApplyAsync(SettlementDocType.CreditMemo, memo.Id, SettlementDocType.Invoice, second.Id, 1m);

        again.Succeeded.ShouldBeFalse();
        again.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Apply_CurrencyMismatch_Rejected()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Settle D");
        var invoice = await PostInvoiceAsync(customer.Data!.Id, 100m, currency: "EUR", rate: 1.1m);
        var payment = await PostInboundPaymentAsync(customer.Data.Id, 100m);

        var applied = await ApplyAsync(SettlementDocType.PaymentEntry, payment.Id, SettlementDocType.Invoice, invoice.Id, 100m);
        applied.Succeeded.ShouldBeFalse();
        applied.Message.ShouldNotBeNull();
        applied.Message.ShouldContain("Currency mismatch");
    }

    [Fact]
    public async Task Apply_RealizedFx_PostsGainLossAndBalances()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Settle FX");

        // 发票 EUR@1.10（AR 借 110 base），收款 EUR@1.20（AR 贷 120 base）→ AR 残差 -10 = 收益 10
        var invoice = await PostInvoiceAsync(customer.Data!.Id, 100m, currency: "EUR", rate: 1.10m);
        var payment = await PostInboundPaymentAsync(customer.Data.Id, 100m, currency: "EUR", rate: 1.20m);

        var applied = await ApplyAsync(SettlementDocType.PaymentEntry, payment.Id, SettlementDocType.Invoice, invoice.Id, 100m);
        applied.Succeeded.ShouldBeTrue(applied.Message);
        var fxEntryId = applied.Data!.Single().RealizedFxJournalEntryId;
        fxEntryId.ShouldNotBeNull();

        var fxEntry = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(fxEntryId!.Value));
        fxEntry.Succeeded.ShouldBeTrue(fxEntry.Message);
        fxEntry.Data!.TotalDebit.ShouldBe(10m);
        // FX 凭证记入两单据较晚的记账日（收款 3/20 晚于发票 3/1），不受"今天"影响
        fxEntry.Data.PostingDate.ShouldBe(new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc));
        var arId = await AccountIdAsync("1200");
        var fxAccountId = await AccountIdAsync("5800");
        // AR 借 10（回补残差），汇兑损益贷 10（收益）
        fxEntry.Data.Lines.Single(l => l.AccountId == arId).Debit.ShouldBe(10m);
        fxEntry.Data.Lines.Single(l => l.AccountId == fxAccountId).Credit.ShouldBe(10m);

        // 全账本试算平衡
        var tb = await InScopeAsync<IFinancialReportService, Result<TrialBalanceReportDto>>(
            s => s.GetTrialBalanceAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
        tb.Data!.TotalPeriodDebit.ShouldBe(tb.Data.TotalPeriodCredit);
        // AR 期末归零（110 - 120 + 10）
        tb.Data.Rows.SingleOrDefault(r => r.AccountId == arId)?.ClosingBalance.ShouldBe(0m);

        // 撤销核销 → FX 凭证被冲销，AR 恢复残差前状态
        var unapply = await InScopeAsync<ISettlementService, Result>(s => s.UnapplyAsync(applied.Data!.Single().Id));
        unapply.Succeeded.ShouldBeTrue(unapply.Message);

        var fxAfter = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(fxEntryId!.Value));
        fxAfter.Data!.Status.ShouldBe(JournalEntryStatus.Reversed);
    }

    /// <summary>
    /// 回归：realized FX 凭证的撤销路径是「撤销核销」，不是总账冲销。
    /// </summary>
    /// <remarks>
    /// 核销还在时从总账冲销它，AR 控制科目多出一笔 -10，而发票仍是 Paid、收款仍全额核销，
    /// 账龄合计 0、控制科目 -10，两边各自看起来都正常。撤销核销自己经冲销漏斗处理这张凭证，
    /// 不走总账那道门，故把它拦在总账端点之外不会堵死任何既有流程。
    /// </remarks>
    [Fact]
    public async Task RealizedFx_GlReverseRejected_WhileUnapplyStillReversesIt()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Settle FX gate");
        var invoice = await PostInvoiceAsync(customer.Data!.Id, 100m, currency: "EUR", rate: 1.10m);
        var payment = await PostInboundPaymentAsync(customer.Data.Id, 100m, currency: "EUR", rate: 1.20m);

        var applied = await ApplyAsync(SettlementDocType.PaymentEntry, payment.Id, SettlementDocType.Invoice, invoice.Id, 100m);
        applied.Succeeded.ShouldBeTrue(applied.Message);
        var fxEntryId = applied.Data!.Single().RealizedFxJournalEntryId!.Value;

        // 总账端点与编程式冲销是同一道门，都必须拒绝
        var viaJournal = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(fxEntryId, new ReverseJournalEntryDto()));
        viaJournal.Succeeded.ShouldBeFalse();
        viaJournal.Code.ShouldBe(409);
        viaJournal.Message!.ShouldContain(FinanceSourceTypes.PaymentApplication);

        var viaLedger = await InScopeAsync<ILedgerPostingService, Result<JournalEntryDto>>(s => s.ReverseAsync(fxEntryId));
        viaLedger.Succeeded.ShouldBeFalse();
        viaLedger.Code.ShouldBe(409);

        // 拒绝路径零写入：FX 凭证仍 Posted、AR 控制科目仍与（已付清的）账龄一致为 0
        (await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(fxEntryId)))
            .Data!.Status.ShouldBe(JournalEntryStatus.Posted);
        var arId = await AccountIdAsync("1200");
        var tb = await InScopeAsync<IFinancialReportService, Result<TrialBalanceReportDto>>(
            s => s.GetTrialBalanceAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
        (tb.Data!.Rows.SingleOrDefault(r => r.AccountId == arId)?.ClosingBalance ?? 0m).ShouldBe(0m);

        // 撤销核销仍能冲销它
        var unapply = await InScopeAsync<ISettlementService, Result>(s => s.UnapplyAsync(applied.Data!.Single().Id));
        unapply.Succeeded.ShouldBeTrue(unapply.Message);
        (await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(fxEntryId)))
            .Data!.Status.ShouldBe(JournalEntryStatus.Reversed);
    }

    [Fact]
    public async Task Aging_BucketsByDueDate()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Aging A");
        var asOf = new DateTime(2026, 6, 30);

        await PostInvoiceAsync(customer.Data!.Id, 100m, docDate: new DateTime(2026, 6, 1), dueDate: new DateTime(2026, 7, 15));   // 未到期 → Current
        await PostInvoiceAsync(customer.Data.Id, 200m, docDate: new DateTime(2026, 5, 20), dueDate: new DateTime(2026, 6, 10));   // 逾期 20 天 → 1-30
        await PostInvoiceAsync(customer.Data.Id, 300m, docDate: new DateTime(2026, 2, 1), dueDate: new DateTime(2026, 3, 1));     // 逾期 121 天 → 90+

        var aging = await InScopeAsync<IFinancialReportService, Result<AgingReportDto>>(s => s.GetArAgingAsync(asOf));
        aging.Succeeded.ShouldBeTrue(aging.Message);

        var row = aging.Data!.Rows.Single(r => r.PartyId == customer.Data.Id);
        row.Current.ShouldBe(100m);
        row.Days1To30.ShouldBe(200m);
        row.Over90.ShouldBe(300m);
        row.Total.ShouldBe(600m);
        aging.Data.Totals.Total.ShouldBe(600m);
    }

    [Fact]
    public async Task ExternalIngest_FailedAutoPost_SelfHealsOnRetry()
    {
        // 未播种 COA：AutoPost 因缺 AR 角色科目失败，草稿留库
        var customer = await CreateCustomerAsync("Heal A");
        var bankLess = new ExternalPaymentIngestDto
        {
            SourceType = "Payment.Order",
            SourceId = "heal-1",
            CustomerId = customer.Data!.Id,
            DocDate = new DateTime(2026, 5, 1),
            Amount = 20m
        };

        var first = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateFromExternalAsync(bankLess));
        first.Succeeded.ShouldBeFalse(); // 过账失败以失败返回（草稿已留库），调用方可感知

        // 条件就绪（播种 COA + 指定存入科目不需要——重投沿用原草稿字段，这里补上存入科目再摄取）
        await SeedCoaAsync();
        var bank = await AccountIdAsync("1120");
        // 原草稿无存入科目，重投仍失败 → 调用方修正草稿（或运维配置 UndepositedFunds 后重投）
        var retryNoAccount = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateFromExternalAsync(bankLess));
        retryNoAccount.Succeeded.ShouldBeFalse();

        // 修正草稿存入科目后，再次幂等摄取补投成功（自愈）
        var draft = await InScopeAsync<IPaymentEntryService, Result<IPagedList<PaymentEntryDto>>>(
            s => s.GetPagedAsync(new PaymentEntryQueryDto { PageSize = 10 }));
        var draftId = draft.Data!.Items.Single(p => p.SourceId == "heal-1").Id;
        var updated = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.UpdateDraftAsync(draftId, new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = customer.Data.Id,
            DocDate = new DateTime(2026, 5, 1),
            Amount = 20m,
            DepositToAccountId = bank
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);

        var healed = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateFromExternalAsync(bankLess));
        healed.Succeeded.ShouldBeTrue(healed.Message);
        healed.Data!.Status.ShouldBe(FinanceDocumentStatus.Posted);
        healed.Data.Id.ShouldBe(draftId);
    }

    [Fact]
    public async Task ExternalIngest_IsIdempotent_AndAutoPosts()
    {
        await SeedCoaAsync();
        var customer = await CreateCustomerAsync("Ingest A");
        var bank = await AccountIdAsync("1120");

        var input = new ExternalPaymentIngestDto
        {
            SourceType = "Payment.Order",
            SourceId = "order-123",
            CustomerId = customer.Data!.Id,
            DocDate = new DateTime(2026, 4, 10),
            Amount = 55m,
            DepositToAccountId = bank,
            Reference = "gw-abc"
        };

        var first = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateFromExternalAsync(input));
        first.Succeeded.ShouldBeTrue(first.Message);
        first.Data!.Status.ShouldBe(FinanceDocumentStatus.Posted);
        first.Data.SourceType.ShouldBe("Payment.Order");

        // 幂等：重复摄取返回既有单据
        var second = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateFromExternalAsync(input));
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.Id.ShouldBe(first.Data.Id);

        var list = await InScopeAsync<IPaymentEntryService, Result<IPagedList<PaymentEntryDto>>>(
            s => s.GetPagedAsync(new PaymentEntryQueryDto { PageSize = 10 }));
        list.Data!.Items.Count(p => p.SourceId == "order-123").ShouldBe(1);
    }
}
