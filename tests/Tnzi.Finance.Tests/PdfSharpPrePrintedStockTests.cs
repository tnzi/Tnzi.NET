using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;

namespace Tnzi.Finance.Tests;

/// <summary>
/// PdfSharp 认识预印票纸：票纸自带的元素不再画（否则就是套印重影）
/// </summary>
/// <remarks>
/// 缺陷是：该渲染器对 <see cref="CheckStockType"/> 只有两处分支、都在 MICR 上，
/// 抬头 / 银行标识 / 支票号 / "PAY TO THE ORDER OF" / 金额框一律无条件画上去 ——
/// <b>往预印票纸上打就是把纸上已经印好的东西再印一遍</b>。而 HTML 那条路早就用
/// <c>noprint</c> 处理对了，两条渲染路径对同一个语义给出了不同结果。
/// <para>
/// ★ 断言打在 <b>PDF 内容流</b>上而不是纯函数上：这个仓库反复兑现过「纯函数测试
/// 证明不了机制已接上」。PdfSharp 的文本以可读字面量写进内容流
/// （<c>(Bank of the North) Tj</c>），所以「哪些字画了、画在哪」是可以直接读出来的。
/// </para>
/// <para>
/// ★ 位置比对是<b>同一次运行内</b>白纸与预印两份产物之间的比对，故与本机字体度量无关
/// （两边用的是同一套字体）。绝对坐标由累加 <c>Td</c> 得到 —— PdfSharp 发的是
/// <b>相对</b>位移，删掉前面的绘制会让后面的 <c>Td</c> 数值跟着变，直接比数值会假红。
/// </para>
/// </remarks>
public class PdfSharpPrePrintedStockTests
{
    /// <summary>票纸自带、因而预印时不该再打的字样（对应 HTML 模板挂 <c>noprint</c> 的那一组）。</summary>
    private const string BankName = "Bank of the North";
    /// <summary>票面上的支票号。<b>补零到默认位数</b> —— 与磁码行里的串行号必须逐字相同。</summary>
    private const string ChequeNumber = "No. 01001";
    private const string PayToLabel = "Pay to the order of";

    /// <summary>打印机这一次真正补上去的内容，任何票纸下都必须在。</summary>
    private const string Payee = "Northwind Supplies";
    private const string AmountDigits = "**1,234.56";
    private const string AmountWords = "One Thousand Two Hundred Thirty-Four and 56/100 Dollars";
    private const string DateValue = "2026-03-17";

    // ── 决定表 ───────────────────────────────────────────────

    [Theory]
    [InlineData(CheckStockType.Blank, false, PdfSharpCheckRenderer.StockElementMode.Print)]
    [InlineData(CheckStockType.Blank, true, PdfSharpCheckRenderer.StockElementMode.Print)]
    [InlineData(CheckStockType.PrePrinted, false, PdfSharpCheckRenderer.StockElementMode.Skip)]
    [InlineData(CheckStockType.PrePrinted, true, PdfSharpCheckRenderer.StockElementMode.Ghost)]
    internal void StockElementMode_IsPrintOnBlank_SkipWhenPrinting_GhostOnSpecimen(
        CheckStockType stockType, bool specimen, PdfSharpCheckRenderer.StockElementMode expected)
    {
        var request = BuildRequest(stockType, specimen);

        PdfSharpCheckRenderer.ModeForStockElements(request).ShouldBe(expected);
    }

    // ── F：预印票纸不再套印 ──────────────────────────────────

    [Fact]
    public void PrePrintedStock_DoesNotRedrawWhatTheStockAlreadyCarries()
    {
        var content = Render(CheckStockType.PrePrinted, specimen: false);
        var texts = TextRuns(content).Select(r => r.Text).ToList();

        // 票纸自带的：一个都不许再打
        texts.ShouldNotContain(BankName);
        texts.ShouldNotContain(ChequeNumber);
        texts.ShouldNotContain(PayToLabel);
        // 金额框也在票纸上（矩形路径少一个）
        CountOf(content, " re\n").ShouldBe(CountOf(Render(CheckStockType.Blank, false), " re\n") - 1);

        // 打印机这一次要补的：一个都不许少
        texts.ShouldContain(Payee);
        texts.ShouldContain(AmountDigits);
        texts.ShouldContain(AmountWords);
    }

