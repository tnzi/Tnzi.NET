namespace Tnzi.Documents.Options;

/// <summary>
/// PDF 光栅化配置。
/// </summary>
/// <remarks>
/// 只有一项，而且它管的是「拒绝什么」而不是「怎么渲染」：怎么渲染由每次调用的
/// <see cref="PdfRasterRequest"/> 决定，配置只负责给这台机器的资源用量划一条线。
/// </remarks>
[ConfigSection("Documents:Raster")]
public class PdfRasterOptions
{
    /// <summary>默认单页像素上限。</summary>
    /// <remarks>
    /// 4000 万像素约等于 US Letter 在 600 dpi 下的尺寸；灰度下约 40 MB，彩色约 160 MB。
    /// </remarks>
    public const long DefaultMaxPagePixels = 40_000_000L;

    /// <summary>
    /// 单页渲染出来允许的最大像素数，超过就拒绝渲染。
    /// </summary>
    /// <remarks>
    /// ★ <b>这不是性能调优项，是一道闸门。</b>页面尺寸来自文件，dpi 来自调用方，
    /// 两者相乘没有上界 —— 一份声明自己有 200 英寸见方的 PDF 配上 600 dpi，
    /// 会让 PDFium 去申请上百 GB 的位图。没有这条线的话，一个上传口就是一次
    /// 「一份文件放倒一个进程」。<c>0</c> 表示不限（只在完全受信任的输入上这么设）。
    /// </remarks>
    public long MaxPagePixels { get; set; } = DefaultMaxPagePixels;
}
