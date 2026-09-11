using System.Text.RegularExpressions;
using Tnzi.Finance.Documents.Metadata;

namespace Tnzi.Finance.Tests;

/// <summary>
/// CPA Standard 006 §5.4.1 item 6：日期字段的 "DATE" 字样与格式指引
/// </summary>
/// <remarks>
/// 条文两句，两套加拿大版式此前<b>两句都没做到</b>：
/// <list type="number">
/// <item><c>The word "DATE" shall be in a minimum of 8 point font.</c> —— 两套版式一个 DATE 字样都没印，
/// 只有 <c>Y Y Y Y M M D D</c> 那条格式指引。<b>指引不能顶替字样</b>：指引说的是「按哪种格式填」，
/// 字样说的是「这一栏是什么」。</item>
/// <item><c>Field Indicators ... shall be printed below the guidance boxes (or date if the date field is
/// filled using an automated process)</c> —— 我们的日期是机打的，故指引必须在日期<b>下方</b>；
/// 两套版式都印在上方。</item>
/// </list>
/// <para>
/// ★ 只管两套 <b>CPA</b> 版式：另外三套美式凭证与每页三张的版式不受 CPA-006 管辖，
/// 它们自己的 "Date" 小字（6-6.5pt）在美式支票上是常规做法，按加拿大条文去改它们没有依据。
/// </para>
/// <para>
/// ★ 断言打在<b>渲染出来的票面标记</b>上（从 <c>&lt;body&gt;</c> 起切片），不是整份文档的文本 ——
/// 本仓在这里栽过：模板注释里正需要点名这些类去说明为什么这么排，按整篇断言会被自己的文档判红。
/// </para>
/// </remarks>
public class CheckDateFieldComplianceTests
{
    /// <summary>CPA-006 §5.4.1 item 6 给 "DATE" 字样定的下限。</summary>
    private const decimal MinimumDateWordPt = 8m;

    /// <summary>指引字号区间（同一条：min 6pt / max 8pt）。</summary>
    private const decimal MinIndicatorPt = 6m;
    private const decimal MaxIndicatorPt = 8m;

    public static TheoryData<string> CanadianLayouts() =>
    [
        "check-cpa006-ca.cshtml", "check-cpa006-ca-window.cshtml"
    ];

    [Theory]
    [MemberData(nameof(CanadianLayouts))]
    public void TheWordDate_IsPrintedAtEightPointOrMore(string resourceFile)
    {
        var css = ReadEmbeddedTemplate(resourceFile);

        Pt(css, resourceFile, "date-label").ShouldBeGreaterThanOrEqualTo(MinimumDateWordPt,
            $"{resourceFile}: CPA-006 5.4.1 item 6 sets {MinimumDateWordPt}pt as the minimum for the word \"DATE\".");
    }

    [Theory]
    [MemberData(nameof(CanadianLayouts))]
    public async Task TheWordDate_ActuallyReachesTheFace(string resourceFile)
    {
        // 样式规则在、元素没渲出来，与「这条要求没做」在纸上完全一样
        var face = await RenderFaceAsync(resourceFile);

        face.Contains("date-label", StringComparison.Ordinal).ShouldBeTrue(
            $"{resourceFile}: the '.date-label' rule exists but no element carries it.");
        face.Contains(">DATE<", StringComparison.Ordinal).ShouldBeTrue(
            $"{resourceFile}: the date field carries no \"DATE\" word.");
    }

    [Theory]
    [MemberData(nameof(CanadianLayouts))]
    public void TheFormatIndicators_StayWithinTheSixToEightPointRange(string resourceFile)
    {
        var css = ReadEmbeddedTemplate(resourceFile);
        var pt = Pt(css, resourceFile, "date-guide");

        pt.ShouldBeGreaterThanOrEqualTo(MinIndicatorPt, $"{resourceFile}: indicators below 6pt.");
        pt.ShouldBeLessThanOrEqualTo(MaxIndicatorPt, $"{resourceFile}: indicators above 8pt.");
    }

    /// <summary>★★ 本轮的第二条缺陷：指引必须排在日期<b>下方</b>。</summary>
    /// <remarks>
    /// 判据是<b>渲染出来的标记里谁在前</b>，不是坐标 —— 两者都在同一个绝对定位块里按文档流排，
    /// 所以「先出现的在上面」就是这份布局的全部事实，而它恰好也是最难写错的一种断言。
    /// </remarks>
    [Theory]
    [MemberData(nameof(CanadianLayouts))]
    public async Task TheFormatIndicators_ComeAfterTheDateValue(string resourceFile)
    {
        var face = await RenderFaceAsync(resourceFile);

        var value = face.IndexOf("date-value", StringComparison.Ordinal);
        var guide = face.IndexOf("date-guide", StringComparison.Ordinal);

        value.ShouldBeGreaterThan(-1, $"{resourceFile}: no date value on the face.");
        guide.ShouldBeGreaterThan(-1, $"{resourceFile}: no format indicators on the face.");
        guide.ShouldBeGreaterThan(value,
            $"{resourceFile}: CPA-006 5.4.1 item 6 puts the format indicators BELOW the date when the date is "
            + "machine-printed. They are rendered above it.");
    }

