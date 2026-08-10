namespace Tnzi.Finance.Tax.Ca.Tests;

/// <summary>
/// CRA GST34 行映射的行为锁。
/// </summary>
/// <remarks>
/// <para>
/// 这个模块此前<b>零测试覆盖</b>（<c>Gst34</c> / <c>ITaxReturnMapper</c> 在整个 tests 树里
/// 一次都没出现过）。它只有 121 行、是纯函数逻辑、没有 I/O —— 但它决定报给税务机关的数字，
/// 且它的每一条设计取舍<b>坏掉时都是静默的</b>：
/// </para>
/// <list type="bullet">
/// <item>101 行取自利润表而非税务汇总（两者口径不同：汇总聚合的是税额不是税基）。
/// 改成从汇总取，101 会变成一个看起来合理的错数。</item>
/// <item>利润表取不到时整张表失败，不拿 0 顶上。一张 101=0.00 而 105/108/109 都非零的
/// GST34 看起来是完整的，操作员没有理由怀疑它。</item>
/// <item>未归入的销项与进项<b>分两行</b>报、不做净额轧差。机关名配错时两侧通常一起漏，
/// 轧差后恰好抵消成 0，提示行就不出现了 —— 而那正是它存在的唯一场景。</item>
/// <item>省级销售税（PST/QST/RST）不进 GST34，且这条排除<b>优先于</b> GST/HST 关键字匹配。</item>
/// <item>关键字必须整词命中，藏在别的词里不算 —— 两个方向各有一个真实误判：
/// FIRST 含 RST（First Nations GST 会被当省税排除、销项税从表上消失），
/// SACRAMENTO 含 CRA（无关税种被收进 105 行，且多出来的钱不会出现在任何诊断行上）。</item>
/// </list>
/// <para>
/// 每条测试锁的都是上面某一条「坏了也不会有人发现」的判断。
/// </para>
/// </remarks>
public class CraGstHstReturnMapperTests
{
    private static readonly DateTime From = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Mapper_identifies_itself_as_the_canadian_gst34_form()
    {
        var mapper = CreateMapper(out _);

        mapper.CountryCode.ShouldBe("CA");
        mapper.FormCode.ShouldBe("GST34");
    }

