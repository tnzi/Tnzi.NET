using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Finance.Documents.Metadata;
using Tnzi.Template.Services;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 支票版式目录与出厂版式模板的契约测试
/// </summary>
/// <remarks>
/// 锁三件事：
/// ①<b>目录、播种器、渲染器读的是同一份出厂声明</b>——每一条声明都要有真的嵌入资源，
///   否则「目录里选得到、播种时静默跳过、渲染时报模板不存在」；
/// ②<b><see cref="CheckLayout"/> 在模板驱动路径上真的生效</b>——选 <c>ThreePerPage</c>
///   之后解析出的是每页三张的模板，这正是本批改动要消灭的「能选、无效、还不报错」；
/// ③每份出厂模板都能渲染出它自己那套几何形状（分段坐标 + 每页张数）。
/// </remarks>
public class CheckLayoutCatalogTests
{
    // ── 出厂声明与资源的一致性 ──────────────────────────────

    [Fact]
    public void EveryBuiltInLayout_HasAnEmbeddedTemplateResource()
    {
        var resources = typeof(CheckTemplates).Assembly.GetManifestResourceNames();

        foreach (var builtIn in BuiltInCheckTemplates.All)
        {
            resources.ShouldContain(
                n => n.EndsWith("Templates." + builtIn.ResourceFile, StringComparison.Ordinal),
                $"Built-in check layout '{builtIn.Name}' declares resource '{builtIn.ResourceFile}', which is not embedded.");
        }
    }

    [Fact]
    public void BuiltInLayouts_CoverTheShippedSet()
    {
        var names = BuiltInCheckTemplates.All.Select(t => t.Name).ToList();

        names.ShouldBe(new[]
        {
            CheckTemplates.Cpa006Canada,
            CheckTemplates.Cpa006CanadaWindow,
            CheckTemplates.VoucherTopUs,
            CheckTemplates.VoucherMiddleUs,
            CheckTemplates.VoucherBottomUs,
            CheckTemplates.ThreePerPage
        });

        // 名字唯一：模板名是模板库的键，重名会让播种只写进去一份而目录显示两条
        names.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(names.Count);
    }

    // ── CheckLayout → 默认模板（本批改动的核心）────────────────

    [Fact]
    public void ThreePerPageLayout_ResolvesToTheThreeUpTemplate()
    {
        var resolution = BuiltInCheckTemplates.Resolve(null, CheckLayout.ThreePerPage);

        resolution.TemplateName.ShouldBe(CheckTemplates.ThreePerPage);
        resolution.ChecksPerPage.ShouldBe(3);
    }

    [Fact]
    public void VoucherLayout_ResolvesToTheFactoryDefaultTemplate()
    {
        var resolution = BuiltInCheckTemplates.Resolve(null, CheckLayout.Voucher);

        resolution.TemplateName.ShouldBe(CheckTemplates.DefaultName);
        resolution.ChecksPerPage.ShouldBe(1);
    }

    [Fact]
    public void ExplicitTemplate_WinsOverTheLayoutDefault()
    {
        // 版式的权威表达是模板：选了模板就以模板为准，枚举不再参与几何
        var resolution = BuiltInCheckTemplates.Resolve(CheckTemplates.VoucherBottomUs, CheckLayout.ThreePerPage);

        resolution.TemplateName.ShouldBe(CheckTemplates.VoucherBottomUs);
        resolution.ChecksPerPage.ShouldBe(1);
    }

    [Fact]
    public void CustomTemplate_FallsBackToTheLayoutForChecksPerPage()
    {
        // 自建模板不在出厂目录里，每页张数无从得知，退回按账户版式推断
        BuiltInCheckTemplates.Resolve("acme-house-style", CheckLayout.ThreePerPage).ChecksPerPage.ShouldBe(3);
        BuiltInCheckTemplates.Resolve("acme-house-style", CheckLayout.Voucher).ChecksPerPage.ShouldBe(1);
    }

    // ── 目录端点的合并清单 ──────────────────────────────────

