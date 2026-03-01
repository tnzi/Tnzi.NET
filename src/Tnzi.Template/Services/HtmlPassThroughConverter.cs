namespace Tnzi.Template.Services;

/// <summary>
/// 默认 HTML 直出转换器
/// 直接返回 HTML 字节，不做 PDF 转换
/// 适用于邮件发送、浏览器查看等场景
/// 如需真实 PDF 输出，请在应用层注册自定义 IHtmlToPdfConverter 实现
/// </summary>
public class HtmlPassThroughConverter : IHtmlToPdfConverter
{
    /// <inheritdoc />
    public string ContentType => "text/html";

    /// <inheritdoc />
    public string FileExtension => ".html";

    /// <inheritdoc />
    public Task<byte[]> ConvertAsync(string htmlContent, PdfConvertOptions? options = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(htmlContent);
        return Task.FromResult(Encoding.UTF8.GetBytes(htmlContent));
    }
}
