namespace Tnzi.Finance.Tax.Ca.Services;

/// <summary>
/// CRA GST/HST 申报表（GST34）行映射
/// </summary>
/// <remarks>
/// 映射的四行是 GST34 的骨架：
/// <list type="table">
/// <item><term>101</term><description>本期销售及其它收入（**不含税**的营业额）</description></item>
/// <item><term>105</term><description>征收/应征收的 GST/HST 与调整（销项）</description></item>
/// <item><term>108</term><description>进项税抵免 ITC 与调整（进项）</description></item>
/// <item><term>109</term><description>净税额 = 105 − 108</description></item>
/// </list>
///
/// ★**只映射，不判定**：哪些税率算 GST/HST、哪些属于免税或零税率，是**科目表与
/// 税码怎么建**的问题，由部署决定；本映射按税务机关（<c>TaxAgency</c>）筛选，
/// 机关名匹配 GST/HST 关键字。配错了机关名会漏行——所以结果里保留了未归入的
/// 金额，宁可让人看见对不上，也不悄悄吞掉。
///
/// **不产出 .tax 文件**：CRA 的电子报送格式随年度变化且需注册，属于消费应用要
/// 对接的东西；框架给到行金额为止，抄进 CRA 网站或交给报送服务都行。
/// </remarks>
public class CraGstHstReturnMapper : ApplicationService, ITaxReturnMapper
{
    private readonly IFinancialReportService _reports;

    public CraGstHstReturnMapper(IServiceProvider serviceProvider, IFinancialReportService reports)
        : base(serviceProvider)
    {
        _reports = Check.NotNull(reports);
    }

    public string CountryCode => "CA";

    public string FormCode => "GST34";

    /// <summary>诊断行的行号占位：它们不是 GST34 上的行，只是"这些钱没归进去"的提示。</summary>
    private const string UnmappedLineMarker = "-";

    /// <summary>
    /// 判定一个税务机关是不是 GST/HST 的收取方。
    /// </summary>
    /// <remarks>
    /// 关键字匹配而不是硬编码机关 Id：机关是部署自己建的主数据，框架不可能预知
    /// 它的主键。省级销售税（PST/QST/RST）刻意排除——它们不进 GST34。
    /// </remarks>
    private static bool IsGstHstAgency(string? agencyName)
    {
        if (string.IsNullOrWhiteSpace(agencyName))
            return false;
        var name = agencyName.ToUpperInvariant();
        if (name.Contains("PST") || name.Contains("QST") || name.Contains("RST"))
            return false;
        return name.Contains("GST") || name.Contains("HST") || name.Contains("CRA")
            || name.Contains("CANADA REVENUE");
    }

    public async Task<Result<TaxReturnDto>> MapAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (from > to)
            return Fail<TaxReturnDto>("The reporting period starts after it ends.", 400);

        var summary = await _reports.GetTaxSummaryAsync(from, to, cancellationToken);
        if (!summary.Succeeded)
            return Fail<TaxReturnDto>(summary.Message!, summary.Code ?? 400);
        var data = summary.Data!;

        var gstRows = data.Rows.Where(r => IsGstHstAgency(r.AgencyName)).ToList();
        var outputTax = gstRows.Sum(r => r.OutputTax);
        var inputTax = gstRows.Sum(r => r.InputTax);
        var netTax = outputTax - inputTax;

        // 101 是**不含税**营业额。税务汇总按税率维度聚合的是税额，不是税基，
        // 所以这里从利润表取收入——两者口径不同，混用会让 101 悄悄错掉。
        //
        // ★取不到就整张表失败，不拿 0 顶上：一张 101 = 0.00 而 105/108/109 都非零的
        // GST34 看起来是完整的，操作员没有理由怀疑它 —— 而 101 正是整张表的取数基准。
        var pnl = await _reports.GetProfitAndLossAsync(from, to, cancellationToken);
        if (!pnl.Succeeded)
            return Fail<TaxReturnDto>(pnl.Message!, pnl.Code ?? 400);
        var revenue = pnl.Data!.TotalIncome;

        var lines = new List<TaxReturnLine>
        {
            new("101", "Sales and other revenue (excluding GST/HST)", revenue),
            new("105", "GST/HST collected or collectible", outputTax),
            new("108", "Input tax credits (ITCs)", inputTax),
            new("109", "Net tax", netTax, IsCalculated: true),
        };

        // 有税额却没归进 GST/HST 的部分单列出来：多半是机关名没按 GST/HST 命名，
        // 悄悄吞掉会让申报数字凭空少一块而没人察觉。
        //
        // ★销项与进项**分两行报，不做净额轧差**：机关名配错时两侧通常一起漏，
        // 轧差后恰好抵消成 0，这一行就不出现了 —— 而那正是它存在的唯一场景。
        var unmappedOutput = data.TotalOutputTax - outputTax;
        var unmappedInput = data.TotalInputTax - inputTax;
        if (unmappedOutput != 0)
        {
            lines.Add(new TaxReturnLine(
                UnmappedLineMarker, "Not mapped to GST/HST: tax collected (check the tax agency names)", unmappedOutput, IsCalculated: true));
        }
        if (unmappedInput != 0)
        {
            lines.Add(new TaxReturnLine(
                UnmappedLineMarker, "Not mapped to GST/HST: input tax (check the tax agency names)", unmappedInput, IsCalculated: true));
        }

        return Ok(new TaxReturnDto
        {
            FormCode = FormCode,
            FormName = "GST34: Goods and Services Tax / Harmonized Sales Tax Return",
            Country = CountryCode,
            PeriodFrom = from,
            PeriodTo = to,
            Currency = data.BaseCurrency,
            Lines = lines,
            NetTax = netTax,
        });
    }
}
