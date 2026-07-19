namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 未实现汇兑损益期末重估：delta-to-target 增量 / 幂等 no-op / 冲销重跑 /
/// 汇总凭证与净额方向 / 时序守卫 / 缺汇率与 inactive / TxnBalance 不变 / 关闭年度。
/// </summary>
public class RevaluationTests : FinanceIntegrationTestBase
{
    /// <summary>创建外币限定资产科目（叶子，可过账），返回 Id</summary>
    private async Task<Guid> CreateForeignAssetAsync(string code, string currency, string? name = null)
    {
        var parent = await AccountIdByCodeAsync("1000");
        return await CreateAccountAsync(new CreateAccountDto
        {
            Code = code,
            Name = name ?? $"{currency} Bank",
            RootType = AccountRootType.Asset,
            Currency = currency,
            ParentId = parent,
            SubType = "Bank",
            CashFlowActivity = CashFlowActivity.CashEquivalent
        });
    }

    /// <summary>过账一笔外币入账：Dr 外币科目 amount / Cr 3100 Owner's Equity amount @ rate</summary>
    private async Task SeedForeignBalanceAsync(Guid foreignAccount, string currency, decimal amount, decimal rate, DateTime date, string sourceId)
    {
        var result = await PostLedgerAsync(Posting(date, currency, rate, sourceId,
            new LedgerPostingLine { AccountId = foreignAccount, Debit = amount },
            new LedgerPostingLine { AccountCode = "3100", Credit = amount }));
        result.Succeeded.ShouldBeTrue(result.Message);
    }

    private Task<Result<RevaluationPreviewDto>> PreviewAsync(DateTime asOf, List<Guid>? accountIds = null)
        => InScopeAsync<IRevaluationService, Result<RevaluationPreviewDto>>(
            s => s.PreviewAsync(new RunRevaluationDto { AsOf = asOf, AccountIds = accountIds }));

    private Task<Result<RevaluationPreviewDto>> RunAsync(DateTime asOf, List<Guid>? accountIds = null)
        => InScopeAsync<IRevaluationService, Result<RevaluationPreviewDto>>(
            s => s.RunAsync(new RunRevaluationDto { AsOf = asOf, AccountIds = accountIds }));

