namespace Tnzi.Documents;

/// <summary>
/// 把 PDF 的某一页渲染成位图。
/// </summary>
/// <remarks>
/// <para>
/// <b>契约在核心、实现在可选包 <c>Tnzi.Documents</c></b>，消费方一律可选注入
/// （<c>IPdfRasterizer? rasterizer = null</c>），没加载时为 null → 退化为「本部署不支持」。
/// </para>
/// <para>
/// ★ <b>这是 <c>Tnzi.Documents</c> 里唯一带原生依赖的原语。</b>光栅化要一个 PDF 渲染引擎
/// （PDFium），它带原生二进制：指定 RID 发布约 19 MB，不指定 RID 的可移植发布上百 MB。
/// 这笔账落在<b>每一个</b>引用该包的消费者身上，包括一行光栅化都不调的 <c>Tnzi.Signing</c>。
/// 它曾是独立包 <c>Tnzi.Documents.Raster</c> 正是为了避免这一点，2026-08-29 合并进来。
/// <b>生产发布请指定 RID</b>；该包随包发的 <c>buildTransitive</c> targets 默认剔掉
/// 89 MB/RID 的 <c>libSkiaSharp.pdb</c>。
/// </para>
/// <para>
/// <b>与 <see cref="IDocumentImageRenderer"/> 的分工</b>：那个把<b>源文档</b>（HTML / Office）
/// 的首页渲染成缩略图，走的是 headless 浏览器，明确<b>不支持 <c>.pdf</c></b>
/// —— 浏览器打开 PDF 渲染出来的是查看器界面而不是页面。本接口才是「PDF → 像素」，
/// 按页索引取、按分辨率取。
/// </para>
/// <para>
/// <b>两类消费者，默认值只偏向其中一类。</b><see cref="PdfRasterRequest"/> 的默认值（200 dpi、灰度、PNG）
/// 服务的是机读（找码、OCR、比对）；给人看的缩略图<b>同样是本接口的正当用途</b>，只是要自己改请求：
/// 彩色（<c>Grayscale = false</c>）、更低的 dpi，再把 PNG 交给 <c>Tnzi.Imaging</c> 缩放、编码。
/// 「要缩略图拿渲染前的 HTML 出」那条建议只对框架<b>自己</b>生成的 PDF 成立 —— 用户上传的 PDF 没有 HTML 可拿，
/// 首页像素只能从这里来。第一个这样的消费者是 <c>Tnzi.Storage</c>（2026-09-14 起为上传的 PDF 出首页缩略图，
/// 可选注入本契约，没加载实现包时 PDF 就没有缩略图）。
/// </para>
/// <para>
/// ★ <b>必须对扫描件与传真件成立，不只是对框架自己生成的 PDF。</b>入站传真是 CCITT G4 编码，
/// 有的扫描仪按横条切成多张图而不是整页一张，有的还叠了一层 OCR 文本 ——
/// 「把内嵌图像抠出来」这条路会在这些形态上逐个碰壁，所以这里的语义是<b>渲染整页</b>，
/// 而不是提取图像。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Document primitives are still shaped by their first consumers")]
public interface IPdfRasterizer
{
    /// <summary>
    /// 这个渲染器<b>此刻</b>能不能干活（原生库装没装上）。
    /// </summary>
    /// <remarks>口径与 <see cref="IDocumentConverter.IsAvailable"/> 一致：调用方要决定
    /// 「把入口显示出来吗」问这一个。</remarks>
    bool IsAvailable => true;

    /// <summary>读出 PDF 的页数。</summary>
    /// <param name="source">PDF 字节。</param>
    /// <param name="ct">取消令牌。</param>
    /// <remarks>要逐页扫的调用方先问这个；<b>不要</b>靠反复 <see cref="RenderPageAsync"/> 撞越界来探边界。</remarks>
    Task<int> GetPageCountAsync(byte[] source, CancellationToken ct = default);

    /// <summary>把某一页渲染成位图。</summary>
    /// <param name="source">PDF 字节。</param>
    /// <param name="pageIndex">页索引，<b>从 0 开始</b>。</param>
    /// <param name="request">渲染请求；为 null 时用 <see cref="PdfRasterRequest"/> 的默认值。</param>
    /// <param name="ct">取消令牌。</param>
    /// <remarks>
    /// 失败时抛 <c>Tnzi.Documents.Exceptions.PdfRasterizationException</c>
    /// （<see cref="Exceptions.InfrastructureException"/> 的子类）：文件不是 PDF、有口令、原生库缺失。
    /// <b>不会静默降级</b> —— 出不来图就报错，而不是回一张空白页。
    /// <paramref name="pageIndex"/> 越界抛 <see cref="ArgumentOutOfRangeException"/>。
    /// </remarks>
    Task<DocumentImage> RenderPageAsync(
        byte[] source,
        int pageIndex,
        PdfRasterRequest? request = null,
        CancellationToken ct = default);
}
