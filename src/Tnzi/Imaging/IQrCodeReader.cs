namespace Tnzi.Imaging;

/// <summary>
/// 从一张图片里读出 QR 码的载荷。
/// </summary>
/// <remarks>
/// <para>
/// <b>契约在核心、实现在可选包 <c>Tnzi.Imaging</c></b>，消费方一律可选注入
/// （<c>IQrCodeReader? reader = null</c>）。与 <see cref="IQrCodeGenerator"/> 成对，
/// 但刻意是两个接口：只印码的应用不该被迫带上解码器，反之亦然。
/// </para>
/// <para>
/// <b>输入是一整张图，不是裁好的码。</b>典型输入是一整页扫描件或传真件 ——
/// 码在页面哪个角落由调用方的排版决定，实现自己去找。
/// </para>
/// <para>
/// ★ <b>「没找到」不是错误。</b>一张没有码的纸是完全正常的输入（人回传了错的一页、
/// 码被订书钉压住、扫描时那一角折了）。实现必须返回
/// <see cref="QrCodeScanResult.NotFound"/> 而不是抛异常 —— 只有「这根本不是一张图片」
/// 才抛。把两者混为一谈会逼调用方用 try/catch 做正常分支。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "QR primitives are still shaped by their first consumers")]
public interface IQrCodeReader
{
    /// <summary>
    /// 在图片里找一个 QR 码并读出它的载荷。
    /// </summary>
    /// <param name="image">图片字节（PNG / JPEG / BMP / TIFF / WebP 等常见格式）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>找到时带载荷，没找到时是 <see cref="QrCodeScanResult.NotFound"/>。</returns>
    /// <remarks>
    /// <para>
    /// 实现<b>必须</b>容忍这些形变，它们是纸质回路的常态而非例外：
    /// 整体旋转（含倒置）、几度的歪斜、黑白反相、以及每个模块只落到两三个扫描像素上的粗采样。
    /// </para>
    /// <para>
    /// 一张图里有多个码时只返回其中一个，且<b>不保证是哪一个</b>。要在多码场景下区分，
    /// 调用方应当先按版面裁切再逐块读。
    /// </para>
    /// <para><paramref name="image"/> 不是可识别的图片格式时抛 <see cref="ArgumentException"/>。</para>
    /// </remarks>
    Task<QrCodeScanResult> ReadAsync(byte[] image, CancellationToken ct = default);
}
