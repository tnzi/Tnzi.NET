namespace Tnzi.Documents.Models;

/// <summary>
/// 一次盖章：在某页的某个归一化矩形上画一样东西。
/// </summary>
/// <remarks>
/// 内容画进页面内容层，天然就是压平的（不使用 AcroForm 表单域，原因见
/// <see cref="Services.PdfSharpPdfStamper"/>）。同一页内按 <see cref="PdfStampRequest.Stamps"/>
/// 的顺序绘制，后画的盖在先画的上面。
/// </remarks>
[ExperimentalApi(Reason = "文档原语包的首个版本，字段可能随消费方需求增补")]
public abstract class PdfStamp
{
    /// <summary>目标页码（从 1 开始）。追加页的页码接在原有页之后。</summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>目标矩形（归一化，左上角原点）。</summary>
    /// <remarks>
    /// 宽为 0 时按「从 X 到页面右边缘」处理，高为 0 时按一行文字的高度处理，
    /// 方便用「一个锚点」而不是「一个框」来定位。
    /// </remarks>
    public NormalizedRect Rect { get; init; }
}

/// <summary>
/// 文本盖章：在矩形内画一行文字。
/// </summary>
/// <remarks>不自动换行 —— 需要多行时由调用方拆成多个 <see cref="PdfTextStamp"/>。</remarks>
[ExperimentalApi(Reason = "文档原语包的首个版本，字段可能随消费方需求增补")]
public sealed class PdfTextStamp : PdfStamp
{
    /// <summary>要画的文字。</summary>
    public required string Text { get; init; }

    /// <summary>字号（point）。</summary>
    public double FontSize { get; init; } = 10d;

    /// <summary>是否加粗。</summary>
    public bool Bold { get; init; }

    /// <summary>是否斜体。</summary>
    public bool Italic { get; init; }

    /// <summary>颜色，<c>#RRGGBB</c> 或 <c>#AARRGGBB</c>；为空取黑色。</summary>
    public string? Color { get; init; }

    /// <summary>文字在矩形内的对齐方式。</summary>
    public PdfStampAlignment Alignment { get; init; } = PdfStampAlignment.CenterLeft;
}

/// <summary>
/// 图片盖章：在矩形内画一张图（签名图、印章图等）。
/// </summary>
[ExperimentalApi(Reason = "文档原语包的首个版本，字段可能随消费方需求增补")]
public sealed class PdfImageStamp : PdfStamp
{
    /// <summary>图片原始字节（PNG / JPEG）。与 <see cref="DataUrl"/> 二选一，本字段优先。</summary>
    public byte[]? Content { get; init; }

    /// <summary>
    /// 图片的 data URL（<c>data:image/png;base64,...</c>）；也接受裸 base64 串。
    /// </summary>
    public string? DataUrl { get; init; }

    /// <summary>是否保持宽高比（true 时按「contain」缩放并在矩形内居中）。</summary>
    public bool PreserveAspectRatio { get; init; } = true;
}

/// <summary>
/// 盖章内容在目标矩形内的对齐方式。
/// </summary>
public enum PdfStampAlignment
{
    /// <summary>左上。</summary>
    TopLeft = 0,

    /// <summary>顶部居中。</summary>
    TopCenter = 1,

    /// <summary>右上。</summary>
    TopRight = 2,

    /// <summary>垂直居中靠左。</summary>
    CenterLeft = 3,

    /// <summary>正中。</summary>
    Center = 4,

    /// <summary>垂直居中靠右。</summary>
    CenterRight = 5,

    /// <summary>左下。</summary>
    BottomLeft = 6,

    /// <summary>底部居中。</summary>
    BottomCenter = 7,

    /// <summary>右下。</summary>
    BottomRight = 8
}
