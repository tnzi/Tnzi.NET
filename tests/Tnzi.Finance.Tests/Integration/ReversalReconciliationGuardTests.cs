using Tnzi.Finance.Services.Internal;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 冲销 × 银行对账的保护：冲销漏斗（<c>LedgerPostingEngine.BuildReversalAsync</c>）内的守卫，
/// 以及与它同源的只读查询 <see cref="ILedgerPostingService.GetReversibilityAsync"/>
/// </summary>
/// <remarks>
/// 被保护的缺陷：作废一笔总账行落在<b>已完成</b>对账窗口内的付款，会往已对平的区间里追加一条
/// 新凭证；对账不能重开，于是该期永久对不平且无受支持的修复途径。同时那条 <c>BankTransaction</c>
/// 仍 <c>Matched</c> 而它指向的总账行已变 <c>Reversed</c>。
/// </remarks>
public class ReversalReconciliationGuardTests : FinanceIntegrationTestBase
{
    private static readonly DateTime PaymentDate = new(2026, 4, 6);
    private static readonly DateTime StatementDate = new(2026, 4, 30);
    private const decimal PaymentAmount = 300m;

    /// <summary>付款贷记银行 300 → 该银行行的本位币净额</summary>
    private const decimal BankLineNet = -PaymentAmount;

    private static CsvMappingDto SingleColumnMapping() => new()
    {
        HasHeader = true,
        Delimiter = ",",
        DateColumn = 0,
        DateFormat = "yyyy-MM-dd",
        AmountColumn = 2,
        DescriptionColumn = 1
    };

    /// <summary>
    /// 建一笔已过账的对外付款（Dr AP / Cr 1120 银行），返回付款单与其过账凭证
    /// </summary>
    private async Task<(PaymentEntryDto Payment, Guid Bank)> SeedPostedPaymentAsync()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");

        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(
            s => s.CreateAsync(new CreateVendorDto { Name = "Guard Vendor" }));
        vendor.Succeeded.ShouldBeTrue(vendor.Message);

        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(
            s => s.CreateDraftAsync(new CreatePaymentEntryDto
            {
                Direction = PaymentDirection.Outbound,
                PartyType = FinancePartyType.Vendor,
                PartyId = vendor.Data!.Id,
                DocDate = PaymentDate,
                Amount = PaymentAmount,
                DepositToAccountId = bank
            }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.JournalEntryId.ShouldNotBeNull();

        return (posted.Data, bank);
    }

    /// <summary>
    /// 导入一条与该付款等额的银行流水并确认匹配（走真实 feed 流程：导入 → 建议 → Draft 对账 → 确认），
    /// 返回该 Draft 对账 id
    /// </summary>
    private async Task<Guid> MatchStatementLineAsync(Guid bank)
    {
        var csv = $"Date,Description,Amount\n{PaymentDate:yyyy-MM-dd},Vendor payment,{BankLineNet:0.00}\n";
        var import = await InScopeAsync<IBankFeedService, Result<BankImportResultDto>>(
            s => s.ImportStatementAsync(bank, BankTransactionSource.Csv, "guard.csv", csv, SingleColumnMapping()));
        import.Succeeded.ShouldBeTrue(import.Message);

        var suggest = await InScopeAsync<IBankFeedService, Result<BankSuggestResultDto>>(s => s.SuggestMatchesAsync(bank));
        suggest.Succeeded.ShouldBeTrue(suggest.Message);
        suggest.Data!.Suggested.ShouldBe(1);

        var recon = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CreateDraftAsync(new CreateReconciliationDto
            {
                AccountId = bank,
                StatementDate = StatementDate,
                StatementEndingBalance = BankLineNet
            }));
        recon.Succeeded.ShouldBeTrue(recon.Message);

        var txnRepo = ServiceProvider.GetRequiredService<IRepository<BankTransaction, Guid>>();
        var txn = (await txnRepo.ToListAsync(t => t.AccountId == bank)).Single();

        var confirmed = await InScopeAsync<IBankFeedService, Result<BankTransactionDto>>(
            s => s.ConfirmMatchAsync(txn.Id, new ConfirmBankMatchDto()));
        confirmed.Succeeded.ShouldBeTrue(confirmed.Message);
        confirmed.Data!.Status.ShouldBe(BankTransactionStatus.Matched);

        return recon.Data!.Id;
    }

    private Task<Result<ReversibilityDto>> ReversibilityAsync(Guid journalEntryId)
        => InScopeAsync<ILedgerPostingService, Result<ReversibilityDto>>(s => s.GetReversibilityAsync(journalEntryId));