    /// <summary>
    /// 字样与指引都是<b>票纸自带</b>的，预印票纸上不得再打一遍。
    /// </summary>
    /// <remarks>
    /// 少了这一条，「补上 DATE 字样」就会在预印票纸上变成一次套印重影 —— 修一个缺陷造一个新的。
    /// </remarks>
    [Theory]
    [MemberData(nameof(CanadianLayouts))]
    public async Task TheWordAndTheIndicators_AreCarriedByPrePrintedStock(string resourceFile)
    {
        var prePrinted = await RenderFaceAsync(resourceFile, CheckStockType.PrePrinted);
        var blank = await RenderFaceAsync(resourceFile, CheckStockType.Blank);

        Regex.IsMatch(prePrinted, @"class=""date-label noprint""").ShouldBeTrue(
            $"{resourceFile}: the \"DATE\" word would be printed on top of the one already on the stock.");
        Regex.IsMatch(prePrinted, @"class=""date-guide noprint""").ShouldBeTrue(
            $"{resourceFile}: the format indicators would be printed on top of the ones already on the stock.");

        // 白纸票纸上整张票都是打印机打的，两者都必须真的印出来
        Regex.IsMatch(blank, @"class=""date-label ""|class=""date-label""").ShouldBeTrue(
            $"{resourceFile}: blank stock carries nothing, so the \"DATE\" word must be printed.");
        Regex.IsMatch(blank, @"class=""date-guide ""|class=""date-guide""").ShouldBeTrue(
            $"{resourceFile}: blank stock carries nothing, so the indicators must be printed.");
    }

    // ── 夹具 ─────────────────────────────────────────────────

    /// <summary>渲染并只取票面标记（从 <c>&lt;body&gt;</c> 起），把样式表与注释排除在断言之外。</summary>
    private static async Task<string> RenderFaceAsync(string resourceFile, CheckStockType stock = CheckStockType.Blank)
    {
        var html = await CheckTemplateHarness.RenderAsync(resourceFile, new CheckRenderRequest
        {
            Layout = CheckLayout.Voucher,
            StockType = stock,
            Scheme = BankNumberScheme.CaEft,
            BankName = "Bank of the North",
            AccountName = "Operating",
            InstitutionNumber = "003",
            TransitNumber = "12345",
            AccountNumberPlain = stock == CheckStockType.Blank ? "000123456" : null,
            TemplateName = TemplateNameOf(resourceFile),
            Checks =
            [
                new CheckRenderItem
                {
                    CheckNumber = 1001,
                    PayeeName = "Northwind Supplies",
                    Amount = 1234.56m,
                    Currency = "CAD",
                    AmountInWords = "One Thousand Two Hundred Thirty-Four and 56/100 Dollars",
                    IssueDate = new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc)
                }
            ]
        });

        var body = html.IndexOf("<body>", StringComparison.Ordinal);
        body.ShouldBeGreaterThan(-1, "the rendered document has no <body>");
        return html[body..];
    }

    private static string TemplateNameOf(string resourceFile)
        => BuiltInCheckTemplates.All.Single(t => t.ResourceFile == resourceFile).Name;

    /// <summary>取某个 class 的 <c>font-size</c>（pt）。取不到即红——门禁不许静默跳过。</summary>
    private static decimal Pt(string css, string resourceFile, string className)
    {
        var rule = Regex.Match(css, $@"^\.{Regex.Escape(className)}\s*\{{([^}}]*)\}}", RegexOptions.Multiline);
        rule.Success.ShouldBeTrue(
            $"{resourceFile}: no '.{className}' rule found. A gate that cannot find what it measures reports "
            + "the same green as a compliant sheet.");

        var size = Regex.Match(rule.Groups[1].Value, @"(?:^|;)\s*font-size\s*:\s*([\d.]+)pt");
        size.Success.ShouldBeTrue($"{resourceFile}: '.{className}' declares no font-size in points.");
        return decimal.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static string ReadEmbeddedTemplate(string resourceFile)
        => CheckTemplateHarness.ReadEmbeddedTemplate(resourceFile);
}