    [Fact]
    public async Task Catalogue_ListsEveryBuiltInLayoutWithPickerMetadata()
    {
        // 模板存储缺席：只出出厂清单（且标出"尚未播种"），绝不整个失败
        var catalogue = new CheckTemplateCatalog(NullLogger<CheckTemplateCatalog>.Instance);

        var result = await catalogue.GetAllAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(BuiltInCheckTemplates.All.Count);
        result.Data.ShouldAllBe(t => t.IsBuiltIn && !t.IsSeeded);

        var threeUp = result.Data.Single(t => t.Name == CheckTemplates.ThreePerPage);
        threeUp.ChecksPerPage.ShouldBe(3);
        threeUp.Position.ShouldBeNull();
        threeUp.PaperSize.ShouldBe("Letter");
        threeUp.SupportedStockTypes.ShouldContain(CheckStockType.PrePrinted);
        threeUp.SupportedStockTypes.ShouldContain(CheckStockType.Blank);
        threeUp.DisplayName.ShouldNotBeNullOrWhiteSpace();
        threeUp.Description.ShouldNotBeNullOrWhiteSpace();

        var bottom = result.Data.Single(t => t.Name == CheckTemplates.VoucherBottomUs);
        bottom.Position.ShouldBe(CheckPosition.Bottom);
        bottom.Region.ShouldBe("US");
    }

    // ── 每份出厂模板都能渲染出自己的几何形状 ─────────────────

    [Theory]
    [InlineData("check-cpa006-ca.cshtml")]
    [InlineData("check-cpa006-ca-window.cshtml")]
    [InlineData("check-voucher-top-us.cshtml")]
    [InlineData("check-voucher-middle-us.cshtml")]
    [InlineData("check-voucher-bottom-us.cshtml")]
    [InlineData("check-3up.cshtml")]
    public async Task EveryBuiltInTemplate_RendersEveryCheckWithoutRazorEscapeDamage(string resourceFile)
    {
        var html = await RenderAsync(resourceFile, BuildRequest(CheckLayout.Voucher));

        // Razor 转义：CSS at-rule 必须还原成单个 @，写错会整页丢样式
        html.ShouldContain("@page { size: auto; margin: 0; }");
        html.ShouldContain("@media print");
        html.ShouldNotContain("@@");

        // 两张支票都要出现在票面上——切页写错时最典型的症状是安静地少印一张
        html.ShouldContain("Northwind Supplies");
        html.ShouldContain("Contoso Couriers");
        html.ShouldContain("***1,234.56");
        html.ShouldContain("***75.00");
    }

    [Fact]
    public async Task ThreeUpTemplate_PutsThreeChequesOnOnePageAndSpillsTheFourthOver()
    {
        var request = BuildRequest(CheckLayout.ThreePerPage, checkCount: 4);

        var html = await RenderAsync("check-3up.cshtml", request);

        // 4 张 → 2 页（3 + 1），页内三个槽位按 slot-1/2/3 落位
        // 数的是票面元素而不是裸 class 名 —— 样式表里也各有一条 .slot-N 定义
        CountOf(html, "class=\"cheque-page\"").ShouldBe(2);
        CountOf(html, "cheque slot-1").ShouldBe(2);
        CountOf(html, "cheque slot-2").ShouldBe(1);
        CountOf(html, "cheque slot-3").ShouldBe(1);
        // 0.5in 走纸边留白，三条支票带 8.5 x 3.5in 分段
        html.ShouldContain("top: 177.8mm");
        html.ShouldContain("top: 266.7mm");
    }

    [Fact]
    public async Task VoucherPositions_MoveTheChequeBandAndNothingElse()
    {
        var request = BuildRequest(CheckLayout.Voucher);

        var top = await RenderAsync("check-voucher-top-us.cshtml", request);
        var middle = await RenderAsync("check-voucher-middle-us.cshtml", request);
        var bottom = await RenderAsync("check-voucher-bottom-us.cshtml", request);

        top.ShouldContain(".cheque { top: 0mm; height: 88.9mm; }");
        middle.ShouldContain(".cheque { top: 88.9mm; height: 88.9mm; }");
        bottom.ShouldContain(".cheque { top: 190.5mm; height: 88.9mm; }");

        // 票在下时，4in 存根在页首、3.5in 存根居中（几何见 Payments Canada / US 通用票纸）
        bottom.ShouldContain(".stub-1 { top: 0mm; height: 101.6mm; }");
        bottom.ShouldContain(".stub-2 { top: 101.6mm; height: 88.9mm; }");

        // 三份都仍是完整的凭证式：两联存根 + 票面
        foreach (var html in new[] { top, middle, bottom })
        {
            html.ShouldContain("Voucher copy 1");
            html.ShouldContain("Voucher copy 2");
        }
    }

