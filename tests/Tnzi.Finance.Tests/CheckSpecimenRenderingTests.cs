using Tnzi.Finance.Documents.Metadata;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 样张在屏幕上把「票纸自带的」与「打印机现打的」分开，而真票与付款预览一个字节都不变
/// </summary>
/// <remarks>
/// 缺陷是：预印元素带的 <c>noprint</c> 只定义在 <c>@media print</c> 块里，**屏幕上照常显示** ——
/// 于是两种票纸的样张渲出来几乎一模一样，版式选择器里那个票纸开关按下去看不出变化。
/// <para>
/// ★ 这里有一处<b>刻意的不对称</b>：付款预览（<c>IsPreview</c>）屏幕上就该显示完整票面，
/// 那时的任务是校对整张支票；需要第三种呈现的只有样张（<c>IsSpecimen</c>）。
/// 本组的后半就是钉住这条不对称 —— 真票与预览的产物必须逐字节不变。
/// </para>
/// </remarks>
public class CheckSpecimenRenderingTests
{
    private static readonly string[] AllTemplates =
    [
        "check-cpa006-ca.cshtml",
        "check-cpa006-ca-window.cshtml",
        "check-voucher-top-us.cshtml",
        "check-voucher-middle-us.cshtml",
        "check-voucher-bottom-us.cshtml",
        "check-3up.cshtml"
    ];

