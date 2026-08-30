namespace Tnzi.Documents;

/// <summary>
/// 一次 PDF 页面光栅化请求。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>产物恒为 PNG，刻意没有格式与质量选项。</b>本接口服务的是机读：JPEG 的块效应
/// 会在黑白边界上产生振铃，而那正是条码与文字识别用来判定边界的地方。要更小的产物，
/// 拿这份 PNG 交给 <see cref="Imaging.IImageEditor"/> 转 —— 那时至少是显式的决定，
/// 而且那边真的能控制编码质量。
/// </para>
/// <para>
/// ★ <b>分辨率不是「越高越好」。</b>它同时决定了产物的像素数（US Letter 在 600dpi 下是 3300 万像素）
/// 与下游算法看到的采样密度。要在扫描件上找一个 QR 码，决定成败的是<b>每个模块落到几个像素</b>，
/// 而那由「原件上码有多大」和这里的 dpi 共同决定 —— 单方面调高 dpi 只会把噪点一起放大。
/// </para>
/// </remarks>
public class PdfRasterRequest
{
    /// <summary>默认渲染分辨率（dpi）。</summary>
    /// <remarks>
    /// 200 dpi 是扫描与传真件的常用采样率，在「看得清」与「像素别太多」之间；
    /// 实测 US Letter 单页约 0.3 秒。
    /// </remarks>
    public const int DefaultDpi = 200;

    /// <summary>渲染分辨率（dpi），默认 <see cref="DefaultDpi"/>。</summary>
    public int Dpi { get; set; } = DefaultDpi;

    /// <summary>
    /// 渲染成灰度图，默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// 本接口服务的是机读（找码、OCR、比对），这些算法一律先把彩色扔掉；
    /// 直接出灰度省掉一次转换，产物也小得多。要看颜色的场合把它设成 <c>false</c>。
    /// </remarks>
    public bool Grayscale { get; set; } = true;

    /// <summary>
    /// 渲染批注（注释、图章、表单控件的外观流），默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// 默认关掉是因为批注不是页面内容：把审阅意见画进「这一页长什么样」的位图里，
    /// 会让机读看到原件上并不存在的墨迹。
    /// </remarks>
    public bool WithAnnotations { get; set; }

}