    [Fact]
    public async Task Period_that_ends_before_it_starts_is_rejected()
    {
        var mapper = CreateMapper(out _);

        var result = await mapper.MapAsync(To, From);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    // ── 机关名匹配 ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("CRA")]
    [InlineData("Canada Revenue Agency")]
    [InlineData("GST/HST")]
    [InlineData("Ontario HST")]
    // 小写：匹配必须大小写不敏感，否则部署按自己习惯命名机关就会整片漏掉
    [InlineData("cra")]
    [InlineData("canada revenue")]
    public async Task Gst_hst_agencies_are_counted_into_lines_105_and_108(string agencyName)
    {
        var mapper = CreateMapper(out var reports);
        StubReports(reports, revenue: 1000m, rows: [Row(agencyName, output: 130m, input: 30m)]);

        var lines = (await mapper.MapAsync(From, To)).Data!.Lines;

        Amount(lines, "105").ShouldBe(130m);
        Amount(lines, "108").ShouldBe(30m);
    }

    [Theory]
    [InlineData("BC PST")]
    [InlineData("Revenu Québec QST")]
    [InlineData("Manitoba RST")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Some Unrelated Levy")]
    public async Task Non_gst_hst_agencies_are_kept_out_of_lines_105_and_108(string? agencyName)
    {
        var mapper = CreateMapper(out var reports);
        StubReports(reports, revenue: 1000m, rows: [Row(agencyName, output: 130m, input: 30m)]);

        var lines = (await mapper.MapAsync(From, To)).Data!.Lines;

        Amount(lines, "105").ShouldBe(0m);
        Amount(lines, "108").ShouldBe(0m);
    }

    /// <summary>
    /// 省税排除<b>优先于</b> GST/HST 关键字：名字里两者都有时按省税处理（排除）。
    /// </summary>
    /// <remarks>
    /// "GST/PST Combined" 这类合并科目名在实务里很常见。若关键字匹配先命中 GST 就收进来，
    /// 105 会混入不属于 GST34 的省税，报出去的是一个偏大的应缴数。
    /// </remarks>
    [Theory]
    [InlineData("GST/PST Combined")]
    [InlineData("GST and QST")]
    [InlineData("CRA + RST pooled account")]
    public async Task Provincial_sales_tax_exclusion_wins_over_the_gst_keyword(string agencyName)
    {
        var mapper = CreateMapper(out var reports);
        StubReports(reports, revenue: 1000m, rows: [Row(agencyName, output: 130m, input: 30m)]);

        var lines = (await mapper.MapAsync(From, To)).Data!.Lines;

        Amount(lines, "105").ShouldBe(0m);
        Amount(lines, "108").ShouldBe(0m);
    }

    /// <summary>
    /// 省税代码只有作为<b>独立的词</b>出现时才排除，藏在别的词里不算。
    /// </summary>
    /// <remarks>
    /// <c>"First Nations GST"</c> 含 <c>"RST"</c>（在 FIRST 里），而 First Nations GST 由 CRA
    /// 征收、报在 105 行。裸子串匹配会把它当省税排除，那笔销项税就从申报表上消失 ——
    /// 它只在"未归入"诊断行里露出一个金额，而提示写的是"检查机构名"，
    /// 可机构名在操作员看来完全正确，于是没有可执行的线索。
    /// </remarks>
    [Theory]
    [InlineData("First Nations GST")]        // FIRST 含 RST
    [InlineData("Upstream Services GST")]    // UPSTREAM 含 PST
    public async Task Provincial_code_buried_inside_another_word_does_not_exclude(string agencyName)
    {
        var mapper = CreateMapper(out var reports);
        StubReports(reports, revenue: 1000m, rows: [Row(agencyName, output: 130m, input: 30m)]);

        var lines = (await mapper.MapAsync(From, To)).Data!.Lines;

        Amount(lines, "105").ShouldBe(130m);
        Amount(lines, "108").ShouldBe(30m);
    }

    /// <summary>
    /// GST/HST 关键字同样必须是独立的词 —— 藏在别的词里不算命中。
    /// </summary>
    /// <remarks>
    /// 这是上一条的反方向，而且<b>更危险</b>：<c>"Sacramento"</c> 含 <c>"CRA"</c>，
    /// 裸子串会把与 GST/HST 无关的税收进 105 行，报出去的是一个偏大的应缴数 ——
    /// 而多出来的钱<b>不会出现在任何诊断行上</b>（诊断行只报"没归进去"的，不报"多归进来"的）。
    /// </remarks>
    [Theory]
    [InlineData("Sacramento County Levy")]   // SACRAMENTO 含 CRA
    [InlineData("Democracy Fund Levy")]      // DEMOCRACY 含 CRA
    public async Task Gst_keyword_buried_inside_another_word_does_not_include(string agencyName)
    {
        var mapper = CreateMapper(out var reports);
        StubReports(reports, revenue: 1000m, rows: [Row(agencyName, output: 130m, input: 30m)]);

        var lines = (await mapper.MapAsync(From, To)).Data!.Lines;

        Amount(lines, "105").ShouldBe(0m);
        Amount(lines, "108").ShouldBe(0m);
        // 归不进去的钱必须被诊断行透出，否则申报数字凭空少一块而没人察觉
        lines.Count(l => l.Line == "-").ShouldBe(2);
    }

    // ── 行金额 ────────────────────────────────────────────────────────────────

    /// <summary>101 取自利润表的收入，<b>不是</b>税务汇总里的任何数字。</summary>
    [Fact]
    public async Task Line_101_comes_from_the_profit_and_loss_revenue_not_from_the_tax_summary()
    {
        var mapper = CreateMapper(out var reports);
        StubReports(reports, revenue: 50_000m, rows: [Row("CRA", output: 6_500m, input: 1_200m)]);

        var lines = (await mapper.MapAsync(From, To)).Data!.Lines;

        Amount(lines, "101").ShouldBe(50_000m);
    }

    [Fact]
    public async Task Line_109_is_output_tax_minus_input_tax()
    {
        var mapper = CreateMapper(out var reports);
        StubReports(reports, revenue: 50_000m, rows:
        [
            Row("CRA", output: 6_500m, input: 1_200m),
            Row("Ontario HST", output: 500m, input: 300m),
        ]);

        var result = await mapper.MapAsync(From, To);
        var lines = result.Data!.Lines;

        Amount(lines, "105").ShouldBe(7_000m);
        Amount(lines, "108").ShouldBe(1_500m);
        Amount(lines, "109").ShouldBe(5_500m);
        result.Data.NetTax.ShouldBe(5_500m);
    }

    [Fact]
    public async Task Line_109_is_negative_when_input_tax_exceeds_output_tax()
    {
        var mapper = CreateMapper(out var reports);
        StubReports(reports, revenue: 1_000m, rows: [Row("CRA", output: 100m, input: 400m)]);

        var lines = (await mapper.MapAsync(From, To)).Data!.Lines;

        // 退税期间（进项大于销项）必须报负数，钳到 0 会让企业拿不回该退的钱
        Amount(lines, "109").ShouldBe(-300m);
    }

    // ── 未归入提示行 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 未归入的销项与进项分两行报，<b>绝不轧差</b>。
    /// </summary>
    /// <remarks>
    /// 本例刻意让两侧漏掉的金额<b>相等</b>（各 210）—— 这正是机关名配错时的典型形态，
    /// 也正是轧差会把提示抹成 0、从而让提示行整个消失的那一种。
    /// </remarks>
    [Fact]
    public async Task Unmapped_output_and_input_are_reported_separately_never_netted()
    {
        var mapper = CreateMapper(out var reports);
        StubReports(reports, revenue: 1_000m,
            rows: [Row("CRA", output: 100m, input: 40m)],
            totalOutputTax: 310m,      // 比归入的 100 多 210
            totalInputTax: 250m);      // 比归入的 40 多 210

        var lines = (await mapper.MapAsync(From, To)).Data!.Lines;
        var unmapped = lines.Where(l => l.Line == "-").ToList();

        unmapped.Count.ShouldBe(2);
        unmapped.Select(l => l.Amount).ShouldAllBe(a => a == 210m);
        unmapped.ShouldAllBe(l => l.IsCalculated);
    }

    [Fact]
    public async Task No_unmapped_lines_when_every_tax_row_maps_to_gst_hst()
    {
        var mapper = CreateMapper(out var reports);
        StubReports(reports, revenue: 1_000m, rows: [Row("CRA", output: 130m, input: 30m)]);

        var lines = (await mapper.MapAsync(From, To)).Data!.Lines;

        lines.ShouldAllBe(l => l.Line != "-");
        lines.Count.ShouldBe(4);
    }

    // ── 失败传播 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 利润表取不到 → 整张表失败，<b>不</b>发出一张 101=0 的表。
    /// </summary>
    /// <remarks>
    /// 这条是本文件里最重要的一条。101 是整张 GST34 的取数基准；用 0 顶上会得到一张
    /// 结构完整、四行俱全、只有基准数是零的申报表 —— 没有任何迹象提示它是错的。
    /// </remarks>
    [Fact]
    public async Task Profit_and_loss_failure_fails_the_whole_return_instead_of_reporting_zero_revenue()
    {
        var mapper = CreateMapper(out var reports);
        StubReports(reports, revenue: 0m, rows: [Row("CRA", output: 130m, input: 30m)]);
        reports.Setup(r => r.GetProfitAndLossAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProfitAndLossReportDto>("No closing rate for the period.", 409));

        var result = await mapper.MapAsync(From, To);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        result.Data.ShouldBeNull();
    }

    [Fact]
    public async Task Tax_summary_failure_propagates_its_message_and_code()
    {
        var mapper = CreateMapper(out var reports);
        reports.Setup(r => r.GetTaxSummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<TaxSummaryReportDto>("Period is not closed.", 422));

        var result = await mapper.MapAsync(From, To);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(422);
        result.Message.ShouldBe("Period is not closed.");
    }

    // ── 表头 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Header_carries_the_period_and_the_base_currency_from_the_tax_summary()
    {
        var mapper = CreateMapper(out var reports);
        StubReports(reports, revenue: 1_000m, rows: [Row("CRA", output: 130m, input: 30m)], baseCurrency: "CAD");

        var dto = (await mapper.MapAsync(From, To)).Data!;

        dto.FormCode.ShouldBe("GST34");
        dto.Country.ShouldBe("CA");
        dto.Currency.ShouldBe("CAD");
        dto.PeriodFrom.ShouldBe(From);
        dto.PeriodTo.ShouldBe(To);
    }

    /// <summary>109 是算出来的行，前端据此渲染成只读；101/105/108 是取数行。</summary>
    [Fact]
    public async Task Only_the_net_tax_line_is_flagged_as_calculated()
    {
        var mapper = CreateMapper(out var reports);
        StubReports(reports, revenue: 1_000m, rows: [Row("CRA", output: 130m, input: 30m)]);

        var lines = (await mapper.MapAsync(From, To)).Data!.Lines;

        lines.Single(l => l.Line == "109").IsCalculated.ShouldBeTrue();
        lines.Where(l => l.Line is "101" or "105" or "108")
            .ShouldAllBe(l => !l.IsCalculated);
    }

    // ── 夹具 ──────────────────────────────────────────────────────────────────

    private static CraGstHstReturnMapper CreateMapper(out Mock<IFinancialReportService> reports)
    {
        reports = new Mock<IFinancialReportService>();
        var provider = new ServiceCollection().BuildServiceProvider();
        return new CraGstHstReturnMapper(provider, reports.Object);
    }

    private static TaxSummaryRowDto Row(string? agencyName, decimal output, decimal input)
        => new()
        {
            TaxRateId = Guid.NewGuid(),
            RateName = agencyName,
            AgencyName = agencyName,
            OutputTax = output,
            InputTax = input,
        };

    /// <summary>
    /// 默认让 <c>TotalOutputTax</c>/<c>TotalInputTax</c> 等于各行之和（=全部归入，无提示行）；
    /// 需要制造「未归入」时显式传更大的合计。
    /// </summary>
    private static void StubReports(
        Mock<IFinancialReportService> reports,
        decimal revenue,
        List<TaxSummaryRowDto> rows,
        decimal? totalOutputTax = null,
        decimal? totalInputTax = null,
        string baseCurrency = "CAD")
    {
        var summary = new TaxSummaryReportDto
        {
            From = From,
            To = To,
            BaseCurrency = baseCurrency,
            Rows = rows,
            TotalOutputTax = totalOutputTax ?? rows.Sum(r => r.OutputTax),
            TotalInputTax = totalInputTax ?? rows.Sum(r => r.InputTax),
        };
        summary.TotalNetTax = summary.TotalOutputTax - summary.TotalInputTax;

        reports.Setup(r => r.GetTaxSummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(summary));

        reports.Setup(r => r.GetProfitAndLossAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ProfitAndLossReportDto
            {
                BaseCurrency = baseCurrency,
                TotalIncome = revenue,
            }));
    }

    private static decimal Amount(IEnumerable<TaxReturnLine> lines, string lineNumber)
        => lines.Single(l => l.Line == lineNumber).Amount;
}
