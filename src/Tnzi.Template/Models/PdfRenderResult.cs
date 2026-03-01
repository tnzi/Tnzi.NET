namespace Tnzi.Template.Models;

/// <summary>
/// PDF 渲染结果
/// </summary>
public class PdfRenderResult
{
    /// <summary>
    /// 输出字节数组
    /// </summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 内容类型（如 application/pdf 或 text/html）
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// 文件扩展名（如 .pdf 或 .html）
    /// </summary>
    public string FileExtension { get; set; } = string.Empty;

    /// <summary>
    /// 使用的模板名称
    /// </summary>
    public string TemplateName { get; set; } = string.Empty;
}