    private async Task<int> JournalEntryCountAsync()
        => await ServiceProvider.GetRequiredService<IRepository<JournalEntry, Guid>>().CountAsync();

    private async Task<long> NextJournalNumberAsync()
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<DocumentSequence, Guid>>();
        var sequence = await repo.FirstOrDefaultAsync(s => s.Scope == LedgerPostingEngine.JournalEntrySequenceScope);
        return sequence?.NextValue ?? 0;
    }

    // ---- A：冲销漏斗内的守卫 ----

    /// <summary>
    /// 已完成对账锁定的行不得被冲销，且拒绝路径<b>零写入</b>
    /// （守卫位于凭证号分配与余额桶累加之前，烧号即等于留下缺口）。
    /// </summary>
    [Fact]
    public async Task Void_LinesLockedByCompletedReconciliation_Rejected_WithoutAnyWrite()
    {
        var (payment, bank) = await SeedPostedPaymentAsync();
        var reconciliationId = await MatchStatementLineAsync(bank);

        var completed = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CompleteAsync(reconciliationId));
        completed.Succeeded.ShouldBeTrue(completed.Message);
        completed.Data!.Status.ShouldBe(ReconciliationStatus.Completed);

        var entriesBefore = await JournalEntryCountAsync();
        var nextNumberBefore = await NextJournalNumberAsync();

        var voided = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.VoidAsync(payment.Id));
        voided.Succeeded.ShouldBeFalse();
        voided.Code.ShouldBe(409);
        voided.Message.ShouldNotBeNull();
        voided.Message.ShouldContain("completed bank reconciliation");
        // 措辞必须给出补救办法，否则操作员只知道被拒、不知道该干什么
        voided.Message.ShouldContain("correcting entry");

        // 零写入：付款仍 Posted、原凭证仍 Posted、无新凭证、凭证号未被烧掉
        var reloadedPayment = (await ReloadAsync<PaymentEntry>(payment.Id))!;
        reloadedPayment.Status.ShouldBe(FinanceDocumentStatus.Posted);
        reloadedPayment.VoidJournalEntryId.ShouldBeNull();

        var original = (await ReloadAsync<JournalEntry>(payment.JournalEntryId!.Value))!;
        original.Status.ShouldBe(JournalEntryStatus.Posted);
        original.ReversedByEntryId.ShouldBeNull();

        (await JournalEntryCountAsync()).ShouldBe(entriesBefore);
        (await NextJournalNumberAsync()).ShouldBe(nextNumberBefore);
    }

    /// <summary>
    /// 已匹配银行流水（其对账仍是 Draft）的行不得被冲销：指路解除匹配，
    /// 但绝不自动解除——那是在无声地丢弃别人的对账工作。
    /// </summary>
    [Fact]
    public async Task Void_LinesMatchedToStatementLine_Rejected_AndMatchLeftIntact()
    {
        var (payment, bank) = await SeedPostedPaymentAsync();
        await MatchStatementLineAsync(bank); // 对账停在 Draft

        var voided = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.VoidAsync(payment.Id));
        voided.Succeeded.ShouldBeFalse();
        voided.Code.ShouldBe(409);
        voided.Message.ShouldNotBeNull();
        voided.Message.ShouldContain("Unmatch them first");

        var txnRepo = ServiceProvider.GetRequiredService<IRepository<BankTransaction, Guid>>();
        var txn = (await txnRepo.ToListAsync(t => t.AccountId == bank)).Single();
        txn.Status.ShouldBe(BankTransactionStatus.Matched);
        txn.MatchedJournalLineId.ShouldNotBeNull();

        (await ReloadAsync<PaymentEntry>(payment.Id))!.Status.ShouldBe(FinanceDocumentStatus.Posted);
    }

    /// <summary>
    /// 回归保护：未匹配、未对账的行照常可以作废——守卫不得误伤正常路径。
    /// </summary>
    [Fact]
    public async Task Void_UnmatchedUnreconciledLines_StillSucceeds()
    {
        var (payment, _) = await SeedPostedPaymentAsync();

        var voided = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.VoidAsync(payment.Id));
        voided.Succeeded.ShouldBeTrue(voided.Message);
        voided.Data!.Status.ShouldBe(FinanceDocumentStatus.Voided);
        voided.Data.VoidJournalEntryId.ShouldNotBeNull();

        var original = (await ReloadAsync<JournalEntry>(payment.JournalEntryId!.Value))!;
        original.Status.ShouldBe(JournalEntryStatus.Reversed);
    }

    // ---- C：只读可冲销性查询（判定与守卫同源） ----

    [Fact]
    public async Task GetReversibility_ReportsReconciled_WhenLinesLockedByCompletedReconciliation()
    {
        var (payment, bank) = await SeedPostedPaymentAsync();
        var reconciliationId = await MatchStatementLineAsync(bank);
        (await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CompleteAsync(reconciliationId))).Succeeded.ShouldBeTrue();

        var reversibility = await ReversibilityAsync(payment.JournalEntryId!.Value);
        reversibility.Succeeded.ShouldBeTrue(reversibility.Message);
        reversibility.Data!.JournalEntryId.ShouldBe(payment.JournalEntryId!.Value);
        reversibility.Data.CanReverse.ShouldBeFalse();
        reversibility.Data.BlockedBy.ShouldBe(ReversalBlockReasons.Reconciled);
        reversibility.Data.Detail.ShouldNotBeNull();

        // 同源证明：查询给出的理由，就是真冲销时收到的那句话
        var voided = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.VoidAsync(payment.Id));
        voided.Succeeded.ShouldBeFalse();
        voided.Message.ShouldBe(reversibility.Data.Detail);
    }

    [Fact]
    public async Task GetReversibility_ReportsStatementMatched_WhenReconciliationStillDraft()
    {
        var (payment, bank) = await SeedPostedPaymentAsync();
        await MatchStatementLineAsync(bank);

        var reversibility = await ReversibilityAsync(payment.JournalEntryId!.Value);
        reversibility.Succeeded.ShouldBeTrue(reversibility.Message);
        reversibility.Data!.CanReverse.ShouldBeFalse();
        reversibility.Data.BlockedBy.ShouldBe(ReversalBlockReasons.StatementMatched);

        var voided = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.VoidAsync(payment.Id));
        voided.Message.ShouldBe(reversibility.Data.Detail);
    }

    [Fact]
    public async Task GetReversibility_AllowsUnmatchedUnreconciledEntry()
    {
        var (payment, _) = await SeedPostedPaymentAsync();

        var reversibility = await ReversibilityAsync(payment.JournalEntryId!.Value);
        reversibility.Succeeded.ShouldBeTrue(reversibility.Message);
        reversibility.Data!.CanReverse.ShouldBeTrue();
        reversibility.Data.BlockedBy.ShouldBeNull();
        reversibility.Data.Detail.ShouldBeNull();

        // 判定成立：说能冲就真能冲
        (await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.VoidAsync(payment.Id)))
            .Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// 状态门与 <c>IJournalEntryService.ReverseAsync</c> 的前置校验同序：草稿 → 已冲销。
    /// 只读查询恒返回成功的 <c>Result</c>，"查得到、答案是不行"不是错误。
    /// </summary>
    [Fact]
    public async Task GetReversibility_ReportsDraftAndAlreadyReversed()
    {
        await SeedCoaAsync();
        var arId = await AccountIdByCodeAsync("1200");
        var incomeId = await AccountIdByCodeAsync("4100");

        var draft = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.CreateDraftAsync(new CreateJournalEntryDto
            {
                PostingDate = PaymentDate,
                Memo = "Reversibility probe",
                Lines =
                [
                    new CreateJournalLineDto { AccountId = arId, Debit = 120m },
                    new CreateJournalLineDto { AccountId = incomeId, Credit = 120m }
                ]
            }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var asDraft = await ReversibilityAsync(draft.Data!.Id);
        asDraft.Succeeded.ShouldBeTrue(asDraft.Message);
        asDraft.Data!.CanReverse.ShouldBeFalse();
        asDraft.Data.BlockedBy.ShouldBe(ReversalBlockReasons.NotPosted);

        (await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.PostAsync(draft.Data.Id)))
            .Succeeded.ShouldBeTrue();
        (await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(draft.Data.Id, new ReverseJournalEntryDto()))).Succeeded.ShouldBeTrue();

        var afterReversal = await ReversibilityAsync(draft.Data.Id);
        afterReversal.Succeeded.ShouldBeTrue(afterReversal.Message);
        afterReversal.Data!.CanReverse.ShouldBeFalse();
        afterReversal.Data.BlockedBy.ShouldBe(ReversalBlockReasons.AlreadyReversed);

        var missing = await ReversibilityAsync(Guid.NewGuid());
        missing.Succeeded.ShouldBeFalse();
        missing.Code.ShouldBe(404);
    }
}
