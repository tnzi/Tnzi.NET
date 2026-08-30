namespace Tnzi.Documents.Services.Internal;

/// <summary>
/// 页面几何：纸张尺寸解析，以及「页面内容盒有多少 CSS 像素」。
/// </summary>
/// <remarks>
/// 出 PDF 与出缩略图共用同一份纸张配置，几何换算因此收口在这里 ——
/// 两边各算一遍的话，缩略图会慢慢跟它要代表的那份 PDF 跑偏，而这种偏差没有任何报错。
/// </remarks>
internal static class HtmlPageGeometry
{
    private const double PointsPerInch = 72d;
    private const double CssPixelsPerInch = 96d;

    /// <summary>1 点等于多少 CSS 像素（96/72 = 4/3）。</summary>
    public const double CssPixelsPerPoint = CssPixelsPerInch / PointsPerInch;

    /// <summary>解析纸张尺寸（点）：显式宽高 &gt; 纸张名 &gt; US Letter。</summary>
    /// <param name="options">HTML 渲染配置。</param>
    /// <remarks>
    /// <b>不处理横向</b>：出 PDF 那条路把 <c>landscape</c> 原样交给浏览器，由它自己转；
    /// 在这里先转一次会让页面被转两遍。要横向后的实际尺寸用 <see cref="ContentBoxCssPixels"/>。
    /// </remarks>
    public static (double WidthPt, double HeightPt) ResolvePaperSizePt(HtmlPdfOptions options)
    {
        if (options.PaperWidthPt > 0 && options.PaperHeightPt > 0)
            return (options.PaperWidthPt, options.PaperHeightPt);

        // 名字非法在启动期就被验证器拦下了；这里的回退只为「验证器被绕过」留一条确定的路。
        return PaperSizes.TryGet(options.PaperSize, out var named)
            ? named
            : (PaperSizes.LetterWidthPt, PaperSizes.LetterHeightPt);
    }

    /// <summary>
    /// 页面**内容盒**（纸张去掉四边边距，横向已算进去）有多少 CSS 像素。
    /// </summary>
    /// <param name="options">HTML 渲染配置。</param>
    /// <remarks>
    /// 缩略图按内容盒而不是整张纸出图：纸张四周那圈空白在 240px 宽的卡片上纯属浪费，
    /// 而内容盒的宽度正是 PDF 里正文的排版宽度，所以同一份文档两边看起来是一致的。
    /// </remarks>
    public static (double WidthPx, double HeightPx) ContentBoxCssPixels(HtmlPdfOptions options)
    {
        var (widthPt, heightPt) = ResolvePaperSizePt(options);

        if (options.Landscape)
            (widthPt, heightPt) = (heightPt, widthPt);

        // 边距配得比纸还大是配置错误而不是崩溃理由：夹到 1 点，出一张窄图让人一眼看出配错了。
        var contentWidthPt = Math.Max(1d, widthPt - options.MarginLeftPt - options.MarginRightPt);
        var contentHeightPt = Math.Max(1d, heightPt - options.MarginTopPt - options.MarginBottomPt);

        return (contentWidthPt * CssPixelsPerPoint, contentHeightPt * CssPixelsPerPoint);
    }
}
