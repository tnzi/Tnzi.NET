namespace Tnzi.Documents;

/// <summary>
/// 出图请求：目标像素尺寸与编码格式。
/// </summary>
/// <remarks>
/// <para>
/// <b>纸张、边距、缩放不在这里</b> —— 它们取自 <c>Documents:Html</c>，与出 PDF 用的是同一套值。
/// 缩略图要看起来像那份 PDF，让调用方在两个地方各配一次纸张只会让两者悄悄跑偏。
/// 这里只回答「要多少像素」。
/// </para>
/// </remarks>
public sealed class DocumentImageRequest
{
    /// <summary>输出宽度（像素），默认 480。</summary>
    /// <remarks>
    /// 高度按页面内容区的比例算出来，所以只给宽度就能得到一张不变形的整页缩略图。
    /// 请按**设备像素**给值（CSS 240px @ DPR2 就是 480）。
    /// </remarks>
    public int Width { get; init; } = 480;

    /// <summary>
    /// 输出高度（像素）；为 null 时按整页比例自动算。
    /// </summary>
    /// <remarks>
    /// 给了值就是**从页面顶部裁一条**（卡片上那种横幅式预览），而不是把整页压扁 ——
    /// 压扁会让每一份文档都糊成一团灰，认不出是哪份。
    /// 给的值超过整页高度时按整页高度截断，返回的 <see cref="DocumentImage.Height"/> 会如实反映实际高度。
    /// </remarks>
    public int? Height { get; init; }

    /// <summary>编码格式，默认 <see cref="DocumentImageFormat.Png"/>。</summary>
    public DocumentImageFormat Format { get; init; } = DocumentImageFormat.Png;

    /// <summary>有损格式的质量（1-100），默认 80；<see cref="DocumentImageFormat.Png"/> 下无意义。</summary>
    public int Quality { get; init; } = 80;
}
