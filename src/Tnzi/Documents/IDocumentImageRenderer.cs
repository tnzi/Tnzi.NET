namespace Tnzi.Documents;

/// <summary>
/// 把文档的首页渲染成位图（缩略图 / 卡片预览）。
/// </summary>
/// <remarks>
/// <para>
/// <b>与 <see cref="IDocumentConverter"/> 并列的第四个文档原语</b>：那个出 PDF（归档、签署、打印），
/// 本接口出**像素**（列表页认脸）。契约在核心、实现在可选包 <c>Tnzi.Documents</c>，
/// 消费方一律可选注入（<c>IDocumentImageRenderer? renderer = null</c>），没加载时为 null → 退化为「不支持」。
/// </para>
/// <para>
/// <b>为什么值得做成框架原语</b>：不这样做的话，想在列表里显示文档预览的应用只有两条路 ——
/// 要么把 PDF 拉到浏览器里重新解析成像素（一屏 33 张卡片 = 33 个 Web Worker、33 次完整 PDF 解析、
/// 几 MB 流量，且 Safari 上根本出不来），要么自己再起一个 headless 浏览器，
/// 把进程定位、超时、会话管理这些本框架已经拥有的东西再写一遍。
/// </para>
/// <para>
/// <b>输出是「看得出是哪一份」的图，不是证据。</b>它按屏幕渲染（而非打印管线）出图，
/// 与最终归档 PDF 不保证逐像素一致；需要逐像素一致的场合应当用 <see cref="IPdfRasterizer"/>
/// 去光栅化那份 PDF 本身。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Document primitives are still shaped by their first consumers")]
public interface IDocumentImageRenderer
{
    /// <summary>
    /// 这个渲染器<b>此刻</b>能不能干活（运行环境是否齐备）。
    /// </summary>
    /// <remarks>与 <see cref="CanRender"/> 正交，口径与 <see cref="IDocumentConverter.IsAvailable"/> 完全一致。</remarks>
    bool IsAvailable => true;

    /// <summary>判断该文件名的扩展名能否渲染成图片。</summary>
    /// <param name="fileName">源文件名（只取扩展名）。</param>
    /// <remarks>
    /// 只看格式，<b>不看运行环境</b>。★ <c>.pdf</c> <b>不在</b>支持之列，而且这不是「还没做」：
    /// headless 浏览器打开 PDF 渲染出来的是**查看器界面**（工具栏、侧边缩略图面板、深色背景，
    /// 实测确认），不是可用的页面图像。要「PDF → 像素」请用 <see cref="IPdfRasterizer"/> ——
    /// 同一个包里的另一个原语，走 PDFium，按页索引与 dpi 取图。
    /// </remarks>
    bool CanRender(string fileName);

    /// <summary><b>这个文件</b>此刻渲染不渲染得动。</summary>
    /// <param name="fileName">源文件名（只取扩展名）。</param>
    /// <remarks>即 <see cref="CanRender"/> 与 <see cref="IsAvailable"/> 的合取；要决定「显不显示预览入口」问这一个就够了。</remarks>
    bool IsAvailableFor(string fileName) => IsAvailable && CanRender(fileName);

    /// <summary>把文档的首页渲染成位图。</summary>
    /// <param name="source">源文档字节。</param>
    /// <param name="sourceFileName">源文件名；<b>只有扩展名会被采用</b>，其余部分丢弃。</param>
    /// <param name="request">出图请求；为 null 时用 <see cref="DocumentImageRequest"/> 的默认值。</param>
    /// <param name="ct">取消令牌。</param>
    /// <remarks>
    /// 失败时抛 <c>Tnzi.Documents.Exceptions.DocumentConversionException</c>
    /// （<see cref="Exceptions.InfrastructureException"/> 的子类）：渲染器不可用、扩展名不支持、超时、外部进程失败。
    /// <b>不会静默降级</b> —— 出不来图就报错，而不是回一张空白图或占位图。
    /// 请求本身不合法（宽高、质量越界）抛 <see cref="ArgumentOutOfRangeException"/>：那是调用方的编程错误，
    /// 与环境无关，且在起浏览器之前就被拦下。
    /// </remarks>
    Task<DocumentImage> RenderFirstPageAsync(
        byte[] source,
        string sourceFileName,
        DocumentImageRequest? request = null,
        CancellationToken ct = default);
}

/// <summary>位图输出格式。</summary>
public enum DocumentImageFormat
{
    /// <summary>PNG（默认）。无损，文字边缘干净，且 <c>img</c> 标签与所有浏览器都认。</summary>
    Png = 0,

    /// <summary>JPEG。文档缩略图以文字为主，通常比 PNG 更大且更糊，选它多半是为了对齐既有管线。</summary>
    Jpeg = 1,

    /// <summary>WebP。体积最小，老浏览器不认。</summary>
    Webp = 2
}
