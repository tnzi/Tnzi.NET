using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Tnzi.Finance.Documents.Services;

/// <summary>
/// 默认支票 PDF 渲染器（PDFsharp 6.x，纯托管，MIT）
/// </summary>
/// <remarks>
/// 版式：<see cref="CheckLayout.Voucher"/>（票 + 两联存根）/<see cref="CheckLayout.ThreePerPage"/>（每页三票）。
/// 预印票纸不打 MICR；白纸全打印需配置 E-13B 字体（缺失返回 400）。全票面按 OffsetXMm/OffsetYMm 平移校准。
/// 通过 <see cref="Internal.FinanceFontResolver"/> 解析进程级字体（无内嵌字体，从系统字体目录解析常规 sans）。
/// MICR 行拼装复用 Finance 核心的 <c>MicrLineComposer</c>（internal，经 InternalsVisibleTo 可见）。
/// </remarks>
public class PdfSharpCheckRenderer : ICheckDocumentRenderer
{
    private const double MmToPt = 72.0 / 25.4;
    private const double PageWidth = 612.0;   // US Letter 8.5in
    private const double PageHeight = 792.0;  // US Letter 11in
    private const double Margin = 36.0;

    private readonly ILogger<PdfSharpCheckRenderer> _logger;

    public PdfSharpCheckRenderer(ILogger<PdfSharpCheckRenderer> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public Result<byte[]> Render(CheckRenderRequest request)
    {
        Check.NotNull(request);
        if (request.Checks.Count == 0)
            return Result<byte[]>.Failure("No checks to render.", 400);

        var prep = PrepareFonts(request);
        if (!prep.Succeeded)
            return Result<byte[]>.Failure(prep.Message!, prep.Code ?? 400);

        try
        {
            using var document = new PdfDocument();
            var perPage = request.Layout == CheckLayout.ThreePerPage ? 3 : 1;

            for (var i = 0; i < request.Checks.Count; i += perPage)
            {
                var page = NewPage(document);
                using var gfx = XGraphics.FromPdfPage(page);
                ApplyOffset(gfx, request);

                if (request.Layout == CheckLayout.ThreePerPage)
                {
                    var slotHeight = (PageHeight - 2 * Margin) / 3.0;
                    for (var slot = 0; slot < perPage && i + slot < request.Checks.Count; slot++)
                    {
                        var top = Margin + slot * slotHeight;
                        DrawCheck(gfx, request, request.Checks[i + slot], top, slotHeight);
                    }
                }
                else
                {
                    var checkHeight = (PageHeight - 2 * Margin) / 3.0;
                    DrawCheck(gfx, request, request.Checks[i], Margin, checkHeight);
                    DrawStub(gfx, request, request.Checks[i], Margin + checkHeight + 8, "Voucher copy 1");
                    DrawStub(gfx, request, request.Checks[i], Margin + 2 * checkHeight + 16, "Voucher copy 2");
                }
            }

            return Result<byte[]>.Success(ToBytes(document));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render check PDF.");
            return Result<byte[]>.Failure($"Check rendering failed: {ex.Message}", 500);
        }
    }

    public Result<byte[]> RenderCalibration(CheckRenderRequest request)
    {
        Check.NotNull(request);
        if (!Internal.FinanceFontResolver.HasSansFont)
            return NoFontFailure();

        try
        {
            using var document = new PdfDocument();
            var page = NewPage(document);
            using var gfx = XGraphics.FromPdfPage(page);
            ApplyOffset(gfx, request);

            var font = Sans(8);
            var titleFont = Sans(12, XFontStyleEx.Bold);
            gfx.DrawString("Check alignment calibration", titleFont, XBrushes.Black,
                new XRect(Margin, Margin, PageWidth - 2 * Margin, 20), XStringFormats.TopLeft);
            gfx.DrawString($"Layout: {request.Layout}  OffsetX: {request.OffsetXMm}mm  OffsetY: {request.OffsetYMm}mm",
                font, XBrushes.Black, new XRect(Margin, Margin + 18, PageWidth - 2 * Margin, 16), XStringFormats.TopLeft);

            // 每 10mm 一条刻度线（水平 + 垂直），标注毫米数
            for (var mm = 0; mm * MmToPt < PageHeight; mm += 10)
            {
                var y = mm * MmToPt;
                gfx.DrawLine(XPens.LightGray, 0, y, PageWidth, y);
                gfx.DrawString($"{mm}", font, XBrushes.Gray, new XPoint(2, y + 8));
            }
            for (var mm = 0; mm * MmToPt < PageWidth; mm += 10)
            {
                var x = mm * MmToPt;
                gfx.DrawLine(XPens.LightGray, x, 0, x, PageHeight);
                gfx.DrawString($"{mm}", font, XBrushes.Gray, new XPoint(x + 1, 10));
            }

            return Result<byte[]>.Success(ToBytes(document));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render calibration PDF.");
            return Result<byte[]>.Failure($"Calibration rendering failed: {ex.Message}", 500);
        }
    }

    /// <summary>安装字体解析器并按票纸类型确保必要字体可用。</summary>
    private Result PrepareFonts(CheckRenderRequest request)
    {
        Internal.FinanceFontResolver.EnsureInstalled(_logger);
        if (!Internal.FinanceFontResolver.HasSansFont)
            return Result.Failure("No system sans font is available for check rendering. Install a TrueType font or configure a font path.", 500);

        if (request.StockType == CheckStockType.Blank)
        {
            if (!Internal.FinanceFontResolver.TryLoadMicr(request.MicrFontPath))
                return Result.Failure("Blank check stock requires an E-13B MICR font. Configure Finance:CheckMicrFontPath with a valid font file.", 400);

            // ★ 有字体文件不等于画得出磁码：PDFsharp 的字体解析器是**进程级单例**，
            // 若在我们之后又有别人装了自己的，MICR 族会被它当未知族回退成常规 sans ——
            // 磁码行印成普通字形，屏幕与纸面都看不出异常，只有银行的读头认不出来。
            if (!Internal.FinanceFontResolver.OwnsProcessResolver)
            {
                return Result.Failure(
                    "Another component replaced the process-wide PDF font resolver, so the E-13B MICR line cannot be rendered. Print on pre-printed stock, or keep check rendering as the last component to install a font resolver.", 500);
            }
        }

        return Result.Success();
    }

    private static Result<byte[]> NoFontFailure()
        => Result<byte[]>.Failure("No system sans font is available for check rendering. Install a TrueType font or configure a font path.", 500);

    /// <summary>新建一页（版式与偏移不改变纸张：一律 US Letter，偏移经 <see cref="ApplyOffset"/> 平移）。</summary>
    private static PdfPage NewPage(PdfDocument document)
    {
        var page = document.AddPage();
        page.Size = PageSize.Letter;
        return page;
    }

    private static void ApplyOffset(XGraphics gfx, CheckRenderRequest request)
    {
        var dx = (double)request.OffsetXMm * MmToPt;
        var dy = (double)request.OffsetYMm * MmToPt;
        if (dx != 0 || dy != 0)
            gfx.TranslateTransform(dx, dy);
    }

    /// <summary>
    /// 「票纸自带」的元素这一次该怎么画。
    /// </summary>
    /// <remarks>
    /// 哪些元素算票纸自带，以 HTML 模板里挂 <c>noprint</c> 的那一组为准 —— 两条渲染路径
    /// 对同一个语义必须给出同一个答案。
    /// </remarks>
    internal enum StockElementMode
    {
        /// <summary>白纸票纸：打印机把整张票都打上去（与本机制引入前逐字相同）。</summary>
        Print,

        /// <summary>预印票纸的真打印：票纸上已经有了，再打一遍就是套印重影。</summary>
        Skip,

        /// <summary>样张：淡显，让人看出哪些是票纸自带的（对应 HTML 的 <c>.specimen .noprint</c>）。</summary>
        Ghost
    }

    /// <summary>预印元素在样张里的淡显色（对应 HTML 那条 <c>opacity: .28</c>）。</summary>
    private static readonly XColor GhostColor = XColor.FromArgb(190, 190, 190);
    private static readonly XBrush GhostBrush = new XSolidBrush(GhostColor);
    private static readonly XPen GhostPen = new(GhostColor);

    /// <summary>
    /// 决定「票纸自带」元素的画法。<b>白纸恒为 <see cref="StockElementMode.Print"/></b> ——
    /// 这正是白纸产物与本机制引入前逐字相同的原因（守卫恒真，且传下去的仍是同一个
    /// <c>XBrushes.Black</c>）。
    /// </summary>
    internal static StockElementMode ModeForStockElements(CheckRenderRequest request)
    {
        Check.NotNull(request);
        if (request.StockType != CheckStockType.PrePrinted)
            return StockElementMode.Print;
        return request.IsSpecimen ? StockElementMode.Ghost : StockElementMode.Skip;
    }

    private void DrawCheck(XGraphics gfx, CheckRenderRequest request, CheckRenderItem item, double top, double height)
    {
        var left = Margin;
        var right = PageWidth - Margin;
        var width = right - left;

        var bold = Sans(10, XFontStyleEx.Bold);
        var normal = Sans(9);
        var small = Sans(8);

        // 预印票纸：抬头 / 银行标识 / 支票号 / "PAY TO THE ORDER OF" / 金额框都已经印在纸上，
        // 再画一遍就是套印重影。白纸下三个变量恒等于改动前的字面量，故产物逐字不变。
        var stockMode = ModeForStockElements(request);
        var drawStock = stockMode != StockElementMode.Skip;
        var stockBrush = stockMode == StockElementMode.Ghost ? GhostBrush : XBrushes.Black;
        var stockPen = stockMode == StockElementMode.Ghost ? GhostPen : XPens.Black;

        // ★ 票面的号与磁码行的串行号取自同一个字符串（理由同模板路径：算两次就有分道扬镳的机会）
        var checkNumberText = CheckNumberFormat.Format(item.CheckNumber, request.CheckNumberDigits);

        // 表头：银行名（左）+ 支票号（右）
        if (drawStock)
        {
            gfx.DrawString(request.BankName ?? request.AccountName ?? string.Empty, bold, stockBrush,
                new XRect(left, top, width * 0.6, 14), XStringFormats.TopLeft);
            gfx.DrawString($"No. {checkNumberText}", bold, stockBrush,
                new XRect(right - 140, top, 140, 14), XStringFormats.TopRight);
        }

        // 日期（右）。★ 票纸自带的是「Date」这个字样，日期值任何时候都是打印机打的。
        // 预印时只画值，**沿用同一个矩形且仍然右对齐** —— 右对齐文本的右缘由矩形决定，
        // 与字符串长度无关，所以日期数字落在与改动前完全相同的位置。
        var dateRect = new XRect(right - 180, top + 20, 180, 14);
        if (drawStock)
            gfx.DrawString($"Date  {item.IssueDate:yyyy-MM-dd}", normal, stockBrush, dateRect, XStringFormats.TopRight);
        else
            gfx.DrawString($"{item.IssueDate:yyyy-MM-dd}", normal, XBrushes.Black, dateRect, XStringFormats.TopRight);

        // 收款人 + 金额数字框
        var payLine = top + 42;
        if (drawStock)
            gfx.DrawString("Pay to the order of", small, stockBrush, new XPoint(left, payLine - 2));
        gfx.DrawString(item.PayeeName ?? string.Empty, normal, XBrushes.Black,
            new XRect(left + 110, payLine - 12, width - 110 - 120, 14), XStringFormats.TopLeft);
        if (drawStock)
            gfx.DrawRectangle(stockPen, right - 110, payLine - 14, 110, 18);
        gfx.DrawString($"**{item.Amount.ToString("N2", CultureInfo.InvariantCulture)}", bold, XBrushes.Black,
            new XRect(right - 106, payLine - 12, 102, 14), XStringFormats.TopRight);

        // 金额大写行
        var wordsLine = payLine + 22;
        gfx.DrawString(item.AmountInWords, normal, XBrushes.Black,
            new XRect(left, wordsLine - 12, width - 40, 14), XStringFormats.TopLeft);
        gfx.DrawLine(XPens.Black, left, wordsLine + 4, right, wordsLine + 4);

        // 摘要（左下）。★ 这里的 "Memo" 字样也在票纸上，但**刻意不拆**：
        // 它与摘要值被烘在同一个左对齐字符串里，拆开会让摘要值往左挪到票纸自己的
        // "Memo" 上面去 —— 而「已经画的元素坐标一格不许动」是本轮的硬约束。
        // 日期那处能拆是因为它右对齐，右缘由矩形决定、与字符串长度无关。
        // 残留代价：预印票纸上 "Memo" 这一个词会与票纸上的重叠一次。
        if (!string.IsNullOrWhiteSpace(item.Memo))
            gfx.DrawString($"Memo  {item.Memo}", small, XBrushes.Black,
                new XRect(left, top + height - 40, width * 0.6, 12), XStringFormats.TopLeft);

        // MICR 行（仅白纸；预印票纸已印）
        if (request.StockType == CheckStockType.Blank && !string.IsNullOrWhiteSpace(request.AccountNumberPlain))
        {
            var micr = MicrLineComposer.Compose(request.Scheme, checkNumberText,
                request.RoutingNumber, request.InstitutionNumber, request.TransitNumber, request.AccountNumberPlain!);
            var micrFont = new XFont(Internal.FinanceFontResolver.MicrFamily, 12);
            gfx.DrawString(MicrLineComposer.ToFontGlyphs(micr), micrFont, XBrushes.Black,
                new XRect(left, top + height - 18, width, 14), XStringFormats.BottomLeft);
        }

        // 区域分隔线
        gfx.DrawLine(XPens.Gainsboro, left, top + height, right, top + height);
    }

    private void DrawStub(XGraphics gfx, CheckRenderRequest request, CheckRenderItem item, double top, string label)
    {
        var left = Margin;
        var right = PageWidth - Margin;
        var small = Sans(8);
        var bold = Sans(9, XFontStyleEx.Bold);

        gfx.DrawString(label, bold, XBrushes.Gray, new XPoint(left, top));
        gfx.DrawString($"No. {CheckNumberFormat.Format(item.CheckNumber, request.CheckNumberDigits)}   Date {item.IssueDate:yyyy-MM-dd}",
            small, XBrushes.Black, new XPoint(left, top + 16));
        gfx.DrawString($"Payee: {item.PayeeName}", small, XBrushes.Black, new XPoint(left, top + 30));
        gfx.DrawString($"Amount: {item.Amount.ToString("N2", CultureInfo.InvariantCulture)} {item.Currency}", small, XBrushes.Black, new XPoint(left, top + 44));
        if (!string.IsNullOrWhiteSpace(item.Memo))
            gfx.DrawString($"Memo: {item.Memo}", small, XBrushes.Black, new XPoint(left, top + 58));

        // 消费应用附加的存根行，接在固定行之后 —— 没有附加行时下面这段整个不执行，
        // 分隔线仍画在 top + 70，PDF 输出与从前逐字相同。
        var y = top + 72;
        foreach (var line in item.StubLines)
        {
            gfx.DrawString(
                string.IsNullOrWhiteSpace(line.Value) ? line.Label : $"{line.Label}: {line.Value}",
                small, XBrushes.Black, new XPoint(left, y));
            y += 12;
        }

        gfx.DrawLine(XPens.Gainsboro, left, Math.Max(top + 70, y - 4), right, Math.Max(top + 70, y - 4));
    }

    private static XFont Sans(double size, XFontStyleEx style = XFontStyleEx.Regular)
        => new(Internal.FinanceFontResolver.SansFamily, size, style);

    private static byte[] ToBytes(PdfDocument document)
    {
        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }
}
