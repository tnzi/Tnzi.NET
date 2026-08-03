namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 账本封账锁：滚动封账日 + 口令门 + 与会计年度锁的正交性
/// </summary>
public class LedgerLockTests : FinanceIntegrationTestBase
{
    private Task<Result<LedgerLockDto>> SetAsync(SetLedgerLockDto input)
        => InScopeAsync<ILedgerLockService, Result<LedgerLockDto>>(s => s.SetAsync(input));

    private Task<Result<LedgerLockDto>> GetAsync()
        => InScopeAsync<ILedgerLockService, Result<LedgerLockDto>>(s => s.GetAsync());

    /// <summary>在给定日期过一张最小的平衡凭证。</summary>
    private async Task<Result<JournalEntryDto>> PostOnAsync(DateTime date)
    {
        var cash = await AccountIdByCodeAsync("1110");
        var revenue = await AccountIdByCodeAsync("4100");
        var draft = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.CreateDraftAsync(new CreateJournalEntryDto
        {
            PostingDate = date,
            Memo = "lock probe",
            Lines = new List<CreateJournalLineDto>
            {
                new() { AccountId = cash, Debit = 10m },
                new() { AccountId = revenue, Credit = 10m }
            }
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        return await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.PostAsync(draft.Data!.Id));
    }

    [Fact]
    public async Task Unset_AllowsAnyDate()
    {
        await SeedCoaAsync();

        var status = await GetAsync();
        status.Succeeded.ShouldBeTrue(status.Message);
        status.Data!.ClosingDate.ShouldBeNull();
        status.Data.IsPasswordProtected.ShouldBeFalse();

        // 从未封账 = 不设限，倒填照常（这是引入本功能前的行为，回归红线）
        var posted = await PostOnAsync(DateTime.UtcNow.Date.AddYears(-2));
        posted.Succeeded.ShouldBeTrue(posted.Message);
    }

    [Fact]
    public async Task ClosingDate_BlocksOnAndBefore_AllowsAfter()
    {
        await SeedCoaAsync();
        var closing = DateTime.UtcNow.Date.AddDays(-30);
        (await SetAsync(new SetLedgerLockDto { ClosingDate = closing, Note = "Filed Q2 GST/HST" })).Succeeded.ShouldBeTrue();

        // 封账日当天也被挡住（"封到 6/30" 的日常语义是含当日）
        var onClosing = await PostOnAsync(closing);
        onClosing.Succeeded.ShouldBeFalse();
        onClosing.Code.ShouldBe(409);

        var before = await PostOnAsync(closing.AddDays(-1));
        before.Succeeded.ShouldBeFalse();
        before.Code.ShouldBe(409);

        var after = await PostOnAsync(closing.AddDays(1));
        after.Succeeded.ShouldBeTrue(after.Message);
    }

    [Fact]
    public async Task MovingClosingDateBack_ReopensThePeriod()
    {
        await SeedCoaAsync();
        var closing = DateTime.UtcNow.Date.AddDays(-30);
        await SetAsync(new SetLedgerLockDto { ClosingDate = closing });

        (await PostOnAsync(closing.AddDays(-5))).Succeeded.ShouldBeFalse();

        // 受支持的"越过封账线"路径：把线推回去 → 改 → 推回来。三步各自留痕，
        // 比逐笔输口令放行更可审计（也是刻意不做逐笔越权的原因）。
        (await SetAsync(new SetLedgerLockDto { ClosingDate = closing.AddDays(-10) })).Succeeded.ShouldBeTrue();
        (await PostOnAsync(closing.AddDays(-5))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Clearing_RemovesTheLock()
    {
        await SeedCoaAsync();
        var closing = DateTime.UtcNow.Date.AddDays(-30);
        await SetAsync(new SetLedgerLockDto { ClosingDate = closing });

        (await SetAsync(new SetLedgerLockDto { ClosingDate = null })).Succeeded.ShouldBeTrue();
        (await GetAsync()).Data!.ClosingDate.ShouldBeNull();
        (await PostOnAsync(closing.AddDays(-5))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Password_GatesSubsequentChanges()
    {
        await SeedCoaAsync();
        var closing = DateTime.UtcNow.Date.AddDays(-30);

        var set = await SetAsync(new SetLedgerLockDto { ClosingDate = closing, NewPassword = "s3cret" });
        set.Succeeded.ShouldBeTrue(set.Message);
        set.Data!.IsPasswordProtected.ShouldBeTrue();

        // 无口令 / 错口令都拒，且**拒绝路径零副作用**：封账日不能被一次失败的尝试改掉
        var noPassword = await SetAsync(new SetLedgerLockDto { ClosingDate = closing.AddDays(-20) });
        noPassword.Succeeded.ShouldBeFalse();
        noPassword.Code.ShouldBe(403);

        var wrong = await SetAsync(new SetLedgerLockDto { ClosingDate = closing.AddDays(-20), Password = "nope" });
        wrong.Succeeded.ShouldBeFalse();
        wrong.Code.ShouldBe(403);

        (await GetAsync()).Data!.ClosingDate!.Value.Date.ShouldBe(closing);

        var right = await SetAsync(new SetLedgerLockDto { ClosingDate = closing.AddDays(-20), Password = "s3cret" });
        right.Succeeded.ShouldBeTrue(right.Message);
    }

    [Fact]
    public async Task Password_CanBeClearedWithEmptyString_ButNotByOmission()
    {
        await SeedCoaAsync();
        var closing = DateTime.UtcNow.Date.AddDays(-30);
        await SetAsync(new SetLedgerLockDto { ClosingDate = closing, NewPassword = "s3cret" });

        // 省略 NewPassword = 不动口令（与其它可选修改字段同语义）
        var keep = await SetAsync(new SetLedgerLockDto { ClosingDate = closing, Password = "s3cret" });
        keep.Data!.IsPasswordProtected.ShouldBeTrue();

        // 空串 = 清除
        var cleared = await SetAsync(new SetLedgerLockDto { ClosingDate = closing, Password = "s3cret", NewPassword = "" });
        cleared.Data!.IsPasswordProtected.ShouldBeFalse();

        // 清掉之后不再需要口令
        (await SetAsync(new SetLedgerLockDto { ClosingDate = closing.AddDays(-1) })).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task FutureClosingDate_Rejected()
    {
        await SeedCoaAsync();
        var future = await SetAsync(new SetLedgerLockDto { ClosingDate = DateTime.UtcNow.Date.AddMonths(1) });
        future.Succeeded.ShouldBeFalse();
        future.Code.ShouldBe(400);
    }

    [Fact]
    public async Task ClosingDate_AlsoBlocksReversal()
    {
        await SeedCoaAsync();
        var postingDate = DateTime.UtcNow.Date.AddDays(-40);
        var posted = await PostOnAsync(postingDate);
        posted.Succeeded.ShouldBeTrue(posted.Message);

        await SetAsync(new SetLedgerLockDto { ClosingDate = DateTime.UtcNow.Date.AddDays(-30) });

        // 冲销默认回填到原记账日，因此同一把锁必须挡住它 —— 否则封账线只挡新增
        // 不挡撤销，已报出去的期间照样会变。ReversalGuard 与引擎共用同一个漏斗。
        var reversed = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(posted.Data!.Id, new ReverseJournalEntryDto { Memo = "probe" }));
        reversed.Succeeded.ShouldBeFalse();
        reversed.Code.ShouldBe(409);
    }

    [Fact]
    public async Task ClosingDate_IsOrthogonalToFiscalYearClose()
    {
        await SeedCoaAsync();
        var year = DateTime.UtcNow.Year;
        var fy = await InScopeAsync<IFiscalYearService, Result<FiscalYearDto>>(s => s.CreateAsync(new CreateFiscalYearDto
        {
            Name = $"FY{year - 1}",
            StartDate = new DateTime(year - 1, 1, 1),
            EndDate = new DateTime(year - 1, 12, 31)
        }));
        fy.Succeeded.ShouldBeTrue(fy.Message);
        (await InScopeAsync<IFiscalYearService, Result>(s => s.CloseAsync(fy.Data!.Id))).Succeeded.ShouldBeTrue();

        // 两把锁独立：解除封账日**不会**解开已关闭年度
        await SetAsync(new SetLedgerLockDto { ClosingDate = null });
        var inClosedYear = await PostOnAsync(new DateTime(year - 1, 6, 15));
        inClosedYear.Succeeded.ShouldBeFalse();
        inClosedYear.Code.ShouldBe(409);
        inClosedYear.Message!.ShouldContain("fiscal year");
    }
}
