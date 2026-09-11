namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// <see cref="IGeneralLedgerSearchContributor"/> 的契约面：贡献者能命中**任意**来源类型，
/// 而不只是付款单；且命中是 <c>(SourceType, SourceId)</c> <b>成对</b>生效的。
/// </summary>
/// <remarks>
/// 这个扩展点的设计用途就是"消费应用把自己的单据号解析成一条来源命中"（工单号、案件号、
/// 合同号……）。只认付款单等于把契约承诺的能力静默丢掉：搜索返回 0 条，不报错也不记日志。
/// <br/><br/>
/// 第二条用例（同一个 SourceId 挂在两个来源类型下）是**成对**这件事的证据：把实现退化成
/// "只按 SourceId 匹配"照样能让第一条用例通过，但会把另一个来源类型的行一起捞出来。
/// </remarks>
public class GeneralLedgerCustomSourceSearchTests : FinanceIntegrationTestBase
{
    private const string Bank = "1120";
    private const string Income = "4100";

    private const string Alpha = "Test.Alpha";
    private const string Beta = "Test.Beta";

    private static readonly DateTime PeriodFrom = new(2026, 3, 1);
    private static readonly DateTime PeriodTo = new(2026, 3, 31);

    /// <summary>
    /// 消费应用那一侧的贡献者：把"人能记住的东西"（此处是工单号）解析成来源命中。
    /// 字段初始化器先于基类构造函数体运行，故 <see cref="ConfigureServices"/> 回调时它已就绪
    /// </summary>
    private readonly StubSearchContributor _contributor = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // 与基类已注册的 CheckNumberSearchContributor 并存：贡献者是 IEnumerable 注入、结果取并集
        services.AddSingleton<IGeneralLedgerSearchContributor>(_contributor);
    }

    private Task<Result<JournalEntryDto>> PostManualAsync(string sourceType, string sourceId, decimal amount, DateTime date)
        => PostLedgerAsync(new LedgerPostingRequest
        {
            PostingDate = date,
            SourceType = sourceType,
            SourceId = sourceId,
            Lines =
            [
                new LedgerPostingLine { AccountCode = Bank, Debit = amount },
                new LedgerPostingLine { AccountCode = Income, Credit = amount }
            ]
        });

    /// <summary>
    /// 期内两行，**共用同一个 SourceId** 但来源类型不同 —— 唯一的区分点就是 SourceType。
    /// 摘要一律留空，确保命中只可能来自贡献者而不是文本匹配
    /// </summary>
    private async Task<Guid> SeedAsync()
    {
        await SeedCoaAsync();

        (await PostManualAsync(Alpha, "WO-7001", 100m, new DateTime(2026, 3, 1))).Succeeded.ShouldBeTrue();
        (await PostManualAsync(Beta, "WO-7001", 200m, new DateTime(2026, 3, 2))).Succeeded.ShouldBeTrue();

        return await AccountIdByCodeAsync(Bank);
    }

    private async Task<GeneralLedgerReportDto> ByKeywordAsync(Guid accountId, string keyword)
    {
        var result = await InScopeAsync<IFinancialReportService, Result<GeneralLedgerReportDto>>(
            s => s.GetGeneralLedgerAsync(accountId, PeriodFrom, PeriodTo,
                new PagedQueryDto { PageIndex = 1, PageSize = 50 }, new GeneralLedgerFilterDto { Keyword = keyword }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!;
    }

    [Fact]
    public async Task Contributor_CanMatchANonPaymentSourceType()
    {
        var bank = await SeedAsync();
        _contributor.Add("wo-7001", new GeneralLedgerSourceMatch(Alpha, "WO-7001"));

        // 关键字不在任何摘要/凭证号/付款参考号里：命中只可能来自贡献者
        var report = await ByKeywordAsync(bank, "WO-7001");

        report.Lines.TotalCount.ShouldBe(1);
        var line = report.Lines.Items.Single();
        line.SourceType.ShouldBe(Alpha);
        line.SourceId.ShouldBe("WO-7001");
        line.Debit.ShouldBe(100m);
    }

    [Fact]
    public async Task Contributor_MatchesArePairedWithSourceType_NotIdAlone()
    {
        var bank = await SeedAsync();
        // 只贡献 Alpha 那一条；Beta 那一行的 SourceId 逐字相同，但类型不同 —— 不该被带出来
        _contributor.Add("wo-7001", new GeneralLedgerSourceMatch(Alpha, "WO-7001"));

        var report = await ByKeywordAsync(bank, "WO-7001");

        report.Lines.TotalCount.ShouldBe(1);
        report.Lines.Items.Single().SourceType.ShouldBe(Alpha);
        report.Lines.Items.ShouldNotContain(l => l.SourceType == Beta);
    }

    [Fact]
    public async Task Contributor_MatchesFromSeveralSourceTypes_AreUnioned()
    {
        var bank = await SeedAsync();
        _contributor.Add("wo-7001",
            new GeneralLedgerSourceMatch(Alpha, "WO-7001"),
            new GeneralLedgerSourceMatch(Beta, "WO-7001"));

        var report = await ByKeywordAsync(bank, "WO-7001");

        report.Lines.TotalCount.ShouldBe(2);
        report.Lines.Items.Select(l => l.SourceType).ShouldBe([Alpha, Beta]);
    }

    [Fact]
    public async Task Contributor_ReturningNothing_LeavesTheSearchAsItWas()
    {
        var bank = await SeedAsync();

        var report = await ByKeywordAsync(bank, "WO-7001");

        report.Lines.TotalCount.ShouldBe(0);
        report.IsFiltered.ShouldBeTrue();
    }

    /// <summary>关键字 → 来源命中的固定映射，模拟消费应用自己的单据号解析</summary>
    private sealed class StubSearchContributor : IGeneralLedgerSearchContributor
    {
        private readonly Dictionary<string, List<GeneralLedgerSourceMatch>> _matches = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string keyword, params GeneralLedgerSourceMatch[] matches)
        {
            if (!_matches.TryGetValue(keyword, out var list))
                _matches[keyword] = list = [];
            list.AddRange(matches);
        }

        public Task<IReadOnlyList<GeneralLedgerSourceMatch>> MatchAsync(string keyword, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GeneralLedgerSourceMatch>>(
                _matches.TryGetValue(keyword, out var list) ? list : []);
    }
}
