using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Tnzi.Documents.Services;

/// <summary>
/// 默认的 PDF 盖章实现（PDFsharp 6.x，纯托管、MIT）。
/// </summary>
/// <remarks>
/// <para><b>刻意不用 AcroForm 表单域。</b> PDFsharp 对表单压平的支持长期薄弱，而签署场景要的
/// 恰恰是「盖完就不能再改」。直接把文本与图片画进页面内容层，输出天然就是压平的，
/// 也不必依赖任何一方的表单实现是否完整。</para>
/// <para><b>坐标换算只缩放、不翻 Y。</b> PDFsharp 的 <see cref="XGraphics"/> 原点已经在左上角、
/// Y 轴向下，与本包的归一化坐标同向；PDF 原生的左下角原点只在**读**的那一侧出现。
/// 在这里再翻一次 Y 是最容易犯的错，<c>NormalizedCoordinateTests</c> 把这条钉死。</para>
/// <para>文本不自动换行（<see cref="XGraphics.DrawString(string, XFont, XBrush, XRect, XStringFormat)"/>
/// 的语义），需要多行由调用方拆成多个 <see cref="PdfTextStamp"/>，宽度按 <see cref="MeasureTextWidth"/> 量；
/// 量与画共用 <see cref="CreateFont"/>，所以量出来的就是画出来的。页面 <c>/Rotate</c> 不做补偿。</para>
/// <para><b>字体画不出的字符拒绝，不画成空框。</b> 默认字体解析器只找拉丁系的系统 sans（见 <c>DocumentFontResolver</c>），
/// 中日韩等文字在其中没有字形，PDFsharp 会一声不响地画成 .notdef 方框：对签署而言那是一份带着方框、
/// 却已算好哈希出了证书的成品。盖章前逐字符核对字形覆盖，缺字形时抛 <see cref="PdfDocumentException"/>。
/// 要画这些文字，在第一次盖章之前装一个把 <c>TnziDocumentSans</c> 解析到覆盖该文字的字体的
/// <see cref="GlobalFontSettings.FontResolver"/>（本包只在没人装过时才装自己的）。</para>
/// </remarks>
public sealed class PdfSharpPdfStamper : IPdfStamper
{
    /// <summary>文本框高度为 0 时按字号的多少倍补一行高。</summary>
    private const double LineHeightFactor = 1.4d;

    /// <summary>字号不大于 0 时的默认字号（量与画同一个值）。</summary>
    private const double DefaultFontSize = 10d;

    /// <summary>量字用的虚拟画布边长（pt）。量的结果与画布大小无关，只是 PDFsharp 要一个尺寸。</summary>
    private const double MeasureCanvasPoints = 1000d;

    private readonly ILogger<PdfSharpPdfStamper> _logger;

