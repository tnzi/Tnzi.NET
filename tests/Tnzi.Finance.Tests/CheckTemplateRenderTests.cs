using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Finance.Documents.Metadata;
using Tnzi.Finance.Documents.Services.Internal;
using Tnzi.Template.Services;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 内置 CPA-006 支票模板的渲染契约测试
/// </summary>
/// <remarks>
/// 直接用 <see cref="RazorTemplateEngine"/> 渲染嵌入的模板正文（不经数据库存储），锁定三件最易回归的事：
/// ①Razor 转义正确（CSS 的 <c>@@page</c>/<c>@@media</c> 必须还原成单个 <c>@</c>，写错会整页丢样式）；
/// ②强类型模型经 dynamic 绑定可用（<c>@Model.Checks</c> 逐张、嵌套集合逐行）；
/// ③预印/白纸两种票纸模式与 MICR 打印条件符合 CPA-006。
/// </remarks>
public class CheckTemplateRenderTests
{
    [Fact]
    public async Task Template_RendersEveryChequeWithCpa006Elements()
    {
        var request = BuildRequest(CheckStockType.PrePrinted);
        var html = await RenderAsync(request);

        // 每张支票一页，页间分页
        html.Split("class=\"cheque-page\"").Length.ShouldBe(3); // 2 张支票 → 2 次出现
        html.ShouldContain("page-break-after: always");

        // Razor 转义：CSS at-rule 必须还原成单个 @
        html.ShouldContain("@page { size: auto; margin: 0; }");
        html.ShouldContain("@media print");
        html.ShouldNotContain("@@");

        // 票面要素
        html.ShouldContain("Acme Legal Services");        // 出票方抬头
        html.ShouldContain("Bay Street 100");             // 出票方地址（多行）
        html.ShouldContain("Northwind Supplies");         // 收款人
        html.ShouldContain("***1,234.56");                // 防篡改金额数字
        html.ShouldContain("One Thousand Two Hundred Thirty-Four and 56/100"); // 法定金额大写
        html.ShouldNotContain("56/100 Dollars");          // 币种词只由行尾预印 DOLLARS 提供，不与大写串重复
        html.ShouldContain("DOLLARS");                    // 法定金额行尾币种字样
        html.ShouldContain("2026 07 23");                 // CPA-006 的 Y Y Y Y M M D D 日期
        html.ShouldContain("Pay to the order of");
        html.ShouldContain("Voucher copy 1");             // 存根联 1
        html.ShouldContain("Voucher copy 2");             // 存根联 2
        html.ShouldContain("Invoice 88");                 // 存根明细：参考号
    }

    [Fact]
    public async Task PrePrintedStock_MarksPreprintedElementsNoPrintAndSkipsMicr()
    {
        var html = await RenderAsync(BuildRequest(CheckStockType.PrePrinted));

        // 预印元素在屏幕上照常可见，打印时经 .noprint 隐藏但保留占位
        html.ShouldContain("class=\"abs cheque-no noprint\"");
        html.ShouldContain("class=\"courtesy-sign noprint\"");
        html.ShouldContain(".noprint { visibility: hidden; }");

        // 预印票纸的 MICR 已印在票纸上，不得重复打印（.micr-line 只在样式表里留有定义，不落到票面）
        html.ShouldNotContain("class=\"micr-line\"");
    }

    [Fact]
    public async Task BlankStock_PrintsMicrLineAndDropsNoPrintMarkers()
    {
        var html = await RenderAsync(BuildRequest(CheckStockType.Blank));

        html.ShouldContain("blank-stock");
        html.ShouldContain("class=\"micr-line\"");
        // CA CPA-006 磁码序：⑈支票号⑈ ⑆transit⑉institution⑆ 账号⑈，字形映射 A/B/C/D
        html.ShouldContain("C1001C A12345D003A 000123456C");
        // 白纸模式没有预印元素，noprint 标记不出现在票面元素上
        html.ShouldContain("class=\"abs cheque-no \"");
    }

    [Fact]
    public async Task PreviewRequest_StampsNonNegotiableWatermark()
    {
        var request = BuildRequest(CheckStockType.PrePrinted);
        request.IsPreview = true;

        var html = await RenderAsync(request);

        html.ShouldContain("PREVIEW - NOT NEGOTIABLE");
        html.ShouldContain("preview-watermark");
    }

    [Fact]
    public async Task Offsets_ProduceMillimetreTranslateOnEveryPage()
    {
        var request = BuildRequest(CheckStockType.PrePrinted);
        request.OffsetXMm = 1.5m;
        request.OffsetYMm = -2m;

        var html = await RenderAsync(request);

        html.ShouldContain("transform: translate(1.5mm, -2mm);");
    }

    [Fact]
    public void CalibrationSheet_DrawsMillimetreRulerAndZoneMarkers()
    {
        var html = CheckCalibrationSheet.Build(BuildRequest(CheckStockType.PrePrinted));

        html.ShouldContain("Cheque alignment calibration");
        html.ShouldContain("cheque body ends (88.9mm)");
        html.ShouldContain("MICR clear band starts (73.0mm)");
        html.ShouldContain("class=\"tick tick-h\" style=\"top:100mm\"");
        html.ShouldContain(CheckTemplates.DefaultName);
    }

    /// <summary>模板正文来自 Tnzi.Finance.Documents 的嵌入资源（与启动播种同一份，防两处漂移）。</summary>
    private static string ReadEmbeddedTemplate()
    {
        var assembly = typeof(CheckTemplates).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("check-cpa006-ca.cshtml", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<string> RenderAsync(CheckRenderRequest request)
    {
        // OptionsWrapper 而非 Options.Create：本程序集的 global using 引入了 Tnzi.Finance.Options
        // 命名空间，裸 Options.Create 会被解析成命名空间而非 Microsoft.Extensions.Options.Options。
        var engine = new RazorTemplateEngine(
            new OptionsWrapper<TemplateOptions>(new TemplateOptions { EnableCache = false }),
            NullLogger<RazorTemplateEngine>.Instance,
            new MemoryCache(new MemoryCacheOptions()));

        return await engine.RenderAsync(ReadEmbeddedTemplate(), CheckDocumentModelFactory.Create(request));
    }

    private static CheckRenderRequest BuildRequest(CheckStockType stockType) => new()
    {
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
        Checks = new List<CheckRenderItem>
        {
            new()
            {
                CheckNumber = 1001,
                PayeeName = "Northwind Supplies",
                PayeeAddressLines = new List<string> { "King Street 22", "Toronto ON M5H 1A1" },
                Amount = 1234.56m,
                Currency = "CAD",
                AmountInWords = "One Thousand Two Hundred Thirty-Four and 56/100 Dollars",
                IssueDate = new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc),
                Memo = "August retainer",
                PaymentNumber = "PMT-000042",
                Reference = "Invoice 88"
            },
            new()
            {
                CheckNumber = 1002,
                PayeeName = "Contoso Couriers",
                Amount = 75m,
                Currency = "CAD",
                AmountInWords = "Seventy-Five and 00/100 Dollars",
                IssueDate = new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc)
            }
        }
    };
}
