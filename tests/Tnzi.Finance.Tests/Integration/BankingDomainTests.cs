namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// P3a 银行域：资金划转单（单据范式）与银行对账（join 表勾选 + 差额门 + 完成锁定）
/// </summary>
public class BankingDomainTests : FinanceIntegrationTestBase
{
    private async Task<Guid> AccountIdAsync(string code)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var account = await repo.FirstOrDefaultAsync(a => a.Code == code);
        account.ShouldNotBeNull($"account {code}");
        return account.Id;
    }

    private Task<Result<TransferDto>> CreateTransferAsync(Guid from, Guid to, decimal amount, DateTime date)
        => InScopeAsync<ITransferService, Result<TransferDto>>(s => s.CreateDraftAsync(new CreateTransferDto
        {
            FromAccountId = from,
            ToAccountId = to,
            TransferDate = date,
            Amount = amount
        }));

    [Fact]
    public async Task Transfer_FullLifecycle_PostsAndVoids()
    {
        await SeedCoaAsync();
        var bank = await AccountIdAsync("1120");
        var cash = await AccountIdAsync("1110");

        var draft = await CreateTransferAsync(bank, cash, 300m, new DateTime(2026, 3, 10));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.Status.ShouldBe(FinanceDocumentStatus.Draft);
        draft.Data.Number.ShouldBeNull();
        draft.Data.FromAccountName.ShouldNotBeNull();

        var posted = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.PostAsync(draft.Data.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.Status.ShouldBe(FinanceDocumentStatus.Posted);
        posted.Data.Number.ShouldBe("TRF-000001");
        posted.Data.BaseAmount.ShouldBe(300m);
        posted.Data.JournalEntryId.ShouldNotBeNull();

        // 过账凭证：借 转入（现金）/ 贷 转出（银行）
        var entry = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.GetAsync(posted.Data.JournalEntryId!.Value));
        entry.Data!.Lines.Single(l => l.AccountId == cash).Debit.ShouldBe(300m);
        entry.Data.Lines.Single(l => l.AccountId == bank).Credit.ShouldBe(300m);

        var voided = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.VoidAsync(posted.Data.Id));
        voided.Succeeded.ShouldBeTrue(voided.Message);
        voided.Data!.Status.ShouldBe(FinanceDocumentStatus.Voided);
        voided.Data.VoidJournalEntryId.ShouldNotBeNull();

        // 冲销后总账净额归零
        var gl = await InScopeAsync<IFinancialReportService, Result<GeneralLedgerReportDto>>(
            s => s.GetGeneralLedgerAsync(cash, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), new PagedQueryDto { PageIndex = 1, PageSize = 10 }));
        gl.Data!.ClosingBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task Transfer_RejectsNonFundsOrSameAccount()
    {
        await SeedCoaAsync();
        var bank = await AccountIdAsync("1120");
        var ar = await AccountIdAsync("1200"); // Operating，非资金科目

        var nonFunds = await CreateTransferAsync(bank, ar, 100m, new DateTime(2026, 3, 10));
        nonFunds.Succeeded.ShouldBeFalse();
        nonFunds.Message.ShouldNotBeNull();
        nonFunds.Message.ShouldContain("CashEquivalent");

        var same = await CreateTransferAsync(bank, bank, 100m, new DateTime(2026, 3, 10));
        same.Succeeded.ShouldBeFalse();
        same.Message.ShouldNotBeNull();
        same.Message.ShouldContain("different");
    }

    private async Task SeedBankLedgerAsync(Guid bank)
    {
        // 两笔入账 + 一笔支出：银行余额 = 500 + 200 - 100 = 600
        (await PostLedgerAsync(Posting(new DateTime(2026, 3, 5), "in-1",
            new LedgerPostingLine { AccountId = bank, Debit = 500m },
            new LedgerPostingLine { AccountCode = "3100", Credit = 500m }))).Succeeded.ShouldBeTrue();
        (await PostLedgerAsync(Posting(new DateTime(2026, 3, 10), "in-2",
            new LedgerPostingLine { AccountId = bank, Debit = 200m },
            new LedgerPostingLine { AccountCode = "3100", Credit = 200m }))).Succeeded.ShouldBeTrue();
        (await PostLedgerAsync(Posting(new DateTime(2026, 3, 15), "out-1",
            new LedgerPostingLine { AccountCode = "5200", Debit = 100m },
            new LedgerPostingLine { AccountId = bank, Credit = 100m }))).Succeeded.ShouldBeTrue();
    }

    private static LedgerPostingRequest Posting(DateTime date, string sourceId, params LedgerPostingLine[] lines)
        => new()
        {
            PostingDate = date,
            SourceType = "Test.Bank",
            SourceId = sourceId,
            Lines = [.. lines]
        };

    [Fact]
    public async Task GetPaged_FillsClearedBalanceAndDifference()
    {
        // 列表 DTO 的 Difference=0 必须意味着"已配平"，绝不能是"没算" ——
        // 消费方直接渲染列表 DTO，0 与未计算不可区分就会把"差着几千"显示成"已平"
        await SeedCoaAsync();
        var bank = await AccountIdAsync("1120");
        await SeedBankLedgerAsync(bank);

        var draft = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CreateDraftAsync(new CreateReconciliationDto
            {
                AccountId = bank,
                StatementDate = new DateTime(2026, 3, 31),
                StatementEndingBalance = 600m
            }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        var id = draft.Data!.Id;

        async Task<ReconciliationDto> ListedAsync()
        {
            var page = await InScopeAsync<IReconciliationService, Result<IPagedList<ReconciliationDto>>>(
                s => s.GetPagedAsync(new ReconciliationQueryDto()));
            page.Succeeded.ShouldBeTrue(page.Message);
            return page.Data!.Items.Single(r => r.Id == id);
        }

        // 一条未勾：cleared 0、差额 = 全额 600
        var row = await ListedAsync();
        row.ClearedBalance.ShouldBe(0m);
        row.Difference.ShouldBe(600m);

        // 与单实体读同口径
        var single = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(s => s.GetAsync(id));
        row.Difference.ShouldBe(single.Data!.Difference);
        row.ClearedBalance.ShouldBe(single.Data.ClearedBalance);

        var worksheet = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.GetWorksheetAsync(id));
        var allLineIds = worksheet.Data!.Lines.Select(l => l.JournalLineId).ToList();

        // 部分勾选 → 列表随之更新
        (await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(id, new SetReconciliationLinesDto { JournalLineIds = [allLineIds[0]] }))).Succeeded.ShouldBeTrue();
        row = await ListedAsync();
        row.ClearedBalance.ShouldBe(500m);
        row.Difference.ShouldBe(100m);

        // 完成后冻结为完成时刻的事实（差额 0），不随后续对账推进重算
        (await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(id, new SetReconciliationLinesDto { JournalLineIds = allLineIds }))).Succeeded.ShouldBeTrue();
        (await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(s => s.CompleteAsync(id))).Succeeded.ShouldBeTrue();

        row = await ListedAsync();
        row.ClearedBalance.ShouldBe(600m);
        row.Difference.ShouldBe(0m);
    }

    /// <summary>
    /// 回归（完成竞态）：勾选行本身无并发令牌，SetLinesAsync 必须触碰父对账以轮换其并发戳，
    /// 才能与 CompleteAsync 的父行更新互斥。若戳不变，"读到 Draft→并发完成→再插行进已完成对账"
    /// 的 TOCTOU 会静默漂移累计 cleared 锚点。此处断言机制：任何勾选变更后父戳必变。
    /// </summary>
    [Fact]
    public async Task Reconciliation_SetLines_BumpsParentConcurrencyStamp()
    {
        await SeedCoaAsync();
        var bank = await AccountIdAsync("1120");
        await SeedBankLedgerAsync(bank);

        var draft = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CreateDraftAsync(new CreateReconciliationDto
            {
                AccountId = bank,
                StatementDate = new DateTime(2026, 3, 31),
                StatementEndingBalance = 600m
            }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var before = (await ReloadAsync<Reconciliation>(draft.Data!.Id))!.ConcurrencyStamp;

        var worksheet = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.GetWorksheetAsync(draft.Data.Id));
        var set = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(draft.Data.Id, new SetReconciliationLinesDto
            {
                JournalLineIds = [worksheet.Data!.Lines[0].JournalLineId]
            }));
        set.Succeeded.ShouldBeTrue(set.Message);

        var after = (await ReloadAsync<Reconciliation>(draft.Data.Id))!.ConcurrencyStamp;
        after.ShouldNotBe(before);
    }

    [Fact]
    public async Task Reconciliation_FullFlow_ClearsLinesAndCompletes()
    {
        await SeedCoaAsync();
        var bank = await AccountIdAsync("1120");
        await SeedBankLedgerAsync(bank);

        var draft = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CreateDraftAsync(new CreateReconciliationDto
            {
                AccountId = bank,
                StatementDate = new DateTime(2026, 3, 31),
                StatementEndingBalance = 600m
            }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.Status.ShouldBe(ReconciliationStatus.Draft);
        draft.Data.Difference.ShouldBe(600m);

        // 同科目第二张 Draft 被拒
        var second = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CreateDraftAsync(new CreateReconciliationDto { AccountId = bank, StatementDate = new DateTime(2026, 4, 30), StatementEndingBalance = 0m }));
        second.Succeeded.ShouldBeFalse();
        second.Code.ShouldBe(409);

        var worksheet = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.GetWorksheetAsync(draft.Data.Id));
        worksheet.Data!.Lines.Count.ShouldBe(3);
        worksheet.Data.Lines.ShouldAllBe(l => !l.IsSelected);

        // 部分勾选：差额非 0，完成被拒
        var partial = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(draft.Data.Id, new SetReconciliationLinesDto
            {
                JournalLineIds = [worksheet.Data.Lines[0].JournalLineId]
            }));
        partial.Succeeded.ShouldBeTrue(partial.Message);
        partial.Data!.ClearedBalance.ShouldBe(500m);
        partial.Data.Difference.ShouldBe(100m);

        var premature = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CompleteAsync(draft.Data.Id));
        premature.Succeeded.ShouldBeFalse();
        premature.Code.ShouldBe(400);

        // 全部勾选（含支出行——收支互抵语义）：差额 0
        var full = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(draft.Data.Id, new SetReconciliationLinesDto
            {
                JournalLineIds = worksheet.Data.Lines.Select(l => l.JournalLineId).ToList()
            }));
        full.Data!.ClearedBalance.ShouldBe(600m);
        full.Data.Difference.ShouldBe(0m);

        var completed = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CompleteAsync(draft.Data.Id));
        completed.Succeeded.ShouldBeTrue(completed.Message);
        completed.Data!.Status.ShouldBe(ReconciliationStatus.Completed);
        completed.Data.CompletedTime.ShouldNotBeNull();
        completed.Data.LineCount.ShouldBe(3);

        // 完成后锁定：改勾选/头字段均 409
        var lockedLines = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(draft.Data.Id, new SetReconciliationLinesDto { JournalLineIds = [] }));
        lockedLines.Succeeded.ShouldBeFalse();
        lockedLines.Code.ShouldBe(409);

        // 下一期对账：已勾选行不再出现在候选中
        var next = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CreateDraftAsync(new CreateReconciliationDto
            {
                AccountId = bank,
                StatementDate = new DateTime(2026, 4, 30),
                StatementEndingBalance = 600m
            }));
        next.Succeeded.ShouldBeTrue(next.Message);
        var nextWorksheet = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.GetWorksheetAsync(next.Data!.Id));
        nextWorksheet.Data!.Lines.ShouldBeEmpty();
        nextWorksheet.Data.ClearedBalance.ShouldBe(600m);
        nextWorksheet.Data.Difference.ShouldBe(0m);

        // 已完成对账的差额被冻结为完成时刻的事实：后续期间新增过账/勾选不得使其漂移
        (await PostLedgerAsync(Posting(new DateTime(2026, 4, 5), "late-in",
            new LedgerPostingLine { AccountCode = "1120", Debit = 77m },
            new LedgerPostingLine { AccountCode = "3100", Credit = 77m }))).Succeeded.ShouldBeTrue();

        var completedAgain = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.GetAsync(draft.Data.Id));
        completedAgain.Data!.Status.ShouldBe(ReconciliationStatus.Completed);
        completedAgain.Data.Difference.ShouldBe(0m);
        completedAgain.Data.ClearedBalance.ShouldBe(600m);

        var completedWorksheet = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.GetWorksheetAsync(draft.Data.Id));
        completedWorksheet.Data!.Difference.ShouldBe(0m);
    }

    [Fact]
    public async Task Reconciliation_RejectsNonFundsAccount_AndForeignLines()
    {
        await SeedCoaAsync();
        var ar = await AccountIdAsync("1200");

        var rejected = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CreateDraftAsync(new CreateReconciliationDto { AccountId = ar, StatementDate = new DateTime(2026, 3, 31), StatementEndingBalance = 0m }));
        rejected.Succeeded.ShouldBeFalse();
        rejected.Message.ShouldNotBeNull();
        rejected.Message.ShouldContain("CashEquivalent");

        // 勾选不属于对账科目的行被拒
        var bank = await AccountIdAsync("1120");
        await SeedBankLedgerAsync(bank);
        var draft = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CreateDraftAsync(new CreateReconciliationDto { AccountId = bank, StatementDate = new DateTime(2026, 3, 31), StatementEndingBalance = 600m }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var lineRepo = ServiceProvider.GetRequiredService<IRepository<JournalLine, Guid>>();
        var foreignLine = await lineRepo.FirstOrDefaultAsync(l => l.AccountId != bank && l.IsPosted);
        foreignLine.ShouldNotBeNull();

        var setForeign = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(draft.Data!.Id, new SetReconciliationLinesDto { JournalLineIds = [foreignLine.Id] }));
        setForeign.Succeeded.ShouldBeFalse();
        setForeign.Code.ShouldBe(400);

        // 删除草稿：勾选行级联硬删
        var deleteResult = await InScopeAsync<IReconciliationService, Result>(s => s.DeleteDraftAsync(draft.Data!.Id));
        deleteResult.Succeeded.ShouldBeTrue(deleteResult.Message);
    }
}