    [Fact]
    public void PrePrintedStock_PrintsTheDateValueWithoutTheCaptionTheStockCarries()
    {
        var texts = TextRuns(Render(CheckStockType.PrePrinted, specimen: false)).Select(r => r.Text).ToList();

        // 「Date」字样在票纸上；日期值任何时候都是打印机打的
        texts.ShouldContain(DateValue);
        texts.ShouldNotContain($"Date  {DateValue}");
    }

    // ── H：白纸票纸的产物不变 ────────────────────────────────

    [Fact]
    public void BlankStock_StillDrawsEverything()
    {
        var texts = TextRuns(Render(CheckStockType.Blank, specimen: false)).Select(r => r.Text).ToList();

        // 白纸下没有「票纸自带」这回事，整张票都是打印机打的
        texts.ShouldContain(BankName);
        texts.ShouldContain(ChequeNumber);
        texts.ShouldContain(PayToLabel);
        texts.ShouldContain($"Date  {DateValue}");   // 连日期字样也照打
        texts.ShouldContain(Payee);
        texts.ShouldContain(AmountWords);
    }

    [Fact]
    public void BlankStock_RendersDeterministically()
    {
        // 下面那条「同一次运行内比对坐标」的用例建立在这上面；
        // 内容流若不确定，那条断言的红绿就没有意义。
        Render(CheckStockType.Blank, specimen: false)
            .ShouldBe(Render(CheckStockType.Blank, specimen: false));
    }

    // ── G：留下来的墨迹一格都没动 ────────────────────────────

    [Fact]
    public void EveryRetainedElement_KeepsTheExactPositionItHadOnBlankStock()
    {
        var blank = TextRuns(Render(CheckStockType.Blank, specimen: false));
        var prePrinted = TextRuns(Render(CheckStockType.PrePrinted, specimen: false));

        // ★ 按「第几次出现」配对而不是按文本建字典：同一串文字会画不止一次
        // （两联存根各画一份 "No. 1001   Date …"），建字典会直接撞键。
        var blankByText = blank.GroupBy(r => r.Text).ToDictionary(g => g.Key, g => g.ToList());
        var comparedRuns = 0;

        foreach (var group in prePrinted.GroupBy(r => r.Text))
        {
            if (!blankByText.TryGetValue(group.Key, out var before))
                continue;   // 这一串只在预印下画（如去掉字样后的日期值），另有用例管它

            var after = group.ToList();
            after.Count.ShouldBe(before.Count, $"'{group.Key}' is drawn a different number of times");

            for (var i = 0; i < after.Count; i++)
            {
                after[i].X.ShouldBe(before[i].X, 0.01,
                    $"'{group.Key}' #{i} moved horizontally; someone calibrated a printer against the old position");
                after[i].Y.ShouldBe(before[i].Y, 0.01, $"'{group.Key}' #{i} moved vertically");
                comparedRuns++;
            }
        }

        // 先证明这次比对不是空转：两边真的有一批共同的绘制被逐一比过
        comparedRuns.ShouldBeGreaterThan(5);
    }

    // ── I：样张看得出票纸差别 ────────────────────────────────

    [Fact]
    public void PrePrintedSpecimen_ShowsTheStockElementsFaded()
    {
        var specimen = Render(CheckStockType.PrePrinted, specimen: true);
        var printed = Render(CheckStockType.PrePrinted, specimen: false);
        var blank = Render(CheckStockType.Blank, specimen: false);

        // 样张把票纸自带的元素画回来（否则看不出预印与白纸的差别）……
        var texts = TextRuns(specimen).Select(r => r.Text).ToList();
        texts.ShouldContain(BankName);
        texts.ShouldContain(ChequeNumber);
        texts.ShouldContain(PayToLabel);

        // ……但用淡灰画，与打印机现打的黑色区分开。灰色填充色是真打印与白纸都没有的操作符。
        const string ghostFill = "0.745 0.745 0.745 rg";
        specimen.ShouldContain(ghostFill);
        printed.ShouldNotContain(ghostFill);
        blank.ShouldNotContain(ghostFill);
    }

