namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 凭证草稿工作流：创建/更新/删除草稿、过账、冲销、状态机守卫
/// </summary>
public class JournalEntryWorkflowTests : FinanceIntegrationTestBase
{
    private async Task<(Guid ArAccountId, Guid RevenueAccountId)> SeedAndResolveAsync()
    {
        await SeedCoaAsync();
        var ar = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("1200"));
        var revenue = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("4100"));
        return (ar!.Id, revenue!.Id);
    }

    private static CreateJournalEntryDto DraftInput(Guid debitAccountId, Guid creditAccountId, decimal amount)
        => new()
        {
            PostingDate = new DateTime(2026, 3, 20),
            Memo = "Manual entry",
            Lines =
            [
                new CreateJournalLineDto { AccountId = debitAccountId, Debit = amount },
                new CreateJournalLineDto { AccountId = creditAccountId, Credit = amount }
            ]
        };

    [Fact]
    public async Task CreateDraft_Then_Post_Succeeds()
    {
        var (ar, revenue) = await SeedAndResolveAsync();

        var draft = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.CreateDraftAsync(DraftInput(ar, revenue, 500m)));

        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.Status.ShouldBe(JournalEntryStatus.Draft);
        draft.Data.Number.ShouldBeNull();

        var posted = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.PostAsync(draft.Data.Id));

        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.Status.ShouldBe(JournalEntryStatus.Posted);
        posted.Data.Number.ShouldBe("JE-000001");
        posted.Data.TotalDebit.ShouldBe(500m);
        posted.Data.Lines.Count.ShouldBe(2);
        posted.Data.Lines[0].AccountCode.ShouldBe("1200");
    }

    [Fact]
    public async Task CreateDraft_UnknownAccount_Fails()
    {
        await SeedCoaAsync();

        var result = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.CreateDraftAsync(DraftInput(Guid.NewGuid(), Guid.NewGuid(), 100m)));

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("accounts do not exist");
    }

    [Fact]
    public async Task Post_UnbalancedDraft_Fails_AndStaysDraft()
    {
        var (ar, revenue) = await SeedAndResolveAsync();

        var input = DraftInput(ar, revenue, 100m);
        input.Lines[1].Credit = 60m;
        var draft = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.CreateDraftAsync(input));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeFalse();

        var reloaded = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(draft.Data!.Id));
        reloaded.Data!.Status.ShouldBe(JournalEntryStatus.Draft);
        reloaded.Data.Number.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateDraft_ReplacesLines()
    {
        var (ar, revenue) = await SeedAndResolveAsync();

        var draft = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.CreateDraftAsync(DraftInput(ar, revenue, 100m)));

        var updatedInput = DraftInput(ar, revenue, 250m);
        updatedInput.Memo = "Updated memo";
        var updated = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.UpdateDraftAsync(draft.Data!.Id, updatedInput));

        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.Memo.ShouldBe("Updated memo");

        var reloaded = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(draft.Data!.Id));
        reloaded.Data!.Lines.Count.ShouldBe(2);
        reloaded.Data.Lines.Sum(l => l.TxnDebit).ShouldBe(250m);
    }

    [Fact]
    public async Task UpdateOrDelete_PostedEntry_Fails()
    {
        var (ar, revenue) = await SeedAndResolveAsync();

        var draft = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.CreateDraftAsync(DraftInput(ar, revenue, 100m)));
        var posted = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var update = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.UpdateDraftAsync(draft.Data!.Id, DraftInput(ar, revenue, 999m)));
        update.Succeeded.ShouldBeFalse();
        update.Code.ShouldBe(409);

        var delete = await InScopeAsync<IJournalEntryService, Result>(s => s.DeleteDraftAsync(draft.Data!.Id));
        delete.Succeeded.ShouldBeFalse();
        delete.Code.ShouldBe(409);

        var postAgain = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        postAgain.Succeeded.ShouldBeFalse();
        postAgain.Code.ShouldBe(409);
    }

    [Fact]
    public async Task DeleteDraft_RemovesEntry()
    {
        var (ar, revenue) = await SeedAndResolveAsync();

        var draft = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.CreateDraftAsync(DraftInput(ar, revenue, 100m)));

        var delete = await InScopeAsync<IJournalEntryService, Result>(s => s.DeleteDraftAsync(draft.Data!.Id));
        delete.Succeeded.ShouldBeTrue(delete.Message);

        var reloaded = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(draft.Data!.Id));
        reloaded.Succeeded.ShouldBeFalse();
        reloaded.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Reverse_PostedEntry_CreatesMirroredEntry_AndMarksOriginal()
    {
        var (ar, revenue) = await SeedAndResolveAsync();

        var draft = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.CreateDraftAsync(DraftInput(ar, revenue, 300m)));
        var posted = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var reversed = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(draft.Data!.Id, new ReverseJournalEntryDto()));

        reversed.Succeeded.ShouldBeTrue(reversed.Message);
        var reversal = reversed.Data!;
        reversal.Status.ShouldBe(JournalEntryStatus.Posted);
        reversal.Number.ShouldBe("JE-000002");
        reversal.ReversalOfEntryId.ShouldBe(draft.Data!.Id);
        reversal.Memo.ShouldNotBeNull();
        reversal.Memo.ShouldContain("Reversal of JE-000001");

        // 借贷互换
        var arLine = reversal.Lines.Single(l => l.AccountCode == "1200");
        arLine.Credit.ShouldBe(300m);
        arLine.Debit.ShouldBe(0m);

        var original = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(draft.Data!.Id));
        original.Data!.Status.ShouldBe(JournalEntryStatus.Reversed);
        original.Data.ReversedByEntryId.ShouldBe(reversal.Id);

        // 二次冲销被拒绝
        var again = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(draft.Data!.Id, new ReverseJournalEntryDto()));
        again.Succeeded.ShouldBeFalse();
        again.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Post_FailedAfterConversion_DoesNotPersistBaseAmounts()
    {
        var (ar, revenue) = await SeedAndResolveAsync();

        // 提供汇率，但停用舍入差额科目：失败点位于本位币换算之后
        // → 回归校验"失败过账"不得把半转换金额残留到被跟踪的草稿上
        var rateResult = await InScopeAsync<IExchangeRateService, Result<ExchangeRateDto>>(s => s.UpsertAsync(new UpsertExchangeRateDto
        {
            FromCurrency = "EUR",
            ToCurrency = "USD",
            Rate = 1.115m,
            RateDate = new DateTime(2026, 1, 1)
        }));
        rateResult.Succeeded.ShouldBeTrue(rateResult.Message);

        var rounding = await InScopeAsync<IChartOfAccountsService, Account?>(
            s => s.FindByRoleAsync(AccountSystemRole.RoundingDifference));
        var deactivate = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.UpdateAsync(rounding!.Id, new UpdateAccountDto
        {
            Code = rounding.Code,
            Name = rounding.Name,
            ParentId = rounding.ParentId,
            SystemRole = rounding.SystemRole,
            IsActive = false
        }));
        deactivate.Succeeded.ShouldBeTrue(deactivate.Message);

        // 33.33/33.34 借 + 66.67 贷 @1.115 → 尾差 -0.01，需要舍入差额科目 → 失败
        var input = new CreateJournalEntryDto
        {
            PostingDate = new DateTime(2026, 3, 20),
            Currency = "EUR",
            Lines =
            [
                new CreateJournalLineDto { AccountId = ar, Debit = 33.33m },
                new CreateJournalLineDto { AccountId = ar, Debit = 33.34m },
                new CreateJournalLineDto { AccountId = revenue, Credit = 66.67m }
            ]
        };
        var draft = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.CreateDraftAsync(input));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeFalse();
        posted.Message.ShouldNotBeNull();
        posted.Message.ShouldContain("RoundingDifference");

        // 失败过账后草稿必须原封不动：无本位币金额、仍是 Draft、无凭证号
        var reloaded = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(draft.Data!.Id));
        reloaded.Data!.Status.ShouldBe(JournalEntryStatus.Draft);
        reloaded.Data.Number.ShouldBeNull();
        reloaded.Data.Lines.ShouldAllBe(l => l.Debit == 0m && l.Credit == 0m);
    }

    [Fact]
    public async Task Post_RotatesConcurrencyStamp()
    {
        var (ar, revenue) = await SeedAndResolveAsync();

        var draft = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.CreateDraftAsync(DraftInput(ar, revenue, 100m)));
        var before = (await ReloadAsync<JournalEntry>(draft.Data!.Id))!.ConcurrencyStamp;
        before.ShouldNotBeNullOrEmpty();

        var posted = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        // 乐观并发戳随过账轮换（并发过账/冲销的失败方由此在提交时被拒绝并整体回滚）
        var after = (await ReloadAsync<JournalEntry>(draft.Data!.Id))!.ConcurrencyStamp;
        after.ShouldNotBeNullOrEmpty();
        after.ShouldNotBe(before);
    }

    [Fact]
    public async Task Reverse_Draft_Fails()
    {
        var (ar, revenue) = await SeedAndResolveAsync();

        var draft = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.CreateDraftAsync(DraftInput(ar, revenue, 100m)));

        var result = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(draft.Data!.Id, new ReverseJournalEntryDto()));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
    }
}
