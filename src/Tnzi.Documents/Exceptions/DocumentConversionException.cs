namespace Tnzi.Documents.Exceptions;

/// <summary>
/// 文档转 PDF 失败。
/// </summary>
/// <remarks>
/// 属基础设施异常：外部转换器（LibreOffice）缺失、超时、非零退出、无输出，都落到这里。
/// 转换器是原语而非应用服务，故抛异常而不是返回 <c>Result</c>，由调用方决定怎么呈现。
/// </remarks>
public class DocumentConversionException : InfrastructureException
{
    /// <summary>初始化一个 <see cref="DocumentConversionException"/> 实例。</summary>
    /// <param name="message">异常消息（面向开发者，英文）。</param>
    /// <param name="isRetryable">是否可重试（超时、进程被抢占等瞬时故障为 true）。</param>
    /// <param name="innerException">内部异常。</param>
    public DocumentConversionException(string message, bool isRetryable = false, Exception? innerException = null)
        : base("DocumentConverter", message, isRetryable, innerException)
    {
    }
}
