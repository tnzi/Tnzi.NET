namespace Tnzi.Imaging;

/// <summary>
/// 把一段短文本编码成 QR 码位图。
/// </summary>
/// <remarks>
/// <para>
/// <b>契约在核心、实现在可选包 <c>Tnzi.Imaging</c></b>，消费方一律可选注入
/// （<c>IQrCodeGenerator? qr = null</c>），没加载时为 null → 退化为「本部署不支持」。
/// 与 <see cref="IImageEditor"/>、<see cref="Documents.IDocumentConverter"/> 是同一个模式，
/// 理由也相同：想在自己的单据上印一个码的模块，不该因此被拉进一个图像库的依赖闭包。
/// </para>
/// <para>
/// <b>为什么值得做成框架原语</b>：把一份文档发出去、由人在纸上处理、再扫描或拍照送回来，
/// 这条回路要能自动闭合，就得让「送回来的这张纸」认得出对应的是哪一份记录。印一个短码上去、
/// 回来时读出来是通行做法，而它两侧都与业务无关 —— 生成在这里，读取在
/// <see cref="IQrCodeReader"/>。不做成原语的话，每个有纸质回路的应用都要各写一遍。
/// </para>
/// <para>
/// ★ <b>本接口不认识「关联码」这个概念</b>：载荷是什么、怎么校验、和哪条记录对应，
/// 全是调用方的事。这里只负责「这段文本 → 这张图」。
/// </para>
/// <para>
/// <b>同步不是疏忽</b>：编码是纯计算（一份 21×21 的码不到一毫秒），没有 IO 也没有外部进程，
/// 给它一个 <c>Task</c> 只会让调用方以为可以取消。读取那侧要解码图片字节，故是异步的。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "QR primitives are still shaped by their first consumers")]
public interface IQrCodeGenerator
{
    /// <summary>
    /// 按 <paramref name="request"/> 生成一张 QR 码 PNG。
    /// </summary>
    /// <param name="request">生成请求。</param>
    /// <returns>PNG 字节，以及实际用上的版本、模块数与每模块像素数。</returns>
    /// <remarks>
    /// <para>
    /// 载荷放不进 <see cref="QrCodeRequest.MaxVersion"/> 限定的尺寸时抛
    /// <see cref="ArgumentException"/>，<b>不会自动放大版本</b>。悄悄升一个版本会让模块变小，
    /// 而模块大小正是这张码在纸上活不活得下来的决定因素 —— 实测：同一个物理尺寸下
    /// 版本 1 能 100% 读回来的配置，升到版本 2 之后只剩八成。
    /// </para>
    /// <para>请求本身不合法（载荷为空、尺寸越界）同样抛 <see cref="ArgumentException"/> 系列：那是编程错误。</para>
    /// </remarks>
    QrCodeImage Generate(QrCodeRequest request);
}