    /// <summary>初始化一个 <see cref="PdfSharpPdfStamper"/> 实例。</summary>
    /// <param name="logger">日志。</param>
    public PdfSharpPdfStamper(ILogger<PdfSharpPdfStamper> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public byte[] Stamp(byte[] pdf, PdfStampRequest request)
    {
        Check.NotNull(pdf);
        Check.NotNull(request);

        if (pdf.Length == 0)
            throw new PdfDocumentException("The PDF byte array is empty.");

        // 只有真要画文字时才要求字体：只画图片 / 只追加空页在无系统字体的环境（精简容器）也能跑。
        RequireFontIfDrawingText(request);

        using var input = new MemoryStream(pdf, writable: false);
        using var document = OpenForModify(input);
        return Build(document, request);
    }

    /// <inheritdoc />
    public byte[] Create(PdfStampRequest request)
    {
        Check.NotNull(request);

        if (request.AppendPages.Count == 0)
        {
            throw new ArgumentException(
                "Create needs at least one page in AppendPages; a zero-page PDF is not a valid document.",
                nameof(request));
        }

        RequireFontIfDrawingText(request);

        using var document = new PdfDocument();
        return Build(document, request);
    }

    /// <inheritdoc />
    public double MeasureTextWidth(string text, double fontSize, bool bold = false, bool italic = false)
    {
        Check.NotNull(text);

        if (text.Length == 0)
            return 0d;

        RequireFont();

        using var context = XGraphics.CreateMeasureContext(
            new XSize(MeasureCanvasPoints, MeasureCanvasPoints),
            XGraphicsUnit.Point,
            XPageDirection.Downwards);
        return context.MeasureString(text, CreateFont(fontSize, bold, italic)).Width;
    }

    /// <summary>造页 → 绘制 → 保存。<c>Stamp</c> 与 <c>Create</c> 只在「文档从哪来」上不同。</summary>
    private byte[] Build(PdfDocument document, PdfStampRequest request)
    {
        AppendPages(document, request.AppendPages);

        // XImage 要活到 Save 之后（PDFsharp 保存时才落图像数据），所以统一延后释放。
        var pending = new List<IDisposable>();
        try
        {
            DrawStamps(document, request.Stamps, pending);

            using var output = new MemoryStream();
            document.Save(output);
            return output.ToArray();
        }
        finally
        {
            foreach (var disposable in pending)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // 已释放，忽略
                }
            }
        }
    }

    /// <summary>只有真要画文字时才要求字体（见 <see cref="Stamp"/> 里的同款判断），并核对每段文字的字形覆盖。</summary>
    private void RequireFontIfDrawingText(PdfStampRequest request)
    {
        if (!request.Stamps.Any(stamp => stamp is PdfTextStamp))
            return;

        RequireFont();

        foreach (var text in request.Stamps.OfType<PdfTextStamp>())
            RequireGlyphCoverage(text);
    }

    /// <summary>
    /// 解析到的字体必须画得出这段文字的每一个可见字符；缺字形的字符 PDFsharp 会画成 .notdef 方框而不报任何错。
    /// </summary>
    /// <remarks>
    /// 字形查的是与 <see cref="DrawText"/> 同一个 <see cref="XFont"/>（同一份字体解析结果），查到字形号 0 即缺。
    /// 控制字符与格式字符（换行、制表、零宽连接符等）不画任何东西，不参与核对。
    /// 异常消息里不放字体文件路径（它会被转述到调用方），路径记在日志里。
    /// </remarks>
    private void RequireGlyphCoverage(PdfTextStamp stamp)
    {
        if (string.IsNullOrEmpty(stamp.Text))
            return;

        var font = CreateFont(stamp.FontSize, stamp.Bold, stamp.Italic);
        foreach (var pair in GlyphHelper.GlyphIndicesFromString(stamp.Text, font))
        {
            if (pair.GlyphIndex != 0 || IsInvisible(pair.CodePoint))
                continue;

            var character = char.ConvertFromUtf32(pair.CodePoint);
            _logger.LogWarning(
                "Refusing to stamp text: the character U+{CodePoint:X4} has no glyph in the resolved font ({FontPath}).",
                pair.CodePoint, DocumentFontResolver.ResolvedFontPath ?? "a font resolver installed by another component");
            throw new PdfDocumentException(
                $"The text to stamp contains the character '{character}' (U+{pair.CodePoint:X4}), which the resolved font cannot draw; "
                + "it would print as an empty box. Install a PDFsharp font resolver that maps the document font to a font covering this script "
                + "before the first stamp, or remove the character.");
        }
    }

    private static bool IsInvisible(int codePoint)
    {
        if (codePoint is < 0 or > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF)
            return false;

        var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
        return category is UnicodeCategory.Control or UnicodeCategory.Format;
    }

    private void RequireFont()
    {
        if (!DocumentFontResolver.EnsureReady(_logger))
        {
            throw new PdfDocumentException(
                "No usable system font was found, so text cannot be drawn on the PDF. " +
                "Install a sans-serif font (for example DejaVu or Liberation) in the runtime image.");
        }
    }

    /// <summary>
    /// 盖章文字用的字体。<see cref="DrawText"/> 与 <see cref="MeasureTextWidth"/> 共用这一处：
    /// 两边各建一份，迟早会出现「量的是常规体、画的是粗体」，量出来的宽度就不再是画出来的宽度。
    /// </summary>
    private static XFont CreateFont(double fontSize, bool bold, bool italic)
    {
        var style = XFontStyleEx.Regular;
        if (bold)
            style |= XFontStyleEx.Bold;
        if (italic)
            style |= XFontStyleEx.Italic;

        return new XFont(DocumentFontResolver.FamilyName, EffectiveFontSize(fontSize), style);
    }

    private static double EffectiveFontSize(double fontSize) => fontSize > 0 ? fontSize : DefaultFontSize;

    private static PdfDocument OpenForModify(Stream input)
    {
        try
        {
            return PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        }
        catch (Exception ex) when (ex is not TnziException)
        {
            throw new PdfDocumentException($"The byte array is not a writable PDF: {ex.Message}", ex);
        }
    }

    private static void AppendPages(PdfDocument document, IReadOnlyList<PdfPageSpec> specs)
    {
        if (specs.Count == 0)
            return;

        // 默认尺寸取「追加之前」的最后一页，追加多页时不会逐页漂移
        var fallbackWidth = document.PageCount > 0 ? document.Pages[document.PageCount - 1].Width.Point : XUnit.FromPoint(612).Point;
        var fallbackHeight = document.PageCount > 0 ? document.Pages[document.PageCount - 1].Height.Point : XUnit.FromPoint(792).Point;

        foreach (var spec in specs)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(spec.WidthPoints is > 0 ? spec.WidthPoints.Value : fallbackWidth);
            page.Height = XUnit.FromPoint(spec.HeightPoints is > 0 ? spec.HeightPoints.Value : fallbackHeight);
        }
    }

    private void DrawStamps(PdfDocument document, IReadOnlyList<PdfStamp> stamps, List<IDisposable> pending)
    {
        if (stamps.Count == 0)
            return;

        // 按页分组开一次 XGraphics；GroupBy 保序，所以同页内的叠放顺序仍是请求里的顺序。
        foreach (var group in stamps.GroupBy(stamp => stamp.PageNumber))
        {
            if (group.Key < 1 || group.Key > document.PageCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stamps),
                    group.Key,
                    $"Page number is out of range; the document has {document.PageCount} page(s) after appending.");
            }

            var page = document.Pages[group.Key - 1];

            // Append = 画在既有内容之上，不覆盖原页内容
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var pageWidth = graphics.PageSize.Width;
            var pageHeight = graphics.PageSize.Height;

            foreach (var stamp in group)
            {
                switch (stamp)
                {
                    case PdfTextStamp text:
                        DrawText(graphics, text, pageWidth, pageHeight);
                        break;
                    case PdfImageStamp image:
                        DrawImage(graphics, image, pageWidth, pageHeight, pending);
                        break;
                    default:
                        _logger.LogWarning("Ignoring unsupported stamp type '{StampType}'.", stamp.GetType().Name);
                        break;
                }
            }
        }
    }

    private static void DrawText(XGraphics graphics, PdfTextStamp stamp, double pageWidth, double pageHeight)
    {
        if (string.IsNullOrEmpty(stamp.Text))
            return;

        var font = CreateFont(stamp.FontSize, stamp.Bold, stamp.Italic);
        var rect = NormalizedCoordinates.ToPageRect(stamp.Rect, pageWidth, pageHeight, EffectiveFontSize(stamp.FontSize) * LineHeightFactor);

        graphics.DrawString(stamp.Text, font, ResolveBrush(stamp.Color), rect, ResolveFormat(stamp.Alignment));
    }

    private static void DrawImage(XGraphics graphics, PdfImageStamp stamp, double pageWidth, double pageHeight, List<IDisposable> pending)
    {
        if (!ImagePayload.TryResolve(stamp, out var bytes))
        {
            throw new PdfDocumentException(
                "The image stamp carries neither raw content nor a decodable data URL / base64 payload.");
        }

        var stream = new MemoryStream(bytes, writable: false);
        pending.Add(stream);

        XImage image;
        try
        {
            image = XImage.FromStream(stream);
        }
        catch (Exception ex) when (ex is not TnziException)
        {
            throw new PdfDocumentException($"The stamp image could not be decoded (PNG and JPEG are supported): {ex.Message}", ex);
        }

        pending.Add(image);

        var rect = NormalizedCoordinates.ToPageRect(stamp.Rect, pageWidth, pageHeight, 0d);
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new PdfDocumentException("An image stamp needs a rectangle with both width and height greater than zero.");

        graphics.DrawImage(image, stamp.PreserveAspectRatio ? Contain(image, rect) : rect);
    }

    /// <summary>按 contain 语义把图片等比缩放进矩形并居中。</summary>
    private static XRect Contain(XImage image, XRect rect)
    {
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
            return rect;

        var scale = Math.Min(rect.Width / image.PixelWidth, rect.Height / image.PixelHeight);
        var width = image.PixelWidth * scale;
        var height = image.PixelHeight * scale;

        return new XRect(
            rect.X + ((rect.Width - width) / 2d),
            rect.Y + ((rect.Height - height) / 2d),
            width,
            height);
    }

    private static XBrush ResolveBrush(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return XBrushes.Black;

        var value = color.Trim().TrimStart('#');
        if (value.Length == 6 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return new XSolidBrush(XColor.FromArgb((int)((rgb >> 16) & 0xFF), (int)((rgb >> 8) & 0xFF), (int)(rgb & 0xFF)));

        if (value.Length == 8 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return new XSolidBrush(XColor.FromArgb(
                (int)((argb >> 24) & 0xFF),
                (int)((argb >> 16) & 0xFF),
                (int)((argb >> 8) & 0xFF),
                (int)(argb & 0xFF)));
        }

        // 颜色写错不该让整次盖章失败：退回黑色，签署内容仍然落纸
        return XBrushes.Black;
    }

    private static XStringFormat ResolveFormat(PdfStampAlignment alignment) => alignment switch
    {
        PdfStampAlignment.TopLeft => XStringFormats.TopLeft,
        PdfStampAlignment.TopCenter => XStringFormats.TopCenter,
        PdfStampAlignment.TopRight => XStringFormats.TopRight,
        PdfStampAlignment.CenterLeft => XStringFormats.CenterLeft,
        PdfStampAlignment.Center => XStringFormats.Center,
        PdfStampAlignment.CenterRight => XStringFormats.CenterRight,
        PdfStampAlignment.BottomLeft => XStringFormats.BottomLeft,
        PdfStampAlignment.BottomCenter => XStringFormats.BottomCenter,
        PdfStampAlignment.BottomRight => XStringFormats.BottomRight,
        _ => XStringFormats.CenterLeft
    };
}
