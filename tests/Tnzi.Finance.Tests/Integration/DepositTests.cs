using Microsoft.EntityFrameworkCore;
using Tnzi.EFCore.Extensions;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 银行存款单：把待存款项上的 N 张收款一次带进银行（借方一行、贷方逐张），
/// 独占声明由唯一索引保证，作废释放收款。
/// </summary>
/// <remarks>
/// 全部经真实 SQLite + 真实实体配置：<b>只有真实索引能证明「至多一张存活存款单」</b> ——
/// 用 mock 仓储跑，先查再写的那条路径永远是绿的。
/// </remarks>
public class DepositTests : FinanceIntegrationTestBase
{
    private Task<Result<CustomerDto>> CreateCustomerAsync(string name)
        => InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = name }));

    /// <summary>
    /// 开一张已过账的 Inbound 收款。<paramref name="depositToAccountId"/> 传 null =
    /// 不指定存入科目，交给 <c>PostToUndepositedFunds</c> 回退到待存款项角色科目
    /// </summary>
    private async Task<PaymentEntryDto> PostReceiptAsync(Guid customerId, Guid? depositToAccountId, decimal amount, DateTime? date = null, string? currency = null, decimal? rate = null)
    {
        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = customerId,
            DocDate = date ?? new DateTime(2026, 3, 20),
            Amount = amount,
            Currency = currency,
            ExchangeRate = rate,
            PaymentMethod = PaymentMethods.Check,
            DepositToAccountId = depositToAccountId
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!;
    }

    private Task<Result<DepositDto>> CreateDepositAsync(Guid from, Guid to, IEnumerable<Guid> receiptIds, DateTime? date = null, List<CreateDepositFundsLineDto>? otherFunds = null, string? currency = null)
        => InScopeAsync<IDepositService, Result<DepositDto>>(s => s.CreateDraftAsync(new CreateDepositDto
        {
            FromAccountId = from,
            ToAccountId = to,
            DepositDate = date ?? new DateTime(2026, 3, 25),
            Currency = currency,
            PaymentEntryIds = [.. receiptIds],
            OtherFunds = otherFunds ?? []
        }));

    private Task<Result<List<UndepositedReceiptDto>>> UndepositedAsync(Guid accountId)
        => InScopeAsync<IDepositService, Result<List<UndepositedReceiptDto>>>(
            s => s.GetUndepositedReceiptsAsync(new UndepositedReceiptQueryDto { AccountId = accountId }));

    private async Task<decimal> ClosingBalanceAsync(Guid accountId, DateTime from, DateTime to)
    {
        var gl = await InScopeAsync<IFinancialReportService, Result<GeneralLedgerReportDto>>(
            s => s.GetGeneralLedgerAsync(accountId, from, to, new PagedQueryDto { PageIndex = 1, PageSize = 100 }));
        gl.Succeeded.ShouldBeTrue(gl.Message);
        return gl.Data!.ClosingBalance;
    }

    /// <summary>
    /// 核心场景：7 张支票分几天收进待存款项，一次带去银行 —— 总账上是<b>一张</b>凭证、
    /// 借方<b>一行</b> 5,000，银行流水那一行于是配得上；待存款项归零。
    /// </summary>
    [Fact]
    public async Task Deposit_GroupsReceipts_IntoOneBankDebit_AndClearsUndepositedFunds()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var customer = await CreateCustomerAsync("Acme");
        customer.Succeeded.ShouldBeTrue(customer.Message);

        decimal[] amounts = [1200m, 800m, 450m, 1000m, 300m, 750m, 500m];
        var receipts = new List<PaymentEntryDto>();
        for (var i = 0; i < amounts.Length; i++)
            receipts.Add(await PostReceiptAsync(customer.Data!.Id, undeposited, amounts[i], new DateTime(2026, 3, 20).AddDays(i % 3)));

        var period = (From: new DateTime(2026, 3, 1), To: new DateTime(2026, 3, 31));
        (await ClosingBalanceAsync(undeposited, period.From, period.To)).ShouldBe(5000m);

        var draft = await CreateDepositAsync(undeposited, bank, receipts.Select(r => r.Id));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.Amount.ShouldBe(5000m);
        draft.Data.Lines.Count.ShouldBe(7);
        draft.Data.Number.ShouldBeNull("编号在过账时才分配");

        var posted = await InScopeAsync<IDepositService, Result<DepositDto>>(s => s.PostAsync(draft.Data.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.Status.ShouldBe(FinanceDocumentStatus.Posted);
        posted.Data.Number.ShouldNotBeNull();
        posted.Data.JournalEntryId.ShouldNotBeNull();

        // 银行侧必须是**一行** 5000：拆成 7 行，BankMatchEngine 就再也配不上那一行流水
        var entry = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(posted.Data.JournalEntryId!.Value));
        entry.Succeeded.ShouldBeTrue(entry.Message);
        var bankLines = entry.Data!.Lines.Where(l => l.AccountId == bank).ToList();
        bankLines.Count.ShouldBe(1);
        bankLines[0].TxnDebit.ShouldBe(5000m);
        entry.Data.Lines.Count(l => l.AccountId == undeposited).ShouldBe(7);

        (await ClosingBalanceAsync(undeposited, period.From, period.To)).ShouldBe(0m);
        (await ClosingBalanceAsync(bank, period.From, period.To)).ShouldBe(5000m);
    }

    /// <summary>
    /// 「其它款项」行：不来自任何收款单的钱（银行利息）与收款一起进同一笔存款。
    /// </summary>
    [Fact]
    public async Task Deposit_CarriesOtherFundsLines_AlongsideReceipts()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var income = await AccountIdByCodeAsync("4100");
        var customer = await CreateCustomerAsync("Acme");

        var receipt = await PostReceiptAsync(customer.Data!.Id, undeposited, 600m);

        var draft = await CreateDepositAsync(undeposited, bank, [receipt.Id], otherFunds:
        [
            new CreateDepositFundsLineDto { AccountId = income, Amount = 40m, Description = "Bank interest" }
        ]);
        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.Amount.ShouldBe(640m);

        var posted = await InScopeAsync<IDepositService, Result<DepositDto>>(s => s.PostAsync(draft.Data.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var entry = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(posted.Data!.JournalEntryId!.Value));
        entry.Data!.Lines.Single(l => l.AccountId == bank).TxnDebit.ShouldBe(640m);
        entry.Data.Lines.Single(l => l.AccountId == undeposited).TxnCredit.ShouldBe(600m);
        entry.Data.Lines.Single(l => l.AccountId == income).TxnCredit.ShouldBe(40m);
    }

    /// <summary>
    /// 准则 3 —— 一张收款至多进一张存活的存款单，<b>且这条规则在并发下也成立</b>。
    /// </summary>
    /// <remarks>
    /// 两段都要：先查再写的那一段答的是「你选的这张已经被人拿走了」（409，可读）；
    /// 唯一索引那一段答的是并发 —— 两个人同时读，两次都读到「还没人收」。
    /// 这里绕开服务层直接插两条声明行，是因为服务层的预检恰好会挡住第一段，
    /// 而<b>要验证的正是预检失效时数据库还拦不拦得住</b>。
    /// </remarks>
    [Fact]
    public async Task Deposit_ClaimsReceiptExclusively_EnforcedByTheDatabase()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var bank2 = await CreateAccountAsync(new CreateAccountDto
        {
            Code = "1121",
            Name = "Second Bank",
            RootType = AccountRootType.Asset,
            CashFlowActivity = CashFlowActivity.CashEquivalent
        });
        var customer = await CreateCustomerAsync("Acme");
        var receipt = await PostReceiptAsync(customer.Data!.Id, undeposited, 900m);

        var first = await CreateDepositAsync(undeposited, bank, [receipt.Id]);
        first.Succeeded.ShouldBeTrue(first.Message);

        // ① 服务层预检：给出「已经在别的存款单上」这样的可读答复
        var second = await CreateDepositAsync(undeposited, bank2, [receipt.Id]);
        second.Succeeded.ShouldBeFalse("同一张收款不能同时在两张存款单上");
        second.Code.ShouldBe(409);

        // ② 数据库：预检读不到（并发）时唯一索引仍然拦得住
        var lineRepo = ServiceProvider.GetRequiredService<IRepository<DepositLine, Guid>>();
        var rogue = new DepositLine
        {
            DepositId = first.Data!.Id,
            LineNumber = 99,
            PaymentEntryId = receipt.Id,
            ClaimedPaymentEntryId = receipt.Id,
            Amount = 900m
        };
        var conflict = await Should.ThrowAsync<DbUpdateException>(async () => await lineRepo.InsertAsync(rogue));
        conflict.IsUniqueConstraintViolation().ShouldBeTrue(
            "拦下第二条声明的必须是唯一索引本身，不是别的什么异常");
    }

    /// <summary>
    /// 准则 4 —— 作废后收款回到候选清单，可以进新的存款单；
    /// 且作废的那张单据<b>仍然答得出</b>当初装的是哪几笔。
    /// </summary>
    [Fact]
    public async Task Void_ReleasesReceipts_ButKeepsTheLinesForAudit()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var customer = await CreateCustomerAsync("Acme");
        var a = await PostReceiptAsync(customer.Data!.Id, undeposited, 300m);
        var b = await PostReceiptAsync(customer.Data.Id, undeposited, 200m);

        var draft = await CreateDepositAsync(undeposited, bank, [a.Id, b.Id]);
        var posted = await InScopeAsync<IDepositService, Result<DepositDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        (await UndepositedAsync(undeposited)).Data!.ShouldBeEmpty();

        var voided = await InScopeAsync<IDepositService, Result<DepositDto>>(s => s.VoidAsync(posted.Data!.Id));
        voided.Succeeded.ShouldBeTrue(voided.Message);
        voided.Data!.Status.ShouldBe(FinanceDocumentStatus.Voided);
        voided.Data.VoidJournalEntryId.ShouldNotBeNull();

        // 行留着（作废单据仍是审计对象），声明放开
        voided.Data.Lines.Count.ShouldBe(2);
        voided.Data.Lines.ShouldAllBe(l => !l.IsClaimActive);
        voided.Data.Lines.Select(l => l.PaymentEntryId).ShouldBe(new Guid?[] { a.Id, b.Id }, ignoreOrder: true);

        // 两张收款重新可选，且能进一张新的存款单
        var candidates = await UndepositedAsync(undeposited);
        candidates.Data!.Select(c => c.PaymentEntryId).ShouldBe(new[] { a.Id, b.Id }, ignoreOrder: true);

        var again = await CreateDepositAsync(undeposited, bank, [a.Id, b.Id], new DateTime(2026, 3, 26));
        again.Succeeded.ShouldBeTrue(again.Message);

        // 总账：过账 + 冲销净额为零，待存款项回到 500
        (await ClosingBalanceAsync(undeposited, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31))).ShouldBe(500m);
        (await ClosingBalanceAsync(bank, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31))).ShouldBe(0m);
    }

    /// <summary>删除草稿同样释放收款（草稿从未过账，行硬删无证据链损失）</summary>
    [Fact]
    public async Task DeleteDraft_ReleasesReceipts()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var customer = await CreateCustomerAsync("Acme");
        var receipt = await PostReceiptAsync(customer.Data!.Id, undeposited, 120m);

        var draft = await CreateDepositAsync(undeposited, bank, [receipt.Id]);
        draft.Succeeded.ShouldBeTrue(draft.Message);
        (await UndepositedAsync(undeposited)).Data!.ShouldBeEmpty();

        var deleted = await InScopeAsync<IDepositService, Result>(s => s.DeleteDraftAsync(draft.Data!.Id));
        deleted.Succeeded.ShouldBeTrue(deleted.Message);

        (await UndepositedAsync(undeposited)).Data!.Single().PaymentEntryId.ShouldBe(receipt.Id);
    }

    /// <summary>
    /// 更新草稿：移出的收款即刻释放，留下的那张<b>不会</b>被自己的声明挡住。
    /// </summary>
    [Fact]
    public async Task UpdateDraft_ReplacesLines_AndDoesNotCollideWithItsOwnClaims()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var customer = await CreateCustomerAsync("Acme");
        var a = await PostReceiptAsync(customer.Data!.Id, undeposited, 100m);
        var b = await PostReceiptAsync(customer.Data.Id, undeposited, 200m);
        var c = await PostReceiptAsync(customer.Data.Id, undeposited, 300m);

        var draft = await CreateDepositAsync(undeposited, bank, [a.Id, b.Id]);
        draft.Succeeded.ShouldBeTrue(draft.Message);

        // a 留下（自己的声明不得挡住自己）、b 移出、c 加入
        var updated = await InScopeAsync<IDepositService, Result<DepositDto>>(s => s.UpdateDraftAsync(draft.Data!.Id, new CreateDepositDto
        {
            FromAccountId = undeposited,
            ToAccountId = bank,
            DepositDate = new DateTime(2026, 3, 25),
            PaymentEntryIds = [a.Id, c.Id],
            OtherFunds = []
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.Amount.ShouldBe(400m);
        updated.Data.Lines.Select(l => l.PaymentEntryId).ShouldBe(new Guid?[] { a.Id, c.Id }, ignoreOrder: true);

        (await UndepositedAsync(undeposited)).Data!.Single().PaymentEntryId.ShouldBe(b.Id);
    }

    /// <summary>
    /// 准则 6 —— 「其它款项」行不得贷记 A/R、A/P 控制科目。
    /// </summary>
    /// <remarks>
    /// 从存款单直接贷记应收：发票依旧未清、账上同时多一笔无处核销的贷项，
    /// 而账面总额是平的 —— 没有任何报表会因此报警。
    /// </remarks>
    [Fact]
    public async Task OtherFundsLine_RejectsArAndApControlAccounts()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var ar = await AccountIdByCodeAsync("1200");
        var ap = await AccountIdByCodeAsync("2100");

        foreach (var control in new[] { ar, ap })
        {
            var result = await CreateDepositAsync(undeposited, bank, [], otherFunds:
            [
                new CreateDepositFundsLineDto { AccountId = control, Amount = 100m }
            ]);
            result.Succeeded.ShouldBeFalse("贷记控制科目会留下未清发票 + 无主贷项");
            result.Code.ShouldBe(400);
            result.Message!.ShouldContain("control account");
        }
    }

    /// <summary>「其它款项」行也不得贷记本单自己的来源/目标科目</summary>
    [Fact]
    public async Task OtherFundsLine_RejectsTheDepositsOwnAccounts()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");

        foreach (var own in new[] { undeposited, bank })
        {
            var result = await CreateDepositAsync(undeposited, bank, [], otherFunds:
            [
                new CreateDepositFundsLineDto { AccountId = own, Amount = 100m }
            ]);
            result.Succeeded.ShouldBeFalse();
            result.Code.ShouldBe(400);
        }
    }

    /// <summary>
    /// 候选清单只列「已过账 + Inbound + 落在该科目 + 未被声明」的收款。
    /// </summary>
    [Fact]
    public async Task UndepositedList_ExcludesDrafts_OtherAccounts_AndClaimedReceipts()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var customer = await CreateCustomerAsync("Acme");

        var onUndeposited = await PostReceiptAsync(customer.Data!.Id, undeposited, 100m);
        await PostReceiptAsync(customer.Data.Id, bank, 200m); // 直接进银行，不是候选

        // 草稿收款不是候选
        var draftReceipt = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = customer.Data.Id,
            DocDate = new DateTime(2026, 3, 20),
            Amount = 50m,
            DepositToAccountId = undeposited
        }));
        draftReceipt.Succeeded.ShouldBeTrue(draftReceipt.Message);

        var listed = await UndepositedAsync(undeposited);
        listed.Succeeded.ShouldBeTrue(listed.Message);
        listed.Data!.Single().PaymentEntryId.ShouldBe(onUndeposited.Id);
        listed.Data![0].PartyName.ShouldBe("Acme");

        // 被声明之后即从候选中消失
        var draft = await CreateDepositAsync(undeposited, bank, [onUndeposited.Id]);
        draft.Succeeded.ShouldBeTrue(draft.Message);
        (await UndepositedAsync(undeposited)).Data!.ShouldBeEmpty();
    }

    /// <summary>
    /// 已被存款单收走的收款不能再作废 —— 否则待存款项的同一笔钱被贷两次，
    /// 而每张凭证自身都是平的，试算平衡恒为零，没有任何地方会报警。
    /// </summary>
    [Fact]
    public async Task VoidingAReceipt_IsRefusedWhileADepositHoldsIt()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var customer = await CreateCustomerAsync("Acme");
        var receipt = await PostReceiptAsync(customer.Data!.Id, undeposited, 400m);

        // ① 草稿也拦：放行的话草稿会装着一笔已作废收款，到过账那刻才炸
        var draft = await CreateDepositAsync(undeposited, bank, [receipt.Id]);
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var blockedByDraft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.VoidAsync(receipt.Id));
        blockedByDraft.Succeeded.ShouldBeFalse();
        blockedByDraft.Code.ShouldBe(409);
        blockedByDraft.Message!.ShouldContain("draft deposit");

        // ② 过账之后同样拦，提示语指向「先作废那张存款单」
        var posted = await InScopeAsync<IDepositService, Result<DepositDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var blockedByPosted = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.VoidAsync(receipt.Id));
        blockedByPosted.Succeeded.ShouldBeFalse();
        blockedByPosted.Code.ShouldBe(409);
        blockedByPosted.Message!.ShouldContain(posted.Data!.Number!);

        // ③ 存款单作废后，收款才回到可作废状态
        var voidedDeposit = await InScopeAsync<IDepositService, Result<DepositDto>>(s => s.VoidAsync(posted.Data.Id));
        voidedDeposit.Succeeded.ShouldBeTrue(voidedDeposit.Message);

        var voidedReceipt = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.VoidAsync(receipt.Id));
        voidedReceipt.Succeeded.ShouldBeTrue(voidedReceipt.Message);
    }

    /// <summary>
    /// 存款单的凭证不能从总账端点直接冲销：那样凭证冲了、单据仍是 Posted，
    /// 它收走的收款也永远被声明占着。
    /// </summary>
    [Fact]
    public async Task GlReverse_OnADepositEntry_IsRefused()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var customer = await CreateCustomerAsync("Acme");
        var receipt = await PostReceiptAsync(customer.Data!.Id, undeposited, 700m);

        var draft = await CreateDepositAsync(undeposited, bank, [receipt.Id]);
        var posted = await InScopeAsync<IDepositService, Result<DepositDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var reversed = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(posted.Data!.JournalEntryId!.Value, new ReverseJournalEntryDto()));
        reversed.Succeeded.ShouldBeFalse();
        reversed.Code.ShouldBe(409);
        reversed.Message!.ShouldContain(FinanceSourceTypes.Deposit);
    }

    /// <summary>
    /// 准则 7 —— 期间封账照常拦下过账（守卫在引擎里，本单据不另写一份）。
    /// </summary>
    [Fact]
    public async Task Post_RespectsTheClosingDate()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var customer = await CreateCustomerAsync("Acme");
        var receipt = await PostReceiptAsync(customer.Data!.Id, undeposited, 250m);

        var draft = await CreateDepositAsync(undeposited, bank, [receipt.Id], new DateTime(2026, 3, 25));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var locked = await InScopeAsync<ILedgerLockService, Result<LedgerLockDto>>(
            s => s.SetAsync(new SetLedgerLockDto { ClosingDate = new DateTime(2026, 3, 31) }));
        locked.Succeeded.ShouldBeTrue(locked.Message);

        var posted = await InScopeAsync<IDepositService, Result<DepositDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeFalse("封账日之前的日期不得过账");
    }

    /// <summary>收款必须就落在本单的来源科目上，且币种一致</summary>
    [Fact]
    public async Task Draft_RejectsReceiptsFromAnotherAccountOrCurrency()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var customer = await CreateCustomerAsync("Acme");

        var onBank = await PostReceiptAsync(customer.Data!.Id, bank, 100m);
        var wrongAccount = await CreateDepositAsync(undeposited, bank, [onBank.Id]);
        wrongAccount.Succeeded.ShouldBeFalse("这张收款不在本单的来源科目上");

        await UpsertRateAsync("EUR", "USD", 1.1m, new DateTime(2026, 3, 20));
        var eurReceipt = await PostReceiptAsync(customer.Data.Id, undeposited, 100m, new DateTime(2026, 3, 20), "EUR", 1.1m);
        var wrongCurrency = await CreateDepositAsync(undeposited, bank, [eurReceipt.Id]);
        wrongCurrency.Succeeded.ShouldBeFalse("一张存款单只能是一种币");
    }

    /// <summary>空单据、重复收款、两侧同科目 —— 三条最容易撞上的输入错误</summary>
    [Fact]
    public async Task Draft_RejectsEmptyDuplicateAndSameAccountInput()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var customer = await CreateCustomerAsync("Acme");
        var receipt = await PostReceiptAsync(customer.Data!.Id, undeposited, 100m);

        (await CreateDepositAsync(undeposited, bank, [])).Succeeded.ShouldBeFalse("空存款单");
        (await CreateDepositAsync(undeposited, bank, [receipt.Id, receipt.Id])).Succeeded.ShouldBeFalse("同一张收款不能出现两次");
        (await CreateDepositAsync(undeposited, undeposited, [receipt.Id])).Succeeded.ShouldBeFalse("来源与目标不能是同一科目");
    }

    /// <summary>
    /// 真实的待存款项路径：收款<b>不指定存入科目</b>，由 <c>PostToUndepositedFunds</c>
    /// 回退到 UndepositedFunds 角色科目，再由存款单带进银行。
    /// </summary>
    /// <remarks>
    /// 其余用例都显式传 <c>DepositToAccountId</c>，等于绕开了这个开关 —— 而它正是整张单据
    /// 存在的理由。让链路成立的是 <c>PaymentEntryService</c> 过账时的
    /// <c>payment.DepositToAccountId ??= fundsAccountId</c> 回填：少了它，
    /// <c>ValidateReceipt</c> 的「收款须落在本单来源科目上」永不成立，而其余用例全绿。
    /// </remarks>
    [Fact]
    public async Task Deposit_WorksOffTheUndepositedFundsFallback_NotJustAnExplicitAccount()
    {
        PostToUndepositedFundsOption = true;

        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var customer = await CreateCustomerAsync("Acme");
        customer.Succeeded.ShouldBeTrue(customer.Message);

        // 存入科目留空：过账时按角色解析待存款项，并把解析结果回填到收款单上
        var receipt = await PostReceiptAsync(customer.Data!.Id, null, 1500m);
        receipt.DepositToAccountId.ShouldBe(undeposited, "过账须把回退解析出的待存款项科目回填到收款单");

        // 回填的科目就是候选清单的键：不回填，这张收款在编辑器里根本不出现
        var candidates = await UndepositedAsync(undeposited);
        candidates.Succeeded.ShouldBeTrue(candidates.Message);
        candidates.Data!.Select(c => c.PaymentEntryId).ShouldContain(receipt.Id);

        var draft = await CreateDepositAsync(undeposited, bank, [receipt.Id]);
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<IDepositService, Result<DepositDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var period = (From: new DateTime(2026, 3, 1), To: new DateTime(2026, 3, 31));
        (await ClosingBalanceAsync(undeposited, period.From, period.To)).ShouldBe(0m);
        (await ClosingBalanceAsync(bank, period.From, period.To)).ShouldBe(1500m);
    }

    /// <summary>
    /// 列表投影<b>不</b>带行：DTO 上写着「仅详情填充」，那就必须真的是空的 ——
    /// 否则整页每张单据的全部行都被拉下来，而列表一行都不显示它们。
    /// </summary>
    [Fact]
    public async Task PagedList_DoesNotCarryLines()
    {
        await SeedCoaAsync();
        var undeposited = await AccountIdByCodeAsync("1130");
        var bank = await AccountIdByCodeAsync("1120");
        var customer = await CreateCustomerAsync("Acme");
        var r1 = await PostReceiptAsync(customer.Data!.Id, undeposited, 100m);
        var r2 = await PostReceiptAsync(customer.Data!.Id, undeposited, 200m);

        var draft = await CreateDepositAsync(undeposited, bank, [r1.Id, r2.Id]);
        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.Lines.Count.ShouldBe(2, "详情/创建返回仍要带行");

        var paged = await InScopeAsync<IDepositService, Result<IPagedList<DepositDto>>>(
            s => s.GetPagedAsync(new DepositQueryDto { PageIndex = 1, PageSize = 10 }));
        paged.Succeeded.ShouldBeTrue(paged.Message);
        paged.Data!.Items.Single().Lines.ShouldBeEmpty();
    }
}
