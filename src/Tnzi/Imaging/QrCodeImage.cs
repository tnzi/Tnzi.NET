namespace Tnzi.Imaging;

/// <summary>
/// 生成出来的 QR 码：PNG 字节 + 版本与几何。
/// </summary>
/// <remarks>
/// ★ <b>版本与模块数是产出的一部分，不是内部细节。</b>调用方要按它在版面上留出方框、
/// 换算物理尺寸、决定这张码值不值得发出去。拿不到版本的话，唯一的办法是数像素倒推。
/// </remarks>
public sealed class QrCodeImage
{
    /// <summary>初始化一个 <see cref="QrCodeImage"/> 实例。</summary>
    /// <param name="content">PNG 字节。</param>
    /// <param name="version">QR 版本（1-40）。</param>
    /// <param name="pixelsPerModule">每个模块的像素数。</param>
    /// <param name="quietZoneModules">静区宽度（模块）。</param>
    /// <param name="errorCorrection">实际用上的纠错等级。</param>
    public QrCodeImage(byte[] content, int version, int pixelsPerModule, int quietZoneModules, QrErrorCorrection errorCorrection)
    {
        Content = Check.NotNull(content);
        Version = version;
        PixelsPerModule = pixelsPerModule;
        QuietZoneModules = quietZoneModules;
        ErrorCorrection = errorCorrection;
    }

    /// <summary>PNG 字节。</summary>
    public byte[] Content { get; }

    /// <summary>
    /// MIME 类型，恒为 <c>image/png</c>。
    /// </summary>
    /// <remarks>
    /// ★ <b>刻意不提供格式选项。</b>JPEG 的块效应会在模块边界上产生振铃，
    /// 那正是解码器用来判定黑白的地方 —— 一张「能看」的 JPEG QR 码可能根本读不出来。
    /// 要别的格式，拿这份 PNG 自己转，那时至少是显式的决定。
    /// </remarks>
    public string ContentType => "image/png";

    /// <summary>文件扩展名（含点号）。</summary>
    public string FileExtension => ".png";

    /// <summary>QR 版本（1-40）。</summary>
    /// <remarks>版本决定模块数：<see cref="ModuleCount"/> = 4 × 版本 + 17。</remarks>
    public int Version { get; }

    /// <summary>码本身的模块数（边长），<b>不含静区</b>。</summary>
    public int ModuleCount => (4 * Version) + 17;

    /// <summary>静区宽度（模块）。</summary>
    public int QuietZoneModules { get; }

    /// <summary>每个模块的像素数。</summary>
    /// <remarks>
    /// 这是把它印到纸上时唯一需要算的数：物理边长 ÷ (<see cref="ModuleCount"/> + 2 × <see cref="QuietZoneModules"/>)
    /// 就是每个模块的物理尺寸，再乘扫描分辨率就是接收端的每模块采样像素数。
    /// </remarks>
    public int PixelsPerModule { get; }

    /// <summary>实际用上的纠错等级；可能高于请求值（见 <see cref="QrCodeRequest.BoostErrorCorrection"/>）。</summary>
    public QrErrorCorrection ErrorCorrection { get; }

    /// <summary>图片宽度（像素），含静区。</summary>
    public int Width => (ModuleCount + (2 * QuietZoneModules)) * PixelsPerModule;

    /// <summary>图片高度（像素）；QR 码是正方形，恒等于 <see cref="Width"/>。</summary>
    public int Height => Width;
}