    [Fact]
    public async Task Revaluation_DeltaToTarget_PostsOnlyTheIncrement()
    {
        await SeedCoaAsync();
        var eur = await CreateForeignAssetAsync("1121", "EUR");
        await UpsertRateAsync("EUR", "USD", 1.10m, new DateTime(2026, 1, 1));
        await SeedForeignBalanceAsync(eur, "EUR", 1000m, 1.10m, new DateTime(2026, 1, 15), "eur-in");

        // 第一次重估 @1.20：目标 1200，账面 1100 → 调整 +100
        await UpsertRateAsync("EUR", "USD", 1.20m, new DateTime(2026, 3, 31));
        var preview1 = await PreviewAsync(new DateTime(2026, 3, 31));
        preview1.Succeeded.ShouldBeTrue(preview1.Message);
        var row1 = preview1.Data!.Rows.Single(r => r.AccountId == eur);
        row1.TxnBalance.ShouldBe(1000m);
        row1.Rate.ShouldBe(1.20m);
        row1.TargetBase.ShouldBe(1200m);
        row1.BookBase.ShouldBe(1100m);
        row1.Adjustment.ShouldBe(100m);

        var run1 = await RunAsync(new DateTime(2026, 3, 31));
        run1.Succeeded.ShouldBeTrue(run1.Message);
        run1.Data!.JournalEntryId.ShouldNotBeNull();

        // 第二次重估 @1.25：账面已含上次 +100（=1200）→ 只出增量 +50，而非 +150
        await UpsertRateAsync("EUR", "USD", 1.25m, new DateTime(2026, 6, 30));
        var preview2 = await PreviewAsync(new DateTime(2026, 6, 30));
        var row2 = preview2.Data!.Rows.Single(r => r.AccountId == eur);
        row2.BookBase.ShouldBe(1200m);
        row2.TargetBase.ShouldBe(1250m);
        row2.Adjustment.ShouldBe(50m);

        var run2 = await RunAsync(new DateTime(2026, 6, 30));
        run2.Succeeded.ShouldBeTrue(run2.Message);
        run2.Data!.JournalEntryId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Revaluation_NoRateChange_IsIdempotentNoOp()
    {
        await SeedCoaAsync();
        var eur = await CreateForeignAssetAsync("1121", "EUR");
        await UpsertRateAsync("EUR", "USD", 1.20m, new DateTime(2026, 1, 1));
        await SeedForeignBalanceAsync(eur, "EUR", 500m, 1.20m, new DateTime(2026, 1, 15), "eur-in");

        // 账面已 = 600（500 × 1.20）；重估 @ 同汇率 → 调整 0，不出凭证
        var run1 = await RunAsync(new DateTime(2026, 3, 31));
        run1.Succeeded.ShouldBeTrue(run1.Message);
        run1.Data!.JournalEntryId.ShouldBeNull();
        run1.Data.TotalAdjustment.ShouldBe(0m);

        // 更晚日期同汇率重跑仍为 no-op（delta 收敛）
        var run2 = await RunAsync(new DateTime(2026, 4, 30));
        run2.Succeeded.ShouldBeTrue(run2.Message);
        run2.Data!.JournalEntryId.ShouldBeNull();
    }

    [Fact]
    public async Task Revaluation_ReverseThenRerun_AppliesCorrectedRate()
    {
        await SeedCoaAsync();
        var eur = await CreateForeignAssetAsync("1121", "EUR");
        await UpsertRateAsync("EUR", "USD", 1.10m, new DateTime(2026, 1, 1));
        await SeedForeignBalanceAsync(eur, "EUR", 1000m, 1.10m, new DateTime(2026, 1, 15), "eur-in");

        await UpsertRateAsync("EUR", "USD", 1.30m, new DateTime(2026, 3, 31));
        var run1 = await RunAsync(new DateTime(2026, 3, 31));
        run1.Succeeded.ShouldBeTrue(run1.Message);
        var voucherId = run1.Data!.JournalEntryId!.Value;

        // 修正汇率：先冲销原重估凭证（同日），账面回到 1100
        var reversed = await InScopeAsync<ILedgerPostingService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(voucherId, new ReverseJournalEntryDto { PostingDate = new DateTime(2026, 3, 31) }));
        reversed.Succeeded.ShouldBeTrue(reversed.Message);

        // 冲销凭证虽复制了 SourceType="Revaluation"，但是修正手段（ReversalOfEntryId 非空），不阻塞重跑
        await UpsertRateAsync("EUR", "USD", 1.15m, new DateTime(2026, 3, 31));
        var preview = await PreviewAsync(new DateTime(2026, 3, 31));
        var row = preview.Data!.Rows.Single(r => r.AccountId == eur);
        row.BookBase.ShouldBe(1100m); // 原重估 +200 与其冲销 -200 净额归零
        row.TargetBase.ShouldBe(1150m);
        row.Adjustment.ShouldBe(50m);

        var rerun = await RunAsync(new DateTime(2026, 3, 31));
        rerun.Succeeded.ShouldBeTrue(rerun.Message);
        rerun.Data!.JournalEntryId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Revaluation_SummaryVoucher_NetsGainAndLoss()
    {
        await SeedCoaAsync();
        var eur = await CreateForeignAssetAsync("1121", "EUR");
        var gbp = await CreateForeignAssetAsync("1122", "GBP");
        await UpsertRateAsync("EUR", "USD", 1.00m, new DateTime(2026, 1, 1));
        await UpsertRateAsync("GBP", "USD", 1.00m, new DateTime(2026, 1, 1));
        await SeedForeignBalanceAsync(eur, "EUR", 1000m, 1.00m, new DateTime(2026, 1, 15), "eur-in");
        await SeedForeignBalanceAsync(gbp, "GBP", 1000m, 1.00m, new DateTime(2026, 1, 16), "gbp-in");

        // EUR 升值（+200 gain），GBP 贬值（-50 loss）→ 净 +150
        await UpsertRateAsync("EUR", "USD", 1.20m, new DateTime(2026, 3, 31));
        await UpsertRateAsync("GBP", "USD", 0.95m, new DateTime(2026, 3, 31));

        var preview = await PreviewAsync(new DateTime(2026, 3, 31));
        preview.Data!.Rows.Single(r => r.AccountId == eur).Adjustment.ShouldBe(200m);
        preview.Data.Rows.Single(r => r.AccountId == gbp).Adjustment.ShouldBe(-50m);
        preview.Data.TotalAdjustment.ShouldBe(150m);

        var run = await RunAsync(new DateTime(2026, 3, 31));
        run.Succeeded.ShouldBeTrue(run.Message);

        // 汇总凭证：EUR 借 200、GBP 贷 50、净额 150 记汇兑损益（贷）；整体平衡
        var entry = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.GetAsync(run.Data!.JournalEntryId!.Value));
        entry.Succeeded.ShouldBeTrue(entry.Message);
        var fx = await AccountIdByCodeAsync("5800");
        entry.Data!.Lines.Single(l => l.AccountId == eur).Debit.ShouldBe(200m);
        entry.Data.Lines.Single(l => l.AccountId == gbp).Credit.ShouldBe(50m);
        entry.Data.Lines.Single(l => l.AccountId == fx).Credit.ShouldBe(150m);
        entry.Data.TotalDebit.ShouldBe(entry.Data.TotalCredit);
    }