    [Fact]
    public async Task WindowEnvelopeTemplate_SharesTheAmountLineAndDropsThePayeeIntoTheWindow()
    {
        var html = await RenderAsync("check-cpa006-ca-window.cshtml", BuildRequest(CheckLayout.Voucher));

        // CPA-006 §5.4.1 Figure C：大写金额与数字金额同一行。
        // ★ 断言的是「同一行」这个不变量，不是两个毫米数：净空修正会让两者一起移动，
        //   而钉死坐标的断言在那时红的是「数字变了」，不是「Figure C 被破坏了」。
        var legalTop = MmOf(html, "legal", "top");
        var boxTop = MmOf(html, "courtesy", "top");
        var boxHeight = MmOf(html, "courtesy", "height");
        legalTop.ShouldBeInRange(boxTop, boxTop + boxHeight,
            "Figure C puts the written amount on the same row as the courtesy box.");

        // 收款人姓名与地址作为一整块下移到信封窗口位置
        html.ShouldContain("payee-window");
        html.ShouldContain("King Street 22");

        // 窗口块整个落在 MICR 净空带（票面底部 15.9mm，即 73mm 起）之上
        html.ShouldContain(".payee-window { left: 25mm; top: 38mm; width: 96mm; height: 24mm; }");
    }

    [Fact]
    public async Task BlankStock_PrintsOneMicrLinePerChequeOnEveryBuiltInTemplate()
    {
        foreach (var builtIn in BuiltInCheckTemplates.All)
        {
            var request = BuildRequest(CheckLayout.Voucher, stockType: CheckStockType.Blank);

            var html = await RenderAsync(builtIn.ResourceFile, request);

            // 白纸票纸每张票都要现打磁码；少一条就是一张银行读不出来的纸
            CountOf(html, "class=\"micr-line\"").ShouldBe(request.Checks.Count);
        }
    }

    // ── 法定金额行的币种字样（CPA-006 §5.4.1 第 9 条）─────────

    /// <summary>
    /// 开窗信封版把币种词并进机打大写金额，且<b>只出现一次</b>。
    /// </summary>
    /// <remarks>
    /// 这一版式的大写金额与数字金额同处一行，行尾就是金额框，放不下单独的 "DOLLARS"；
    /// 硬放会压掉规范要求的 0.64cm 净空（真实渲染实测：压过 <c>$</c> 9mm、压进框内 4mm），
    /// 而**屏幕预览看不出来**——预览与打印是同一份 HTML。
    /// </remarks>
    [Fact]
    public async Task WindowTemplate_IntegratesTheCurrencyWordIntoTheWrittenAmount()
    {
        var html = await RenderAsync("check-cpa006-ca-window.cshtml", BuildRequest(CheckLayout.Voucher, checkCount: 1));

        // 币种词在大写金额串里，且**在 * 填充之前**——接在填充之后等于留出一段可加写的空白
        html.ShouldContain("56/100 Dollars *");
        // 单独排版的币种元素必须不在（它正是压进金额框的那个）
        html.ShouldNotContain("class=\"abs legal-currency");
        // 票面上币种字样恰好一次。★ 只数**渲染出来的标记**：切片从 <body> 起，
        // 否则样式表里解释"为什么不能加回 DOLLARS 元素"的那段注释自己会被数进去
        // （第一版就是这么红的，与净空门禁第一版同一个坑）。
        var body = html[html.IndexOf("<body>", StringComparison.Ordinal)..];
        var face = body[..body.IndexOf("stub stub-1", StringComparison.Ordinal)];
        CountOf(face.ToUpperInvariant(), "DOLLARS").ShouldBe(1);
    }

    /// <summary>其余五套仍走「单独排版的 DOLLARS + 不带币种词的大写金额」，本轮不受影响。</summary>
    [Theory]
    [InlineData("check-cpa006-ca.cshtml")]
    [InlineData("check-voucher-top-us.cshtml")]
    [InlineData("check-voucher-middle-us.cshtml")]
    [InlineData("check-voucher-bottom-us.cshtml")]
    [InlineData("check-3up.cshtml")]
    public async Task OtherLayouts_KeepTheStandaloneCurrencyElement(string resourceFile)
    {
        var html = await RenderAsync(resourceFile, BuildRequest(CheckLayout.Voucher, checkCount: 1));

        html.ShouldContain("class=\"abs legal-currency");
        // 大写金额串里不带币种词（否则票面上出现两次）
        html.ShouldNotContain("56/100 Dollars");
    }

