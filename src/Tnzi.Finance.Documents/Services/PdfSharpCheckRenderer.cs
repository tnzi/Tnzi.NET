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

    private void DrawCheck(XGraphics gfx, CheckRenderRequest request, CheckRenderItem item, double top, double height)
    {
        var left = Margin;
        var right = PageWidth - Margin;
        var width = right - left;

        var bold = Sans(10, XFontStyleEx.Bold);
        var normal = Sans(9);
        var small = Sans(8);

        // 表头：银行名（左）+ 支票号（右）
        gfx.DrawString(request.BankName ?? request.AccountName ?? string.Empty, bold, XBrushes.Black,
            new XRect(left, top, width * 0.6, 14), XStringFormats.TopLeft);
        gfx.DrawString($"No. {item.CheckNumber}", bold, XBrushes.Black,
            new XRect(right - 140, top, 140, 14), XStringFormats.TopRight);

        // 日期（右）
        gfx.DrawString($"Date  {item.IssueDate:yyyy-MM-dd}", normal, XBrushes.Black,
            new XRect(right - 180, top + 20, 180, 14), XStringFormats.TopRight);

        // 收款人 + 金额数字框
        var payLine = top + 42;
        gfx.DrawString("Pay to the order of", small, XBrushes.Black, new XPoint(left, payLine - 2));
        gfx.DrawString(item.PayeeName ?? string.Empty, normal, XBrushes.Black,
            new XRect(left + 110, payLine - 12, width - 110 - 120, 14), XStringFormats.TopLeft);
        gfx.DrawRectangle(XPens.Black, right - 110, payLine - 14, 110, 18);
        gfx.DrawString($"**{item.Amount.ToString("N2", CultureInfo.InvariantCulture)}", bold, XBrushes.Black,
            new XRect(right - 106, payLine - 12, 102, 14), XStringFormats.TopRight);

        // 金额大写行
        var wordsLine = payLine + 22;
        gfx.DrawString(item.AmountInWords, normal, XBrushes.Black,
            new XRect(left, wordsLine - 12, width - 40, 14), XStringFormats.TopLeft);
        gfx.DrawLine(XPens.Black, left, wordsLine + 4, right, wordsLine + 4);

        // 摘要（左下）
        if (!string.IsNullOrWhiteSpace(item.Memo))
            gfx.DrawString($"Memo  {item.Memo}", small, XBrushes.Black,
                new XRect(left, top + height - 40, width * 0.6, 12), XStringFormats.TopLeft);

        // MICR 行（仅白纸；预印票纸已印）
        if (request.StockType == CheckStockType.Blank && !string.IsNullOrWhiteSpace(request.AccountNumberPlain))
        {
            var micr = MicrLineComposer.Compose(request.Scheme, item.CheckNumber,
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
        gfx.DrawString($"No. {item.CheckNumber}   Date {item.IssueDate:yyyy-MM-dd}", small, XBrushes.Black, new XPoint(left, top + 16));
        gfx.DrawString($"Payee: {item.PayeeName}", small, XBrushes.Black, new XPoint(left, top + 30));
        gfx.DrawString($"Amount: {item.Amount.ToString("N2", CultureInfo.InvariantCulture)} {item.Currency}", small, XBrushes.Black, new XPoint(left, top + 44));
        if (!string.IsNullOrWhiteSpace(item.Memo))
            gfx.DrawString($"Memo: {item.Memo}", small, XBrushes.Black, new XPoint(left, top + 58));
        gfx.DrawLine(XPens.Gainsboro, left, top + 70, right, top + 70);
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
