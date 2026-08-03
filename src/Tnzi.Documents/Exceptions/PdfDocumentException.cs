namespace Tnzi.Documents.Exceptions;

/// <summary>
/// PDF 读取或写入失败。
/// </summary>
/// <remarks>
/// 用于把 PdfPig / PDFsharp 的底层异常包成一致的框架异常并保留 <see cref="Exception.InnerException"/>：
/// 字节不是合法 PDF、文档被加密、正则模式非法、页上无可用字体等，都落到这里。
/// 调用方传错页码这类**调用方错误**不走这里，走 <see cref="ArgumentOutOfRangeException"/>。
/// </remarks>
public class PdfDocumentException : InfrastructureException
{
    /// <summary>初始化一个 <see cref="PdfDocumentException"/> 实例。</summary>
    /// <param name="message">异常消息（面向开发者，英文）。</param>
    /// <param name="innerException">内部异常。</param>
    public PdfDocumentException(string message, Exception? innerException = null)
        : base("Pdf", message, isRetryable: false, innerException)
    {
    }
}
