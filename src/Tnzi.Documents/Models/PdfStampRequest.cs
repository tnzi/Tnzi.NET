namespace Tnzi.Documents.Models;

/// <summary>
/// 一次盖章请求：先按 <see cref="AppendPages"/> 追加整页，再按 <see cref="Stamps"/> 逐个绘制。
/// </summary>
/// <remarks>
/// 追加页先于绘制发生，因此 <see cref="PdfStamp.PageNumber"/> 可以直接指向追加出来的页
/// （页码接在原有页之后），「追加一页完成证书并往上写字」是一次调用。
/// </remarks>
[ExperimentalApi(Reason = "文档原语包的首个版本，字段可能随消费方需求增补")]
public sealed class PdfStampRequest
{
    /// <summary>要追加到文档末尾的整页（按顺序）。</summary>
    public IReadOnlyList<PdfPageSpec> AppendPages { get; init; } = [];

    /// <summary>要绘制的内容，列表顺序即同页内的叠放顺序（后画的在上）。</summary>
    public IReadOnlyList<PdfStamp> Stamps { get; init; } = [];
}

/// <summary>
/// 追加页的尺寸规格。
/// </summary>
[ExperimentalApi(Reason = "文档原语包的首个版本，字段可能随消费方需求增补")]
public sealed class PdfPageSpec
{
    /// <summary>页宽（point）；为空时沿用原文档最后一页的宽度。</summary>
    public double? WidthPoints { get; init; }

    /// <summary>页高（point）；为空时沿用原文档最后一页的高度。</summary>
    public double? HeightPoints { get; init; }
}