    [Fact]
    public async Task Revaluation_TimingGuard_RejectsEarlierOrEqualAfterExisting()
    {
        await SeedCoaAsync();
        var eur = await CreateForeignAssetAsync("1121", "EUR");
        await UpsertRateAsync("EUR", "USD", 1.10m, new DateTime(2026, 1, 1));
        await SeedForeignBalanceAsync(eur, "EUR", 1000m, 1.10m, new DateTime(2026, 1, 15), "eur-in");
        await UpsertRateAsync("EUR", "USD", 1.20m, new DateTime(2026, 3, 31));

        var first = await RunAsync(new DateTime(2026, 3, 31));
        first.Succeeded.ShouldBeTrue(first.Message);

        // 同日重估（汇率再变）→ 被时序守卫拒（须先冲销原凭证）
        await UpsertRateAsync("EUR", "USD", 1.25m, new DateTime(2026, 3, 31));
        var sameDay = await RunAsync(new DateTime(2026, 3, 31));
        sameDay.Succeeded.ShouldBeFalse();
        sameDay.Code.ShouldBe(409);

        // 更早日期重估 → 同样被拒
        await UpsertRateAsync("EUR", "USD", 1.05m, new DateTime(2026, 2, 28));
        var earlier = await RunAsync(new DateTime(2026, 2, 28));
        earlier.Succeeded.ShouldBeFalse();
        earlier.Code.ShouldBe(409);

        // 更晚日期重估 → 放行（顺序推进）
        await UpsertRateAsync("EUR", "USD", 1.30m, new DateTime(2026, 6, 30));
        var later = await RunAsync(new DateTime(2026, 6, 30));
        later.Succeeded.ShouldBeTrue(later.Message);
    }