    public static TheoryData<string> Templates
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var t in AllTemplates)
                data.Add(t);
            return data;
        }
    }

    // ── 样张：票纸差别在屏幕上看得出来 ────────────────────────

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task Specimen_MarksThePageRootSoTheStylesheetCanFadeStockElements(string resourceFile)
    {
        var html = await RenderAsync(resourceFile, BuildRequest(isSpecimen: true));

        // 页根带上 specimen 修饰类，样式表据此淡显预印元素
        html.ShouldContain("class=\"cheque-page specimen\"");
        // 而那条规则确实在样式表里（否则类名挂了也没人消费）
        html.ShouldContain(".specimen .noprint { opacity: .28; }");
    }

    [Fact]
    public async Task PrePrintedSpecimen_HasFadeTargets_AndBlankSpecimenHasNone()
    {
        var prePrinted = await RenderAsync("check-cpa006-ca.cshtml",
            BuildRequest(isSpecimen: true, stockType: CheckStockType.PrePrinted));
        var blank = await RenderAsync("check-cpa006-ca.cshtml",
            BuildRequest(isSpecimen: true, stockType: CheckStockType.Blank));

        // 预印票纸：票面上真的有一批 noprint 元素可被淡显（不然那条规则等于没有）
        CountOf(BodyOf(prePrinted), "noprint").ShouldBeGreaterThan(5);
        // 白纸：一个都没有 —— 打印机把整张票都打上去，没有"票纸自带"的东西
        CountOf(BodyOf(blank), "noprint").ShouldBe(0);
        // 白纸样张继续现打磁码行（既有行为，不得回退）
        blank.ShouldContain("class=\"micr-line\"");
        prePrinted.ShouldNotContain("class=\"micr-line\"");
    }

    // ── 真票与付款预览：逐字节不变 ────────────────────────────

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task RealCheque_RendersByteIdenticallyToTheTemplateWithoutSpecimenSupport(string resourceFile)
    {
        var shipped = ReadEmbeddedTemplate(resourceFile);
        var withoutSpecimen = StripSpecimenSupport(shipped);

        // ★ 先证明这次剥离真的剥掉了东西 —— 否则本条断言恒真，是一条绿色的谎
        withoutSpecimen.ShouldNotBe(shipped);

        var request = BuildRequest(isSpecimen: false);
        var actual = await RenderTemplateBodyAsync(shipped, request);
        var before = await RenderTemplateBodyAsync(withoutSpecimen, request);

        AssertPixelIdenticalToBefore(actual, before);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task PaymentPreview_AlsoRendersByteIdentically_AndKeepsTheFullFaceOnScreen(string resourceFile)
    {
        var shipped = ReadEmbeddedTemplate(resourceFile);
        var withoutSpecimen = StripSpecimenSupport(shipped);
        withoutSpecimen.ShouldNotBe(shipped);

        // 付款预览 = IsPreview 但不是样张：校对整张支票的场景，票面不该被淡显
        var request = BuildRequest(isSpecimen: false);
        request.IsPreview = true;

        var actual = await RenderTemplateBodyAsync(shipped, request);
        var before = await RenderTemplateBodyAsync(withoutSpecimen, request);

        AssertPixelIdenticalToBefore(actual, before);
        actual.ShouldContain("class=\"cheque-page\"");
    }

    /// <summary>
    /// 「本轮改动对这份产物逐像素没有影响」的三段式自证。
    /// </summary>
    /// <remarks>
    /// ★ 整份文档做不到逐字节相同，也<b>不应该</b>要求它 —— 样式表确实多了一条规则。
    /// 能证明也必须证明的是这三条，合起来蕴含逐像素相同：
    /// <list type="number">
    /// <item><b>票面标记逐字节相同</b>：页根那个三元式对真票没有任何贡献；</item>
    /// <item><b>整份文档的唯一差异就是那段 specimen CSS</b>（把它剥掉即与改动前逐字节相同）；</item>
    /// <item><b>那段 CSS 匹配不到任何元素</b>：真票的票面标记里一处 <c>specimen</c> 都没有。</item>
    /// </list>
    /// </remarks>
    private static void AssertPixelIdenticalToBefore(string actual, string before)
    {
        BodyOf(actual).ShouldBe(BodyOf(before),
            "the page markup of a real cheque must be byte-identical to what it was before specimen mode");

        StripSpecimenSupport(actual).ShouldBe(before,
            "the only difference in the whole document must be the specimen CSS block");

        BodyOf(actual).ShouldNotContain("specimen", Case.Insensitive,
            "no element on a real cheque may carry the modifier, so the added rule matches nothing");
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task WithoutSpecimenMode_NoElementCarriesTheModifier(string resourceFile)
    {
        var html = await RenderAsync(resourceFile, BuildRequest(isSpecimen: false));

        // 样式表里可以提 specimen（规则与注释），但**票面标记里一处都不能有** ——
        // 匹配不到任何元素的规则，对渲染结果的贡献恰好是零。
        BodyOf(html).ShouldNotContain("specimen", Case.Insensitive);
        html.ShouldContain("class=\"cheque-page\"");
    }

    // ── helpers ──────────────────────────────────────────────

    /// <summary>切到 <c>&lt;body&gt;</c> 之后：样式表里的注释与规则会提到 specimen，票面标记里不该有。</summary>
    private static string BodyOf(string html)
        => html[html.IndexOf("<body>", StringComparison.Ordinal)..];

    private static int CountOf(string haystack, string needle)
        => haystack.Split(needle).Length - 1;

    /// <summary>
    /// 把模板还原成"样张支持从未加过"的样子：删掉那条 CSS 规则与它的说明，页根还原。
    /// </summary>
    /// <remarks>
    /// 这是本组的 diff 自证工具 —— 用它渲染出的产物与出厂模板的产物相比对，
    /// 就是"这次改动对真票有没有影响"的直接答案。日后若有人加了一条**没有**
    /// 收在 <c>.specimen</c> 下的规则，剥离后的产物就会不同，本组立刻变红。
    /// </remarks>
    private static string StripSpecimenSupport(string template)
    {
        var stripped = template;

        var blockStart = stripped.IndexOf("/* ---------- specimen mode", StringComparison.Ordinal);
        if (blockStart >= 0)
        {
            const string lastLine = ".specimen .noprint { opacity: .28; }";
            var blockEnd = stripped.IndexOf(lastLine, blockStart, StringComparison.Ordinal);
            blockEnd.ShouldBeGreaterThan(-1, "the specimen CSS block must end with its single rule");
            stripped = stripped.Remove(blockStart, blockEnd + lastLine.Length - blockStart);
        }

        var razorComment = stripped.IndexOf("@* The ternary supplies its own leading space", StringComparison.Ordinal);
        if (razorComment >= 0)
        {
            var afterComment = stripped.IndexOf("*@", razorComment, StringComparison.Ordinal) + 2;
            stripped = stripped.Remove(razorComment, afterComment - razorComment);
        }

        return stripped.Replace(
            "<section class=\"cheque-page@(Model.IsSpecimen ? \" specimen\" : \"\")\" style=\"@Model.OffsetStyle\">",
            "<section class=\"cheque-page\" style=\"@Model.OffsetStyle\">",
            StringComparison.Ordinal);
    }

    private static Task<string> RenderAsync(string resourceFile, CheckRenderRequest request)
        => CheckTemplateHarness.RenderAsync(resourceFile, request);

    private static Task<string> RenderTemplateBodyAsync(string templateBody, CheckRenderRequest request)
        => CheckTemplateHarness.RenderBodyAsync(templateBody, request);

    private static string ReadEmbeddedTemplate(string resourceFile)
        => CheckTemplateHarness.ReadEmbeddedTemplate(resourceFile);

    private static CheckRenderRequest BuildRequest(
        bool isSpecimen,
        CheckStockType stockType = CheckStockType.PrePrinted)
        => new()
        {
            Layout = CheckLayout.Voucher,
            StockType = stockType,
            IsSpecimen = isSpecimen,
            IsPreview = isSpecimen,
            PreviewLabel = isSpecimen ? "SPECIMEN - NOT NEGOTIABLE" : null,
            Scheme = BankNumberScheme.CaEft,
            BankName = "Royal Bank of Canada",
            AccountName = "Operating account",
            InstitutionNumber = "003",
            TransitNumber = "12345",
            AccountNumberPlain = stockType == CheckStockType.Blank ? "000123456" : null,
            TemplateName = CheckTemplates.Cpa006Canada,
            Issuer = new CheckIssuerInfo
            {
                Name = "Acme Legal Services",
                AddressLines = ["Bay Street 100", "Toronto ON M5J 2T3"],
                SignatureName = "Jordan Lee"
            },
            Checks =
            [
                new CheckRenderItem
                {
                    CheckNumber = 1001,
                    PayeeName = "Northwind Supplies",
                    PayeeAddressLines = ["King Street 22", "Toronto ON M5H 1A1"],
                    Amount = 1234.56m,
                    Currency = "CAD",
                    AmountInWords = "One Thousand Two Hundred Thirty-Four and 56/100 Dollars",
                    IssueDate = new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc),
                    Memo = "August retainer",
                    PaymentNumber = "PMT-000042",
                    Reference = "Invoice 88"
                }
            ]
        };
}
