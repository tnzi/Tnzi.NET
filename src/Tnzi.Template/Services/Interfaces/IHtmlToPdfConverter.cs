namespace Tnzi.Template.Services;

/// <summary>
/// HTML 转 PDF 转换器接口
/// 将渲染后的 HTML 内容转换为 PDF（或其他格式）字节数组
/// 框架提供默认的 HTML 直出实现，应用层可替换为 PuppeteerSharp/Gotenberg/wkhtmltopdf 等实现
/// </summary>
public interface IHtmlToPdfConverter
{
    /// <summary>
    /// 将 HTML 内容转换为 PDF 字节数组
    /// </summary>
    /// <param name="htmlContent">渲染后的 HTML 内容</param>
    /// <param name="options">转换选项（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>输出字节数组</returns>
    Task<byte[]> ConvertAsync(string htmlContent, PdfConvertOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 输出内容类型（如 application/pdf 或 text/html）
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// 输出文件扩展名（如 .pdf 或 .html）
    /// </summary>
    string FileExtension { get; }
}

/// <summary>
/// PDF 转换选项
/// </summary>
public class PdfConvertOptions
{
    /// <summary>
    /// 纸张格式（默认 A4）
    /// </summary>
    public string PaperFormat { get; set; } = "A4";

    /// <summary>
    /// 是否横向
    /// </summary>
    public bool Landscape { get; set; }

    /// <summary>
    /// 是否打印背景
    /// </summary>
    public bool PrintBackground { get; set; } = true;

    /// <summary>
    /// 页边距（单位 mm）
    /// </summary>
    public PdfMargins? Margins { get; set; }

    /// <summary>
    /// 自定义页眉 HTML
    /// </summary>
    public string? HeaderHtml { get; set; }

    /// <summary>
    /// 自定义页脚 HTML
    /// </summary>
    public string? FooterHtml { get; set; }
}

/// <summary>
/// PDF 页边距
/// </summary>
public class PdfMargins
{
    /// <summary>
    /// 上边距（mm）
    /// </summary>
    public int Top { get; set; } = 10;

    /// <summary>
    /// 下边距（mm）
    /// </summary>
    public int Bottom { get; set; } = 10;

    /// <summary>
    /// 左边距（mm）
    /// </summary>
    public int Left { get; set; } = 10;

    /// <summary>
    /// 右边距（mm）
    /// </summary>
    public int Right { get; set; } = 10;
}
