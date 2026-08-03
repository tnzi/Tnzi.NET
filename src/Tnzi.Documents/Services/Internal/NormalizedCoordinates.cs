
namespace Tnzi.Documents.Services.Internal;

/// <summary>
/// 归一化坐标（0-1，左上角原点）与两套 PDF 坐标系之间的换算。
/// </summary>
/// <remarks>
/// 三套坐标系必须分清，写反一次就是「盖章盖到页面外」：
/// <list type="number">
/// <item><b>PDF 原生</b>（PdfPig 读出来的）：原点在**左下角**，Y 轴向上，单位 point。</item>
/// <item><b>归一化</b>（本包对外形态）：原点在**左上角**，Y 轴向下，取值 0-1。</item>
/// <item><b>PDFsharp 的 XGraphics</b>：原点已经在**左上角**、Y 轴向下，单位 point。</item>
/// </list>
/// 所以：读（1 -&gt; 2）要翻 Y，写（2 -&gt; 3）**只缩放不翻 Y**。
/// 在写的那一侧再翻一次是最容易犯的错，<c>NormalizedCoordinateTests</c> 把这条钉死。
/// </remarks>
internal static class NormalizedCoordinates
{
    /// <summary>
    /// PDF 原生包围盒（左下角原点）-&gt; 归一化矩形（左上角原点）。
    /// </summary>
    /// <param name="minX">包围盒左边界（point）。</param>
    /// <param name="minY">包围盒下边界（point）。</param>
    /// <param name="maxX">包围盒右边界（point）。</param>
    /// <param name="maxY">包围盒上边界（point）。</param>
    /// <param name="pageWidth">页宽（point）。</param>
    /// <param name="pageHeight">页高（point）。</param>
    public static NormalizedRect FromPdfBox(double minX, double minY, double maxX, double maxY, double pageWidth, double pageHeight)
    {
        if (pageWidth <= 0 || pageHeight <= 0)
            return NormalizedRect.Empty;

        return new NormalizedRect(
            minX / pageWidth,
            1d - (maxY / pageHeight),
            (maxX - minX) / pageWidth,
            (maxY - minY) / pageHeight);
    }

    /// <summary>
    /// 归一化矩形 -&gt; XGraphics 页面矩形（point）。两者原点都在左上角，故**不翻 Y**。
    /// </summary>
    /// <param name="rect">归一化矩形。</param>
    /// <param name="pageWidth">页宽（point）。</param>
    /// <param name="pageHeight">页高（point）。</param>
    /// <param name="lineHeight">高为 0 时代替使用的行高（point）。</param>
    public static XRect ToPageRect(NormalizedRect rect, double pageWidth, double pageHeight, double lineHeight = 0d)
    {
        var left = rect.X * pageWidth;
        var top = rect.Y * pageHeight;

        // 宽/高为 0 = 用锚点而不是框来定位：宽补到页面右边缘，高补一行文字。
        var width = rect.Width > 0 ? rect.Width * pageWidth : Math.Max(0d, pageWidth - left);
        var height = rect.Height > 0 ? rect.Height * pageHeight : lineHeight;

        return new XRect(left, top, width, height);
    }
}
