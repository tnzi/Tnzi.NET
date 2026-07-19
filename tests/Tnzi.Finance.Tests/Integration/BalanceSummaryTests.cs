using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 科目期间余额汇总（批次 F）：维护正确性（双口径/累加/冲销归零/舍入/倒填）、原子性、
/// 重建/校验、以及报表在开关两态下的深度等价（含残月边界矩阵）。
/// </summary>
public class BalanceSummaryTests : FinanceIntegrationTestBase
{
    // ---- helpers ----

    private async Task<List<AccountPeriodBalance>> LoadBucketsAsync()
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<AccountPeriodBalance, Guid>>();
        return await repo.AsNoTracking()
            .OrderBy(b => b.AccountId).ThenBy(b => b.Period).ThenBy(b => b.Currency)
            .ToListAsync();
    }

    private async Task InsertBucketAsync(AccountPeriodBalance bucket)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<AccountPeriodBalance, Guid>>();
        await repo.InsertAsync(bucket);
        await repo.SaveChangesAsync();
    }

    private async Task TamperBucketAsync(Guid accountId, int period, string currency, decimal debitDelta)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<AccountPeriodBalance, Guid>>();
        var bucket = await repo.FirstOrDefaultAsync(b => b.AccountId == accountId && b.Period == period && b.Currency == currency);
        bucket.ShouldNotBeNull();
        bucket.Debit += debitDelta;
        await repo.UpdateAsync(bucket);
        await repo.SaveChangesAsync();
    }

    private async Task<BalanceSummaryVerifyDto> VerifyAsync()
    {
        var result = await InScopeAsync<IBalanceSummaryService, Result<BalanceSummaryVerifyDto>>(s => s.VerifyAsync());
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!;
    }

    private async Task<BalanceSummaryRebuildDto> RebuildAsync()
    {
        var result = await InScopeAsync<IBalanceSummaryService, Result<BalanceSummaryRebuildDto>>(s => s.RebuildAsync());
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!;
    }

    private static (decimal Debit, decimal Credit, decimal TxnDebit, decimal TxnCredit, int LineCount) Totals(AccountPeriodBalance b)
        => (b.Debit, b.Credit, b.TxnDebit, b.TxnCredit, b.LineCount);

    /// <summary>富 fixture：多币种 + 冲销 + 舍入 + 倒填 + 跨月，返回被冲销的费用凭证 Id</summary>
    private async Task SeedRichFixtureAsync()
    {
        await SeedCoaAsync();
        await UpsertRateAsync("EUR", "USD", 1.20m, new DateTime(2026, 2, 1));
        await UpsertRateAsync("EUR", "USD", 1.115m, new DateTime(2026, 3, 1));

        // Jan 10: USD sale 1000 (Dr 1200 AR / Cr 4100 income)
        (await PostLedgerAsync(SimpleSale(1000m, new DateTime(2026, 1, 10), "s1"))).Succeeded.ShouldBeTrue();

        // Feb 15: USD expense 300 (Dr 5200 / Cr 1120) — reversed below
        var exp = await PostLedgerAsync(new LedgerPostingRequest
        {
            PostingDate = new DateTime(2026, 2, 15),
            SourceType = "Test",
            SourceId = "exp",
            Lines =
            [
                new LedgerPostingLine { AccountCode = "5200", Debit = 300m },
                new LedgerPostingLine { AccountCode = "1120", Credit = 300m }
            ]
        });
        exp.Succeeded.ShouldBeTrue(exp.Message);

        // Feb 20: EUR sale 500 @1.20 (Dr 1120 / Cr 4100) — multi-currency, cross-month
        (await PostLedgerAsync(Posting(new DateTime(2026, 2, 20), "EUR", 1.20m, "eur1",
            new LedgerPostingLine { AccountCode = "1120", Debit = 500m },
            new LedgerPostingLine { AccountCode = "4100", Credit = 500m }))).Succeeded.ShouldBeTrue();

        // Mar 5: USD fixed asset purchase 200 (Dr 1500 Investing / Cr 1120)
        (await PostLedgerAsync(new LedgerPostingRequest
        {
            PostingDate = new DateTime(2026, 3, 5),
            SourceType = "Test",
            SourceId = "fa",
            Lines =
            [
                new LedgerPostingLine { AccountCode = "1500", Debit = 200m },
                new LedgerPostingLine { AccountCode = "1120", Credit = 200m }
            ]
        })).Succeeded.ShouldBeTrue();

        // Mar 10: EUR entry triggering a rounding line (residual 0.01 base @1.115)
        (await PostLedgerAsync(new LedgerPostingRequest
        {
            PostingDate = new DateTime(2026, 3, 10),
            Currency = "EUR",
            ExchangeRate = 1.115m,
            SourceType = "Test",
            SourceId = "rnd",
            Lines =
            [
                new LedgerPostingLine { AccountCode = "1120", Debit = 10.00m },
                new LedgerPostingLine { AccountCode = "4100", Credit = 3.33m },
                new LedgerPostingLine { AccountCode = "4100", Credit = 3.33m },
                new LedgerPostingLine { AccountCode = "4100", Credit = 3.34m }
            ]
        })).Succeeded.ShouldBeTrue();

        // Reverse the Feb expense (same-date reversal → nets in the Feb bucket)
        (await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(exp.Data!.Id, new ReverseJournalEntryDto()))).Succeeded.ShouldBeTrue();

        // Back-dated: a Jan 25 entry posted AFTER the March entries (lands in the Jan bucket)
        (await PostLedgerAsync(new LedgerPostingRequest
        {
            PostingDate = new DateTime(2026, 1, 25),
            SourceType = "Test",
            SourceId = "bd",
            Lines =
            [
                new LedgerPostingLine { AccountCode = "5200", Debit = 75m },
                new LedgerPostingLine { AccountCode = "1120", Credit = 75m }
            ]
        })).Succeeded.ShouldBeTrue();
    }

    private async Task<Result<T>> ReportAsync<T>(bool useSummary, Func<IFinancialReportService, Task<Result<T>>> call)
    {
        UseBalanceSummaryOption = useSummary;
        return await InScopeAsync<IFinancialReportService, Result<T>>(call);
    }

    // 汇总桶为 decimal(19,4)，读回带尾零（"336.1500"）；明细求和为 scale 2（"336.15"）——
    // 数值相等但 JSON 文本不同。归一化 decimal 序列化（去尾零）后做文本深度对比。
    private static readonly JsonSerializerOptions NormalizedJson = new() { Converters = { new ScaleInsensitiveDecimalConverter() } };

    /// <summary>同一数据、开关两态下报表 DTO 深度相等（归一化 JSON 对比）</summary>
    private async Task AssertReportEquivalentAsync<T>(Func<IFinancialReportService, Task<Result<T>>> call)
    {
        var detail = await ReportAsync(false, call);
        var summary = await ReportAsync(true, call);
        detail.Succeeded.ShouldBeTrue(detail.Message);
        summary.Succeeded.ShouldBeTrue(summary.Message);
        JsonSerializer.Serialize(summary.Data, NormalizedJson).ShouldBe(JsonSerializer.Serialize(detail.Data, NormalizedJson));
    }

    private sealed class ScaleInsensitiveDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
            => writer.WriteRawValue(value.ToString("0.############################", CultureInfo.InvariantCulture));
    }

    // ---- 维护正确性 ----

    [Fact]
    public async Task Post_ForeignCurrency_MaintainsDualBasisBucket()
    {
        await SeedCoaAsync();
        await UpsertRateAsync("EUR", "USD", 1.20m, new DateTime(2026, 2, 1));

        (await PostLedgerAsync(Posting(new DateTime(2026, 2, 10), "EUR", 1.20m, "fx",
            new LedgerPostingLine { AccountCode = "1120", Debit = 100m },
            new LedgerPostingLine { AccountCode = "4100", Credit = 100m }))).Succeeded.ShouldBeTrue();

        var bank = await AccountIdByCodeAsync("1120");
        var buckets = await LoadBucketsAsync();

        var bankBucket = buckets.Single(b => b.AccountId == bank);
        bankBucket.Period.ShouldBe(202602);
        bankBucket.Currency.ShouldBe("EUR");
        Totals(bankBucket).ShouldBe((120m, 0m, 100m, 0m, 1)); // base 100*1.2 = 120, txn 100
    }

    [Fact]
    public async Task Post_MultipleEntriesSameMonth_AccumulateIntoOneBucket()
    {
        await SeedCoaAsync();
        (await PostLedgerAsync(SimpleSale(1000m, new DateTime(2026, 1, 10), "a"))).Succeeded.ShouldBeTrue();
        (await PostLedgerAsync(SimpleSale(2000m, new DateTime(2026, 1, 20), "b"))).Succeeded.ShouldBeTrue();

        var ar = await AccountIdByCodeAsync("1200");
        var buckets = await LoadBucketsAsync();

        var arBuckets = buckets.Where(b => b.AccountId == ar).ToList();
        arBuckets.Count.ShouldBe(1); // 同科目同月同币种 → 单桶
        Totals(arBuckets[0]).ShouldBe((3000m, 0m, 3000m, 0m, 2));
    }

    [Fact]
    public async Task Post_BackDated_LandsInHistoricalPeriodBucket()
    {
        await SeedCoaAsync();
        (await PostLedgerAsync(SimpleSale(500m, new DateTime(2026, 3, 5), "mar"))).Succeeded.ShouldBeTrue();
        // 倒填：晚于 3 月凭证过账，但日期在 1 月
        (await PostLedgerAsync(SimpleSale(700m, new DateTime(2026, 1, 5), "jan"))).Succeeded.ShouldBeTrue();

        var ar = await AccountIdByCodeAsync("1200");
        var buckets = await LoadBucketsAsync();

        var janBucket = buckets.Single(b => b.AccountId == ar && b.Period == 202601);
        janBucket.Debit.ShouldBe(700m);
        buckets.Single(b => b.AccountId == ar && b.Period == 202603).Debit.ShouldBe(500m);
    }

    [Fact]
    public async Task Reversal_AccumulatesGross_NetsToZero()
    {
        await SeedCoaAsync();
        var sale = await PostLedgerAsync(SimpleSale(400m, new DateTime(2026, 4, 10), "rev"));
        sale.Succeeded.ShouldBeTrue();

        (await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(sale.Data!.Id, new ReverseJournalEntryDto()))).Succeeded.ShouldBeTrue();

        var ar = await AccountIdByCodeAsync("1200");
        var arBucket = (await LoadBucketsAsync()).Single(b => b.AccountId == ar);
        // 冲销毛额累加：借 400（原）+ 贷 400（冲销），净额 0，两行
        arBucket.Debit.ShouldBe(400m);
        arBucket.Credit.ShouldBe(400m);
        (arBucket.Debit - arBucket.Credit).ShouldBe(0m);
        arBucket.LineCount.ShouldBe(2);
    }

    [Fact]
    public async Task Post_WithRoundingLine_CountsRoundingBucket()
    {
        await SeedCoaAsync();
        await UpsertRateAsync("EUR", "USD", 1.115m, new DateTime(2026, 3, 1));

        (await PostLedgerAsync(new LedgerPostingRequest
        {
            PostingDate = new DateTime(2026, 3, 10),
            Currency = "EUR",
            ExchangeRate = 1.115m,
            SourceType = "Test",
            SourceId = "rnd",
            Lines =
            [
                new LedgerPostingLine { AccountCode = "1120", Debit = 10.00m },
                new LedgerPostingLine { AccountCode = "4100", Credit = 3.33m },
                new LedgerPostingLine { AccountCode = "4100", Credit = 3.33m },
                new LedgerPostingLine { AccountCode = "4100", Credit = 3.34m }
            ]
        })).Succeeded.ShouldBeTrue();

        var rounding = await AccountIdByCodeAsync("5900");
        var bucket = (await LoadBucketsAsync()).Single(b => b.AccountId == rounding);
        bucket.Currency.ShouldBe("EUR");
        (bucket.Debit + bucket.Credit).ShouldBe(0.01m); // residual 0.01 absorbed
        bucket.TxnDebit.ShouldBe(0m);
        bucket.TxnCredit.ShouldBe(0m);
        bucket.LineCount.ShouldBe(1);
    }

    // ---- 原子性 ----

    [Fact]
    public async Task FailedPosting_LeavesNoBucketResidue()
    {
        await SeedCoaAsync();

        // 借贷不平（交易币 100 vs 50）→ 引擎在提交点前拒绝，维护器不运行
        var result = await PostLedgerAsync(new LedgerPostingRequest
        {
            PostingDate = new DateTime(2026, 1, 10),
            SourceType = "Test",
            SourceId = "bad",
            Lines =
            [
                new LedgerPostingLine { AccountCode = "1200", Debit = 100m },
                new LedgerPostingLine { AccountCode = "4100", Credit = 50m }
            ]
        });
        result.Succeeded.ShouldBeFalse();

        (await LoadBucketsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task AbortedRevaluation_RollsBackBucketIncrement()
    {
        await SeedCoaAsync();
        var parent = await AccountIdByCodeAsync("1000");
        var eur = await CreateAccountAsync(new CreateAccountDto
        {
            Code = "1121",
            Name = "EUR Bank",
            RootType = AccountRootType.Asset,
            Currency = "EUR",
            ParentId = parent,
            SubType = "Bank",
            CashFlowActivity = CashFlowActivity.CashEquivalent
        });
        await UpsertRateAsync("EUR", "USD", 1.10m, new DateTime(2026, 1, 1));
        (await PostLedgerAsync(Posting(new DateTime(2026, 1, 15), "EUR", 1.10m, "eur-in",
            new LedgerPostingLine { AccountId = eur, Debit = 1000m },
            new LedgerPostingLine { AccountCode = "3100", Credit = 1000m }))).Succeeded.ShouldBeTrue();

        // 有效重估 @1.20（Mar 31）：账面 1100 → 目标 1200，调整 +100
        await UpsertRateAsync("EUR", "USD", 1.20m, new DateTime(2026, 3, 31));
        (await InScopeAsync<IRevaluationService, Result<RevaluationPreviewDto>>(
            s => s.RunAsync(new RunRevaluationDto { AsOf = new DateTime(2026, 3, 31) }))).Succeeded.ShouldBeTrue();

        var before = await LoadBucketsAsync();

        // 倒填重估 @1.15（Feb 28）：引擎过账（维护器已累加）后时序守卫发现 Mar 重估 → 409 整体回滚
        await UpsertRateAsync("EUR", "USD", 1.15m, new DateTime(2026, 2, 28));
        var conflict = await InScopeAsync<IRevaluationService, Result<RevaluationPreviewDto>>(
            s => s.RunAsync(new RunRevaluationDto { AsOf = new DateTime(2026, 2, 28) }));
        conflict.Succeeded.ShouldBeFalse();
        conflict.Code.ShouldBe(409);

        var after = await LoadBucketsAsync();
        // 桶集合不变：失败重估的桶增量随 UoW 回滚
        JsonSerializer.Serialize(after.Select(Totals), NormalizedJson)
            .ShouldBe(JsonSerializer.Serialize(before.Select(Totals), NormalizedJson));
        after.Count.ShouldBe(before.Count);
    }

    // ---- 重建 / 校验 ----

    [Fact]
    public async Task Rebuild_EmptyLedger_ProducesNoBuckets()
    {
        await SeedCoaAsync();
        var result = await RebuildAsync();
        result.Buckets.ShouldBe(0);
        result.Lines.ShouldBe(0);
        (await LoadBucketsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Rebuild_MatchesIncremental_AndIsIdempotent()
    {
        await SeedRichFixtureAsync();

        var incremental = (await LoadBucketsAsync()).Select(Totals).ToList();
        (await VerifyAsync()).IsConsistent.ShouldBeTrue(); // incremental maintenance already correct

        var rebuild1 = await RebuildAsync();
        rebuild1.Buckets.ShouldBe(incremental.Count);
        var afterRebuild = (await LoadBucketsAsync()).Select(Totals).ToList();
        JsonSerializer.Serialize(afterRebuild, NormalizedJson).ShouldBe(JsonSerializer.Serialize(incremental, NormalizedJson));

        // 幂等：再次重建结果不变
        var rebuild2 = await RebuildAsync();
        rebuild2.Buckets.ShouldBe(rebuild1.Buckets);
        (await VerifyAsync()).IsConsistent.ShouldBeTrue();
    }

    [Fact]
    public async Task Rebuild_RepairsTamperedBuckets()
    {
        await SeedRichFixtureAsync();
        var ar = await AccountIdByCodeAsync("1200");
        await TamperBucketAsync(ar, 202601, "USD", debitDelta: 999m);

        (await VerifyAsync()).IsConsistent.ShouldBeFalse();

        await RebuildAsync();
        (await VerifyAsync()).IsConsistent.ShouldBeTrue();
    }

    [Fact]
    public async Task Verify_IsConsistent_AfterIncrementalMaintenance()
    {
        await SeedRichFixtureAsync();
        var verify = await VerifyAsync();
        verify.IsConsistent.ShouldBeTrue();
        verify.TotalDifferences.ShouldBe(0);
        verify.CheckedBuckets.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Verify_DetectsTamperedBucket_AsMismatch()
    {
        await SeedRichFixtureAsync();
        var ar = await AccountIdByCodeAsync("1200");
        await TamperBucketAsync(ar, 202601, "USD", debitDelta: 5m);

        var verify = await VerifyAsync();
        verify.IsConsistent.ShouldBeFalse();
        verify.TotalDifferences.ShouldBe(1);
        var diff = verify.Differences.Single();
        diff.Kind.ShouldBe(BalanceSummaryDifferenceKind.Mismatch);
        diff.AccountId.ShouldBe(ar);
        diff.Period.ShouldBe(202601);
        (diff.StoredDebit - diff.ExpectedDebit).ShouldBe(5m);
    }

    [Fact]
    public async Task Verify_DetectsExtraBuckets_AndTruncatesTo100()
    {
        await SeedRichFixtureAsync();
        var bank = await AccountIdByCodeAsync("1120");

        // 注入 150 个总账无对应明细的伪桶（Period 取远期避免与真实桶撞键）
        for (var i = 0; i < 150; i++)
        {
            await InsertBucketAsync(new AccountPeriodBalance
            {
                AccountId = bank,
                Period = 990001 + i,
                Currency = "USD",
                Debit = 1m,
                Credit = 0m,
                LineCount = 1
            });
        }

        var verify = await VerifyAsync();
        verify.IsConsistent.ShouldBeFalse();
        verify.TotalDifferences.ShouldBe(150);
        verify.Differences.Count.ShouldBe(100); // 截断
        verify.Differences.ShouldAllBe(d => d.Kind == BalanceSummaryDifferenceKind.Extra);
    }

    // ---- 报表等价（开关两态深度相等）+ 残月边界矩阵 ----

    [Fact]
    public async Task TrialBalance_SummaryMatchesDetail_FullYear()
    {
        await SeedRichFixtureAsync();
        await AssertReportEquivalentAsync(s => s.GetTrialBalanceAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
    }

    [Fact]
    public async Task BalanceSheet_SummaryMatchesDetail_AsOf()
    {
        await SeedRichFixtureAsync();
        await AssertReportEquivalentAsync(s => s.GetBalanceSheetAsync(new DateTime(2026, 3, 15)));
    }

    [Fact]
    public async Task CashFlow_SummaryMatchesDetail()
    {
        await SeedRichFixtureAsync();
        await AssertReportEquivalentAsync(s => s.GetCashFlowAsync(new DateTime(2026, 1, 1), new DateTime(2026, 3, 31)));
    }

    [Fact]
    public async Task GeneralLedger_SummaryMatchesDetail_HeaderAndCsv()
    {
        await SeedRichFixtureAsync();
        var bank = await AccountIdByCodeAsync("1120");

        await AssertReportEquivalentAsync(s => s.GetGeneralLedgerAsync(
            bank, new DateTime(2026, 1, 15), new DateTime(2026, 3, 20), new PagedQueryDto()));

        // CSV 期初余额来自读路径；两态内容一致（数值 token 去尾零后对比，规避 SQLite decimal 尾零差异）
        var csvDetail = await ReportAsync(false, s => s.ExportGeneralLedgerCsvAsync(bank, new DateTime(2026, 1, 15), new DateTime(2026, 3, 20)));
        var csvSummary = await ReportAsync(true, s => s.ExportGeneralLedgerCsvAsync(bank, new DateTime(2026, 1, 15), new DateTime(2026, 3, 20)));
        csvDetail.Succeeded.ShouldBeTrue(csvDetail.Message);
        csvSummary.Succeeded.ShouldBeTrue(csvSummary.Message);
        NormalizeCsvNumbers(csvSummary.Data!).ShouldBe(NormalizeCsvNumbers(csvDetail.Data!));
    }

    private static string NormalizeCsvNumbers(string csv)
        => System.Text.RegularExpressions.Regex.Replace(csv, @"-?\d+\.\d+",
            m => decimal.Parse(m.Value, CultureInfo.InvariantCulture).ToString("0.############################", CultureInfo.InvariantCulture));

    [Theory]
    // 残月边界矩阵：月初→月初（纯汇总）/ 月中→月中跨月（头+汇总+尾）/ 同月月中（纯明细单段）/
    // 单整月 / 月初→月末 / 空区间（无活动）
    [InlineData("2026-01-01", "2026-03-31")] // 季度：整月边界
    [InlineData("2026-01-15", "2026-03-20")] // 头残月 + 整月 + 尾残月
    [InlineData("2026-02-10", "2026-02-25")] // 同月非月初 → 纯明细单段
    [InlineData("2026-02-01", "2026-02-28")] // 单整月（纯汇总）
    [InlineData("2026-03-01", "2026-03-31")] // 月初→月末（含舍入 + Investing）
    [InlineData("2025-12-01", "2025-12-31")] // 空区间（无活动）
    public async Task ProfitAndLoss_SummaryMatchesDetail_AcrossMonthBoundaries(string from, string to)
    {
        await SeedRichFixtureAsync();
        var fromDate = DateTime.Parse(from, CultureInfo.InvariantCulture);
        var toDate = DateTime.Parse(to, CultureInfo.InvariantCulture);
        await AssertReportEquivalentAsync(s => s.GetProfitAndLossAsync(fromDate, toDate));
    }
}
