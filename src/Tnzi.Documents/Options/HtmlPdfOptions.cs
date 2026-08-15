namespace Tnzi.Documents.Options;

/// <summary>
/// HTML 转 PDF 的配置（配置节 <c>Documents:Html</c>）。
/// </summary>
/// <remarks>
/// <para>
/// HTML 走的是**本机浏览器**（Chrome / Edge / Chromium 的 headless 模式），不是 LibreOffice：
/// LibreOffice 的 HTML 导入只认很小一部分 CSS，实测会丢掉 <c>text-align</c>、<c>text-indent</c>、
/// 行内元素的 <c>border-bottom</c>，连 <c>img</c> 上的 <c>width</c> 都不认（图按原始像素画出来）。
/// 而 HTML 的判定标准恰恰是「浏览器长什么样」，所以只有浏览器自己能给出正确答案。
/// </para>
/// <para>
/// 浏览器**不随框架分发**：用宿主上已经装好的那个。Windows Server 自带 Edge，
/// 因此「文件同步发布到 IIS」这种部署方式无需额外搬运二进制。
/// </para>
/// </remarks>
[ConfigSection("Documents:Html")]
public class HtmlPdfOptions
{
    /// <summary>
    /// 是否启用浏览器渲染，默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// 关掉之后 <c>.htm</c> / <c>.html</c> 会退回 LibreOffice（即旧行为）。**这是显式的退路，不是自动降级** ——
    /// 找不到浏览器时框架宁可报错也不会自己换成 LibreOffice：同一份 HTML 在两条路径下出来的 PDF
    /// 差别极大，「悄悄换一条能跑通的路」比直接失败危险得多。
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 浏览器可执行文件路径（<c>chrome.exe</c> / <c>msedge.exe</c> / <c>chromium</c>），也可给它所在目录。
    /// </summary>
    /// <remarks>
    /// 为空时按各操作系统的常见安装路径 + <c>PATH</c> 自动探测。
    /// **显式配置了就不再回退探测** —— 与 <see cref="DocumentsOptions.LibreOfficePath"/> 同一口径：
    /// 配错路径要立刻报错，而不是悄悄换一个浏览器跑。
    /// </remarks>
    public string? BrowserPath { get; set; }

    /// <summary>
    /// 纸张名，默认 <c>Letter</c>；取值见 <see cref="PaperSizes.Names"/>。
    /// </summary>
    /// <remarks>
    /// 默认取 US Letter 而非 A4，与 <c>IPdfStamper.Create</c> 无尺寸时的回退保持一致
    /// （框架内已有的唯一先例）。被 <see cref="PaperWidthPt"/> / <see cref="PaperHeightPt"/> 覆盖，
    /// 也被文档自带的 <c>@page { size: … }</c> 覆盖（见 <see cref="PreferCssPageSize"/>）。
    /// </remarks>
    public string PaperSize { get; set; } = "Letter";

    /// <summary>纸张宽（点）；大于 0 时覆盖 <see cref="PaperSize"/>。</summary>
    public double PaperWidthPt { get; set; }

    /// <summary>纸张高（点）；大于 0 时覆盖 <see cref="PaperSize"/>。</summary>
    public double PaperHeightPt { get; set; }

    /// <summary>是否横向，默认 <c>false</c>。</summary>
    public bool Landscape { get; set; }

    /// <summary>上边距（点），默认 28.8（0.4 英寸，即浏览器打印对话框的默认边距）。</summary>
    public double MarginTopPt { get; set; } = DefaultMarginPt;

    /// <summary>右边距（点），默认 28.8。</summary>
    public double MarginRightPt { get; set; } = DefaultMarginPt;

    /// <summary>下边距（点），默认 28.8。</summary>
    public double MarginBottomPt { get; set; } = DefaultMarginPt;

    /// <summary>左边距（点），默认 28.8。</summary>
    public double MarginLeftPt { get; set; } = DefaultMarginPt;

    /// <summary>
    /// 是否打印背景色与背景图，默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// 刻意与浏览器打印对话框的默认值（关）相反：本能力的判定标准是「屏幕上那份文档」，
    /// 而表单的底纹、斑马纹表格在屏幕上是可见的。不想要就设成 false。
    /// </remarks>
    public bool PrintBackground { get; set; } = true;

    /// <summary>
    /// 文档自带 <c>@page { size: … }</c> 时是否以它为准，默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// 这是「调用方控制页面尺寸」最精确的一档：尺寸跟着文档走，同一个应用里不同模板可以出不同开本。
    /// 文档没写 <c>@page</c> 时回落到 <see cref="PaperSize"/>（实测确认，不是推断）。
    /// **只管尺寸不管边距**，边距一律取本配置。
    /// </remarks>
    public bool PreferCssPageSize { get; set; } = true;

    /// <summary>缩放比例，默认 1.0（有效范围 0.1–2.0，浏览器自身的限制）。</summary>
    public double Scale { get; set; } = 1.0d;

    /// <summary>单次渲染的超时秒数，默认 60。</summary>
    /// <remarks>超时会杀掉整棵浏览器进程树，否则会留下孤儿进程占着 profile 目录。</remarks>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// 同时最多跑几个浏览器实例，默认 2。
    /// </summary>
    /// <remarks>
    /// 与 LibreOffice 那条路径「全局串行」的原因不同：浏览器各用各的 profile 目录，并发本身是安全的，
    /// 这里限的是**内存**（每个 headless 实例是百 MB 量级，不设上限就是一个现成的拒绝服务面）。
    /// 改这个值需要重启进程。
    /// </remarks>
    public int MaxConcurrency { get; set; } = 2;

    /// <summary>
    /// 是否给浏览器加 <c>--no-sandbox</c>，默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// 只在「容器里以 root 跑」这种拿不到内核沙箱的环境下才打开。**它降低的是浏览器自身的隔离强度**，
    /// 而这个浏览器要去渲染应用喂给它的 HTML，所以不要顺手打开。
    /// </remarks>
    public bool NoSandbox { get; set; }

    private const double DefaultMarginPt = 28.8d;
}
