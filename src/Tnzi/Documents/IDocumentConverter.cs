namespace Tnzi.Documents;

/// <summary>
/// 把 Office 等格式的文档转成 PDF。
/// </summary>
/// <remarks>
/// <para>
/// <b>契约在核心、实现在可选包 <c>Tnzi.Documents</c>。</b>这样需要转换能力的模块
/// （<c>Tnzi.Storage</c> 的 Office 预览、<c>Tnzi.Signing</c> 的上传件归一）可以只依赖核心，
/// 把 LibreOffice 与 PDF 库的重量留在那个包里 —— 与 <see cref="Storage.IFileReferenceProcessor"/>
/// 让 <c>Tnzi.EFCore</c> 不必依赖 <c>Tnzi.Storage</c> 是同一条路子。
/// </para>
/// <para>
/// <b>消费方一律可选注入</b>（<c>IDocumentConverter? converter = null</c>）：没加载
/// <c>Tnzi.Documents</c> 时它就是 null，此时应退化为「不支持」而不是报错。
/// </para>
/// <para>
/// 与 <c>Tnzi.AI</c> 那边负责「文档转 Markdown 文本」的提取器不是一回事：本接口出的是
/// <b>PDF 字节</b>，服务于渲染、预览与签署；那边出的是文本，服务于 RAG 摄取。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Document primitives are still shaped by their first consumers")]
public interface IDocumentConverter
{
    /// <summary>
    /// 这个转换器**此刻**能不能干活。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="CanConvert"/> <b>正交</b>：那个回答「这个格式在支持列表里吗」（静态事实），
    /// 本属性回答「运行环境齐备吗」（外部依赖是否就位，例如 LibreOffice 装没装）。
    /// <b>两个都为真才真的转得动。</b>
    /// </para>
    /// <para>
    /// 存在的理由是「说能预览、点开 500」这类失败：调用方通常要先决定「要不要把入口显示出来」，
    /// 而只问 <see cref="CanConvert"/> 会在缺少外部依赖时给出肯定答复。
    /// 实现应当把探测结果**缓存**起来 —— 调用方会在列表页上逐行询问。
    /// </para>
    /// <para>默认 <c>true</c>：纯托管、无外部依赖的实现无需覆盖。</para>
    /// </remarks>
    bool IsAvailable => true;

    /// <summary>
    /// 判断该文件名的扩展名是否可转 PDF。
    /// </summary>
    /// <param name="fileName">源文件名（只取扩展名）。</param>
    /// <remarks>
    /// 只看格式，<b>不看运行环境</b>；外部依赖是否就位问 <see cref="IsAvailable"/>。
    /// <c>.pdf</c> 返回 false —— PDF 无需转换，调用方直接透传。
    /// </remarks>
    bool CanConvert(string fileName);

    /// <summary>
    /// <b>这个文件</b>此刻转不转得动。
    /// </summary>
    /// <param name="fileName">源文件名（只取扩展名）。</param>
    /// <remarks>
    /// <para>
    /// 即 <see cref="CanConvert"/> 与 <see cref="IsAvailable"/> 的合取，**要决定「显不显示入口」问这一个就够了**。
    /// </para>
    /// <para>
    /// ★ 存在的理由是「一个实现背后可能有多个引擎」：框架的默认实现把 HTML 交给浏览器、
    /// 其余交给 LibreOffice，此时 <see cref="IsAvailable"/> 只能回答「有没有任何引擎能干活」，
    /// 单独拿它去判断 <c>.docx</c> 会在「装了浏览器但没装 LibreOffice」的宿主上答成能预览、点开才 500。
    /// 默认实现是那个合取，只有一个引擎的转换器无需覆盖。
    /// </para>
    /// </remarks>
    bool IsAvailableFor(string fileName) => IsAvailable && CanConvert(fileName);

    /// <summary>
    /// 把源文档转成 PDF。
    /// </summary>
    /// <param name="source">源文档字节。</param>
    /// <param name="sourceFileName">源文件名；<b>只有扩展名会被采用</b>（决定临时文件名），其余部分丢弃。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>PDF 字节。</returns>
    /// <remarks>
    /// 失败时抛 <c>Tnzi.Documents.Exceptions.DocumentConversionException</c>（<see cref="Exceptions.InfrastructureException"/>
    /// 的子类，定义在实现所在的可选包里）：转换器不可用、扩展名不支持、超时、外部进程失败或没有产出。
    /// 只依赖核心的调用方按 <see cref="Exceptions.InfrastructureException"/> 捕获即可。
    /// </remarks>
    Task<byte[]> ConvertToPdfAsync(byte[] source, string sourceFileName, CancellationToken ct = default);
}
