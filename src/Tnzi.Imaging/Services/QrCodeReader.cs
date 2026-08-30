// ZXing 的命名空间刻意只在文件级导入：它有自己的 BitMatrix / Dimension / Result 等
// 通用名，全局导入会在整个程序集里和 ImageSharp 与编码库的同名类型互相遮蔽。
using ZXing;

namespace Tnzi.Imaging.Services;

/// <inheritdoc cref="IQrCodeReader"/>
/// <remarks>
/// <para>
/// 基于 ZXing.Net（Apache-2.0，纯托管、零传递依赖）+ ImageSharp 解图。
/// </para>
/// <para>
/// ★★★ <b>绝不能让 <see cref="DecodeHintType.PURE_BARCODE"/> 这个键出现在 <c>Options.Hints</c> 里。</b>
/// ZXing 读这个字典用的是 <c>ContainsKey</c>，<b>从不看值</b> ——
/// 于是 <c>Hints[PURE_BARCODE] = false</c> 会把纯码模式<b>打开</b>。那个模式假设码是完美对齐、
/// 未旋转、没有外边距的，两度歪斜就从「基本都能读」变成「永远读不出来」，而且不报错。
/// <para>
/// 注意<b>不是</b>「设了 <c>Options.PureBarcode = false</c> 就中招」：0.16.11 的这个属性
/// 在赋 <c>false</c> 时会把键<b>删掉</b>。危险的是绕过属性直接写字典。
/// 所以要守的不变量是「这个键不在里面」，而不是「这个属性是 false」——
/// <c>PureBarcodeHint_IsNeverSet</c> 断言的正是前者，
/// <c>PureBarcodeHintWithAFalseValue_BreaksSkewedCodes</c> 则把陷阱本身钉成可执行的事实。
/// </para>
/// </para>
/// <para>
/// <b>重试阶梯只有两级，而且是量出来的</b>（384 组模拟纸质回路：歪斜 0-3.5°、高斯模糊 0-3、
/// 光照渐变、每模块 1.8-4 像素）：<c>HybridBinarizer</c> 一趟捞回 363 组，
/// 最近邻放大 3 倍后再来一趟又捞回 6 组，<c>GlobalHistogramBinarizer</c> <b>一组都没多捞</b>
/// —— 所以它不在阶梯里。加一级从不触发的兜底，日后没有人能判断它还该不该在。
/// </para>
/// <para>
/// 旋转（含倒置）与黑白反相不需要单独的一级：解码器的 <c>AutoRotate</c> 与
/// <c>Options.TryInverted</c> 已经覆盖，靠的是 <see cref="BitmapLuminanceSource"/> 派生出的
/// 旋转与反相视图。
/// </para>
/// </remarks>
public class QrCodeReader : IQrCodeReader
{
    /// <summary>第二级重试的放大倍数。</summary>
    /// <remarks>
    /// 最近邻，不是插值：插值会把模块边缘抹圆，而这一级要救的恰恰是「边缘落在哪个像素上」。
    /// </remarks>
    private const int UpscaleFactor = 3;

    /// <summary>
    /// 放大后允许的最大像素数，超过就跳过第二级。
    /// </summary>
    /// <remarks>
    /// 没有上界的话，一张 600dpi 的整页扫描件放大三倍会变成上亿像素，
    /// 解一次要几十秒 —— 而这一级本来就只在第一级失败后才跑，为它赔上一个请求线程不划算。
    /// </remarks>
    private const long MaxUpscaledPixels = 40_000_000L;

    /// <summary>输入图片任一边的最大像素数。</summary>
    /// <remarks>
    /// 与 <c>BitmapExtensions.MaxDimension</c> 同口径 —— 同一个模块对「多大算太大」
    /// 只该有一个答案。
    /// </remarks>
    private const int MaxDimension = 10_000;