    [Fact]
    public async Task Revaluation_MissingRate_Fails_And_InactiveAccount_Skips()
    {
        await SeedCoaAsync();
        var eur = await CreateForeignAssetAsync("1121", "EUR");
        await UpsertRateAsync("EUR", "USD", 1.10m, new DateTime(2026, 1, 1));
        await SeedForeignBalanceAsync(eur, "EUR", 1000m, 1.10m, new DateTime(2026, 1, 15), "eur-in");

        // 另一个外币科目有余额，但其汇率将被删除以制造基准日缺失
        var chf = await CreateForeignAssetAsync("1124", "CHF");
        await UpsertRateAsync("CHF", "USD", 1.00m, new DateTime(2026, 1, 1));
        await SeedForeignBalanceAsync(chf, "CHF", 100m, 1.00m, new DateTime(2026, 1, 17), "chf-in");

        // 删除 CHF 汇率使其在基准日无解析
        var rateRepo = ServiceProvider.GetRequiredService<IRepository<ExchangeRate, Guid>>();
        var chfRate = await rateRepo.FirstOrDefaultAsync(r => r.FromCurrency == "CHF");
        chfRate.ShouldNotBeNull();
        await rateRepo.DeleteAsync(chfRate);
        await rateRepo.SaveChangesAsync();

        var missing = await PreviewAsync(new DateTime(2026, 3, 31));
        missing.Succeeded.ShouldBeFalse();
        missing.Code.ShouldBe(400);
        missing.Message.ShouldNotBeNull();
        missing.Message.ShouldContain("CHF");

        // 停用 CHF 科目后，它以 SkipReason 出现而不再阻塞整单
        var chfAccount = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.GetAsync(chf));
        await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.UpdateAsync(chf, new UpdateAccountDto
        {
            Code = "1124",
            Name = chfAccount.Data!.Name,
            Currency = "CHF",
            SubType = "Bank",
            CashFlowActivity = CashFlowActivity.CashEquivalent,
            IsActive = false
        }));

        var preview = await PreviewAsync(new DateTime(2026, 3, 31));
        preview.Succeeded.ShouldBeTrue(preview.Message);
        var chfRow = preview.Data!.Rows.Single(r => r.AccountId == chf);
        chfRow.SkipReason.ShouldNotBeNull();
        chfRow.Adjustment.ShouldBe(0m);
    }

    [Fact]
    public async Task Revaluation_LeavesTransactionCurrencyBalanceUnchanged()
    {
        await SeedCoaAsync();
        var eur = await CreateForeignAssetAsync("1121", "EUR");
        await UpsertRateAsync("EUR", "USD", 1.10m, new DateTime(2026, 1, 1));
        await SeedForeignBalanceAsync(eur, "EUR", 1000m, 1.10m, new DateTime(2026, 1, 15), "eur-in");
        await UpsertRateAsync("EUR", "USD", 1.40m, new DateTime(2026, 3, 31));

        var run = await RunAsync(new DateTime(2026, 3, 31));
        run.Succeeded.ShouldBeTrue(run.Message);

        // 重估后交易币余额不变（本位币调整行 Currency=USD，不计入 EUR 交易币余额）
        var after = await PreviewAsync(new DateTime(2026, 3, 31));
        // 已重估到目标 → 再预览同日增量为 0，但 TxnBalance 仍是 1000
        after.Data!.Rows.Any(r => r.AccountId == eur && r.TxnBalance == 1000m).ShouldBeTrue();
    }

    [Fact]
    public async Task Revaluation_IntoClosedFiscalYear_Fails()
    {
        await SeedCoaAsync();
        var eur = await CreateForeignAssetAsync("1121", "EUR");
        await UpsertRateAsync("EUR", "USD", 1.10m, new DateTime(2026, 1, 1));
        await SeedForeignBalanceAsync(eur, "EUR", 1000m, 1.10m, new DateTime(2026, 1, 15), "eur-in");
        await UpsertRateAsync("EUR", "USD", 1.20m, new DateTime(2026, 3, 31));

        var fy = await InScopeAsync<IFiscalYearService, Result<FiscalYearDto>>(s => s.CreateAsync(new CreateFiscalYearDto
        {
            Name = "FY2026",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31)
        }));
        fy.Succeeded.ShouldBeTrue(fy.Message);
        (await InScopeAsync<IFiscalYearService, Result>(s => s.CloseAsync(fy.Data!.Id))).Succeeded.ShouldBeTrue();

        var run = await RunAsync(new DateTime(2026, 3, 31));
        run.Succeeded.ShouldBeFalse();
    }
}
