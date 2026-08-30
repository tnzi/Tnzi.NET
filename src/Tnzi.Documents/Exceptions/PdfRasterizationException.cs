namespace Tnzi.Documents.Exceptions;

/// <summary>
/// PDF 光栅化失败。
/// </summary>
/// <remarks>
/// <para>
/// 把 PDFium 的底层异常包成一致的框架异常并保留 <see cref="Exception.InnerException"/>：
/// 字节不是合法 PDF、文档被口令保护、原生库没能加载，都落到这里。
/// 调用方传错页码这类**调用方错误**不走这里，走 <see cref="ArgumentOutOfRangeException"/>。
/// </para>
/// <para>
/// ★ <b>与 <see cref="PdfDocumentException"/> 是两个类型，虽然现在住在同一个程序集里。</b>
/// 分开的理由是错误码不同（<c>PdfRaster</c> vs <c>Pdf</c>），而两者代表的失效面也不同：
/// 那个是托管解析出的问题（字节不合法、加密、字体缺失），这个多半是<b>原生库</b>的问题
/// （<c>runtimes/&lt;rid&gt;/native</c> 没随发布产物走、RID 不匹配、精简镜像缺 C 运行时）。
/// 排障时第一步要问的问题不一样，合成一个类型只会让日志少掉这条线索。
/// </para>
/// </remarks>
public class PdfRasterizationException : InfrastructureException
{
    /// <summary>初始化一个 <see cref="PdfRasterizationException"/> 实例。</summary>
    /// <param name="message">异常消息（面向开发者，英文）。</param>
    /// <param name="innerException">内部异常。</param>
    public PdfRasterizationException(string message, Exception? innerException = null)
        : base("PdfRaster", message, isRetryable: false, innerException)
    {
    }
}