    /// <inheritdoc />
    public async Task<QrCodeScanResult> ReadAsync(byte[] image, CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(image);

        using var bitmap = await LoadAsync(image, ct);

        var payload = DecodeSinglePass(bitmap);
        if (payload is not null)
        {
            return new QrCodeScanResult(payload);
        }

        ct.ThrowIfCancellationRequested();

        var upscaledPixels = (long)bitmap.Width * bitmap.Height * UpscaleFactor * UpscaleFactor;
        if (upscaledPixels > MaxUpscaledPixels)
        {
            return QrCodeScanResult.NotFound;
        }

        using var upscaled = bitmap.Clone(x => x.Resize(
            bitmap.Width * UpscaleFactor,
            bitmap.Height * UpscaleFactor,
            KnownResamplers.NearestNeighbor));

        return new QrCodeScanResult(DecodeSinglePass(upscaled));
    }

    /// <summary>
    /// 按本实现的口径建一个解码器。
    /// </summary>
    /// <remarks>
    /// 测试要能看到这份配置本身（尤其是「PURE_BARCODE 这个键不在里面」），
    /// 所以配置收在这一个方法里，而不是散在调用点上。
    /// </remarks>
    internal static BarcodeReaderGeneric CreateReader()
    {
        var reader = new BarcodeReaderGeneric
        {
            // 90/180/270 度的整体旋转由它覆盖；几度的歪斜由 QR 自身的定位图形覆盖。
            AutoRotate = true
        };

        reader.Options.PossibleFormats = [BarcodeFormat.QR_CODE];
        reader.Options.TryHarder = true;

        // 白底黑码与黑底白码都可能出现（反相扫描、深色底纹上的码）。
        reader.Options.TryInverted = true;

        return reader;
    }

    /// <summary>只跑一趟解码，不做任何重试。</summary>
    /// <remarks>
    /// ★ 对测试开放，是为了让「放大这一级确实在救东西」这条断言写得出来：
    /// 一个从不触发的兜底级和没有这一级是一回事，而只断言「整体读得出来」区分不了两者。
    /// </remarks>
    internal static string? DecodeSinglePass(Image<L8> bitmap)
    {
        var luminances = new byte[(long)bitmap.Width * bitmap.Height];
        bitmap.CopyPixelDataTo(luminances);

        var source = new BitmapLuminanceSource(luminances, bitmap.Width, bitmap.Height);
        return CreateReader().Decode(source)?.Text;
    }

    /// <summary>
    /// 解成 8 位灰度。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 直接按 <c>L8</c> 解，而不是解成彩色再转灰度：解码器只看亮度，
    /// 早一步扔掉颜色能把一张整页扫描件的内存占用压到四分之一。
    /// </para>
    /// <para>
    /// ★ <b>先读文件头再解码。</b>本接口的输入是外来的（对方扫描或拍照回传的），
    /// 而尺寸写在文件头里、与文件大小无关：一张几百字节的 PNG 可以声称自己
    /// 60000×60000，解出来就是几个 GB。<c>Identify</c> 只读头不解像素，
    /// 判完再决定要不要真的解 —— 先解了再检查尺寸的话，内存已经申请过了。
    /// </para>
    /// </remarks>
    private static async Task<Image<L8>> LoadAsync(byte[] image, CancellationToken ct)
    {
        try
        {
            using var stream = new MemoryStream(image, writable: false);

            var info = await Image.IdentifyAsync(stream, ct);
            if (info.Width > MaxDimension || info.Height > MaxDimension)
            {
                throw new ArgumentException(
                    $"The image is {info.Width}x{info.Height}; neither side may exceed {MaxDimension} pixels.",
                    nameof(image));
            }

            stream.Position = 0;
            return await Image.LoadAsync<L8>(stream, ct);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            // 「这不是一张图片」是调用方的错，与「这张图里没有码」是两件事 ——
            // 后者返回 NotFound，前者必须响亮地失败。
            throw new ArgumentException("The supplied bytes are not a readable image.", nameof(image), ex);
        }
    }
}