    /// <summary>
    /// 无论调用方给的大写金额带不带币种词，并入后都恰好一个 —— 剥离再追加，不是直接拼接。
    /// </summary>
    [Theory]
    [InlineData("One Thousand Two Hundred Thirty-Four and 56/100 Dollars")]
    [InlineData("One Thousand Two Hundred Thirty-Four and 56/100")]
    public void IntegratedCurrencyWord_AppearsExactlyOnceWhateverTheCallerPassed(string amountInWords)
    {
        var request = BuildRequest(CheckLayout.Voucher, checkCount: 1);
        request.Checks[0].AmountInWords = amountInWords;

        var model = CheckDocumentModelFactory.Create(request);

        var text = model.Checks[0].AmountInWordsWithCurrencyText;
        CountOf(text, "Dollars").ShouldBe(1);
        text.ShouldStartWith("One Thousand Two Hundred Thirty-Four and 56/100 Dollars");
        // 两条法定金额行填充到同一宽度，模板换用哪一个都不会改变防加写的行为
        text.Length.ShouldBe(model.Checks[0].AmountInWordsText.Length);
    }

    /// <summary>非 USD/CAD 币种并入 ISO 代码（与大写金额自带的收尾词同源）。</summary>
    [Fact]
    public void IntegratedCurrencyWord_UsesTheIsoCodeForOtherCurrencies()
    {
        var request = BuildRequest(CheckLayout.Voucher, checkCount: 1);
        request.Checks[0].Currency = "EUR";
        request.Checks[0].AmountInWords = "Forty-Two and 00/100 EUR";

        var model = CheckDocumentModelFactory.Create(request);

        model.Checks[0].AmountInWordsWithCurrencyText.ShouldStartWith("Forty-Two and 00/100 EUR");
        CountOf(model.Checks[0].AmountInWordsWithCurrencyText, "EUR").ShouldBe(1);
    }

    // ── 存根附加行 ───────────────────────────────────────────

    /// <summary>出厂的凭证式版式都要把消费应用给的存根行排出来（每页三张的版式没有存根联，故不在列）。</summary>
    [Theory]
    [InlineData("check-cpa006-ca.cshtml")]
    [InlineData("check-cpa006-ca-window.cshtml")]
    [InlineData("check-voucher-top-us.cshtml")]
    [InlineData("check-voucher-middle-us.cshtml")]
    [InlineData("check-voucher-bottom-us.cshtml")]
    public async Task StubTemplates_RenderTheApplicationSuppliedLinesOnBothStubs(string resourceFile)
    {
        var request = BuildRequest(CheckLayout.Voucher, checkCount: 1, stubLines:
        [
            new CheckStubLine("File No.", "2026-0042"),
            new CheckStubLine("Disbursements", null)
        ]);

        var html = await RenderAsync(resourceFile, request);

        // 两联存根各排一遍：一联归档、一联随票寄出，两边都要能对账
        CountOf(html, "File No.").ShouldBe(2);
        CountOf(html, "2026-0042").ShouldBe(2);
        // 只有标签的一行仍然排出来（分节标题），值为空即空单元格
        CountOf(html, "<th>Disbursements</th><td></td>").ShouldBe(2);
        // 既有固定行一条都没被挤掉
        html.ShouldContain("<th>Memo</th>");
    }

    /// <summary>没有附加行时，存根上不该多出任何一行 —— 这是"不让既有模板变形"的断言。</summary>
    [Theory]
    [InlineData("check-cpa006-ca.cshtml")]
    [InlineData("check-cpa006-ca-window.cshtml")]
    [InlineData("check-voucher-top-us.cshtml")]
    [InlineData("check-voucher-middle-us.cshtml")]
    [InlineData("check-voucher-bottom-us.cshtml")]
    public async Task WithoutStubLines_NoExtraRowIsEmitted(string resourceFile)
    {
        var html = await RenderAsync(resourceFile, BuildRequest(CheckLayout.Voucher));

        // 样式表里那两条 .stub-extra 规则仍在，但没有任何元素匹配它们 →
        // 排出来的纸与本机制引入前逐字相同。
        html.ShouldNotContain("<tr class=\"stub-extra\">");
    }

