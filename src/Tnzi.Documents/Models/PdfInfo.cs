namespace Tnzi.Documents.Models;

/// <summary>
/// PDF 的结构信息：页数与每页尺寸。
/// </summary>
[ExperimentalApi(Reason = "文档原语包的首个版本，字段可能随消费方需求增补")]
public sealed class PdfInfo
{
    /// <summary>初始化一个 <see cref="PdfInfo"/> 实例。</summary>
    /// <param name="pages">按页码升序的页信息。</param>
    public PdfInfo(IReadOnlyList<PdfPageInfo> pages)
    {
        Pages = Check.NotNull(pages);
    }

    /// <summary>页数。</summary>
    public int PageCount => Pages.Count;

    /// <summary>按页码升序的页信息。</summary>
    public IReadOnlyList<PdfPageInfo> Pages { get; }
}

/// <summary>
/// 单页信息。尺寸单位为 PDF 原生的 point（1/72 英寸），呈现端一般只需要宽高比。
/// </summary>
[ExperimentalApi(Reason = "文档原语包的首个版本，字段可能随消费方需求增补")]
public sealed class PdfPageInfo
{
    /// <summary>初始化一个 <see cref="PdfPageInfo"/> 实例。</summary>
    /// <param name="number">页码（从 1 开始）。</param>
    /// <param name="width">页宽（point）。</param>
    /// <param name="height">页高（point）。</param>
    public PdfPageInfo(int number, double width, double height)
    {
        Number = number;
        Width = width;
        Height = height;
    }

    /// <summary>页码（从 1 开始，与 <see cref="PdfStamp.PageNumber"/> 同一口径）。</summary>
    public int Number { get; }

    /// <summary>页宽（point）。</summary>
    public double Width { get; }

    /// <summary>页高（point）。</summary>
    public double Height { get; }
}
