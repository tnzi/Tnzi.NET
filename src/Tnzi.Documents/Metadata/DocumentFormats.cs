namespace Tnzi.Documents.Metadata;

/// <summary>
/// 文档格式常量。
/// </summary>
public static class DocumentFormats
{
    /// <summary>PDF 的 MIME 类型。</summary>
    public const string PdfContentType = "application/pdf";

    /// <summary>PDF 扩展名（含点号）。</summary>
    public const string PdfExtension = ".pdf";

    /// <summary>
    /// HTML 扩展名（含点号，大小写不敏感）。
    /// </summary>
    /// <remarks>
    /// 单列出来是因为 HTML **走的不是 LibreOffice**：它由
    /// <see cref="Services.ChromiumHtmlDocumentConverter"/> 交给本机浏览器渲染，
    /// 只有浏览器才认得全 CSS。这份名单是那条路径的输入白名单，也是
    /// <see cref="Services.RoutingDocumentConverter"/> 的分流依据。
    /// </remarks>
    public static readonly IReadOnlySet<string> HtmlExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".htm", ".html" };

    /// <summary>
    /// 可转 PDF 的源格式扩展名（含点号，大小写不敏感）。
    /// </summary>
    /// <remarks>
    /// <see cref="PdfExtension"/> **不在**其中：PDF 无需转换，调用方直接透传即可。
    /// 这份名单同时是转换器的输入白名单 —— 上传文件名只有扩展名会被采用，
    /// 且必须命中本名单，避免用文件名影响外部进程的命令行。
    /// </remarks>
    public static readonly IReadOnlySet<string> ConvertibleExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 文字处理
            ".doc", ".docx", ".docm", ".dot", ".dotx", ".odt", ".rtf", ".txt", ".fodt",
            // 表格
            ".xls", ".xlsx", ".xlsm", ".ods", ".csv", ".fods",
            // 演示
            ".ppt", ".pptx", ".odp", ".fodp",
            // 网页
            ".htm", ".html"
        };
}