    // ── helpers ──────────────────────────────────────────────

    private static int CountOf(string haystack, string needle)
        => haystack.Split(needle).Length - 1;

    /// <summary>模板正文来自 Tnzi.Finance.Documents 的嵌入资源（与启动播种同一份，防两处漂移）。</summary>
    private static string ReadEmbeddedTemplate(string resourceFile)
    {
        var assembly = typeof(CheckTemplates).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("Templates." + resourceFile, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<string> RenderAsync(string resourceFile, CheckRenderRequest request)
    {
        // OptionsWrapper 而非 Options.Create：本程序集的 global using 引入了 Tnzi.Finance.Options
        // 命名空间，裸 Options.Create 会被解析成命名空间而非 Microsoft.Extensions.Options.Options。
        var engine = new RazorTemplateEngine(
            new OptionsWrapper<TemplateOptions>(new TemplateOptions { EnableCache = false }),
            NullLogger<RazorTemplateEngine>.Instance,
            new MemoryCache(new MemoryCacheOptions()));

        var resolution = BuiltInCheckTemplates.Resolve(request.TemplateName, request.Layout);
        return await engine.RenderAsync(ReadEmbeddedTemplate(resourceFile), CheckDocumentModelFactory.Create(request, resolution));
    }

    private static CheckRenderRequest BuildRequest(
        CheckLayout layout,
        CheckStockType stockType = CheckStockType.PrePrinted,
        int checkCount = 2,
        IReadOnlyList<CheckStubLine>? stubLines = null)
    {
        var payees = new[] { "Northwind Supplies", "Contoso Couriers", "Fabrikam Print", "Tailspin Filing" };
        var amounts = new[] { 1234.56m, 75m, 42m, 9.99m };
        var words = new[]
        {
            "One Thousand Two Hundred Thirty-Four and 56/100 Dollars",
            "Seventy-Five and 00/100 Dollars",
            "Forty-Two and 00/100 Dollars",
            "Nine and 99/100 Dollars"
        };

        return new CheckRenderRequest
        {
            Layout = layout,
            StockType = stockType,
            Scheme = BankNumberScheme.CaEft,
            BankName = "Royal Bank of Canada",
            AccountName = "Operating account",
            InstitutionNumber = "003",
            TransitNumber = "12345",
            AccountNumberPlain = stockType == CheckStockType.Blank ? "000123456" : null,
            Issuer = new CheckIssuerInfo
            {
                Name = "Acme Legal Services",
                AddressLines = new List<string> { "Bay Street 100", "Toronto ON M5J 2T3" },
                Phone = "+1 416 555 0100",
                SignatureName = "Jordan Lee",
                SignatureTitle = "Managing Partner"
            },
            Checks = Enumerable.Range(0, checkCount).Select(i => new CheckRenderItem
            {
                CheckNumber = 1001 + i,
                PayeeName = payees[i % payees.Length],
                PayeeAddressLines = new List<string> { "King Street 22", "Toronto ON M5H 1A1" },
                Amount = amounts[i % amounts.Length],
                Currency = "CAD",
                AmountInWords = words[i % words.Length],
                IssueDate = new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc),
                Memo = "August retainer",
                PaymentNumber = "PMT-00004" + i,
                Reference = "Invoice 8" + i,
                StubLines = stubLines == null ? [] : [.. stubLines]
            }).ToList()
        };
    }

    /// <summary>从渲染出来的样式表里读某个 class 的毫米属性（读不到即红，不静默当 0）。</summary>
    private static decimal MmOf(string html, string className, string property)
    {
        var rule = System.Text.RegularExpressions.Regex.Match(
            html, $@"^\.{className}\s*\{{([^}}]*)\}}", System.Text.RegularExpressions.RegexOptions.Multiline);
        rule.Success.ShouldBeTrue($"no '.{className}' rule in the rendered stylesheet");

        var value = System.Text.RegularExpressions.Regex.Match(
            rule.Groups[1].Value, $@"(?:^|;)\s*{property}\s*:\s*(-?[\d.]+)mm");
        value.Success.ShouldBeTrue($"no '{property}' in millimetres on '.{className}'");
        return decimal.Parse(value.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