    [Fact]
    public void BlankSpecimen_HasNothingToFade()
    {
        // 白纸下整张票都是打印机打的，没有「票纸自带」的东西可淡显 ——
        // 样张与真打印的产物因此完全一致。
        Render(CheckStockType.Blank, specimen: true)
            .ShouldBe(Render(CheckStockType.Blank, specimen: false));
    }

    // ── 内容流解析 ───────────────────────────────────────────

    private sealed record TextRun(string Text, double X, double Y);

    private static readonly Regex TextOp = new(
        @"(?<dx>-?[\d.]+)\s+(?<dy>-?[\d.]+)\s+Td|\((?<text>[^)]*)\)\s*Tj",
        RegexOptions.Compiled);

    /// <summary>
    /// 从内容流里取出「画了什么字、画在哪」。
    /// </summary>
    /// <remarks>
    /// <c>Td</c> 是相对上一行原点的位移，且每个 <c>BT</c> 把原点复位，
    /// 所以必须逐段累加才能得到绝对坐标 —— 直接比 <c>Td</c> 的数值会因为
    /// 前面少画了一个元素而全盘对不上。
    /// </remarks>
    private static List<TextRun> TextRuns(string content)
    {
        var runs = new List<TextRun>();
        foreach (var block in content.Split("BT").Skip(1))
        {
            var segment = block.Split("ET")[0];
            double x = 0, y = 0;
            foreach (Match m in TextOp.Matches(segment))
            {
                if (m.Groups["text"].Success)
                    runs.Add(new TextRun(m.Groups["text"].Value, x, y));
                else
                {
                    x += double.Parse(m.Groups["dx"].Value, CultureInfo.InvariantCulture);
                    y += double.Parse(m.Groups["dy"].Value, CultureInfo.InvariantCulture);
                }
            }
        }
        return runs;
    }

    private static int CountOf(string haystack, string needle)
        => haystack.Split(needle).Length - 1;

    /// <summary>
    /// 充当 MICR 字体的系统 TrueType 文件：借 <see cref="FinanceFontResolver"/> 探测到的 sans。
    /// 它在哪个操作系统上找得到，这里就在哪个操作系统上找得到；找不到时渲染本身早已因
    /// 缺 sans 字体而失败，故这里直接给出可读的原因而不是让六条用例各报一次 <c>Succeeded</c> 为假。
    /// </summary>
    private static readonly string MicrStandInFontPath =
        FinanceFontResolver.EnsureInstalled()
        ?? throw new InvalidOperationException("No system TrueType font is available to stand in for the MICR face.");

    private static string Render(CheckStockType stockType, bool specimen)
    {
        var renderer = new PdfSharpCheckRenderer(NullLogger<PdfSharpCheckRenderer>.Instance);
        var result = renderer.Render(BuildRequest(stockType, specimen));
        result.Succeeded.ShouldBeTrue(result.Message);

        using var input = new MemoryStream(result.Data!);
        using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        return Encoding.Latin1.GetString(doc.Pages[0].Contents.CreateSingleContent().Stream.UnfilteredValue);
    }

    private static CheckRenderRequest BuildRequest(CheckStockType stockType, bool specimen) => new()
    {
        Layout = CheckLayout.Voucher,
        StockType = stockType,
        IsSpecimen = specimen,
        IsPreview = specimen,
        Scheme = BankNumberScheme.UsAba,
        BankName = BankName,
        AccountName = "Operating account",
        RoutingNumber = "021000021",
        // 白纸票纸要现打磁码行，故必须有 E-13B 字体才渲染得出来。这里借渲染器自己探测到的那款
        // 系统 sans 顶替：本组测的是「画了什么、画在哪」，不是磁码字形对不对。
        // ★ 不能写死 Windows 路径：CI 跑在 Linux 上，`C:\Windows\Fonts\consola.ttf` 不存在
        // ⇒ TryLoadMicr 失败 ⇒ 六条白纸用例在 CI 全红而本机全绿（09-02 → 09-04 实发）。
        MicrFontPath = MicrStandInFontPath,
        AccountNumberPlain = stockType == CheckStockType.Blank ? "000123456" : null,
        Checks =
        [
            new CheckRenderItem
            {
                CheckNumber = 1001,
                PayeeName = Payee,
                Amount = 1234.56m,
                Currency = "USD",
                AmountInWords = AmountWords,
                IssueDate = new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc),
                Memo = "August retainer"
            }
        ]
    };
}
