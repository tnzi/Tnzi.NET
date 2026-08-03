namespace Tnzi.Documents.Options;

/// <summary>
/// 文档原语配置（配置节 <c>Documents</c>）。
/// </summary>
/// <remarks>
/// 只有 Office 转 PDF 需要配置（要跑外部进程）；PDF 读取与盖章是纯托管的，无配置项。
/// </remarks>
[ConfigSection("Documents")]
public class DocumentsOptions
{
    /// <summary>
    /// LibreOffice 可执行文件（<c>soffice</c> / <c>soffice.exe</c>）路径，也可给它所在目录。
    /// </summary>
    /// <remarks>
    /// 为空时按各操作系统的常见安装路径 + <c>PATH</c> 自动探测。
    /// **显式配置了就不再回退探测** —— 配错路径要立刻报错，而不是悄悄换一个版本跑。
    /// </remarks>
    public string? LibreOfficePath { get; set; }

    /// <summary>单次转换的超时秒数，默认 120。</summary>
    /// <remarks>
    /// 实测（Windows + LibreOffice 25.8）冷 profile 首次约 4.9s，之后暖启动约 1.4s；
    /// 120s 留足了首启动与大文档的余量。超时后整棵进程树会被杀掉。
    /// </remarks>
    public int ConversionTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// LibreOffice 用户 profile 目录（绝对路径）。
    /// </summary>
    /// <remarks>
    /// 为空时用临时目录下的固定子目录 <c>tnzi-libreoffice-profile</c>。
    /// 该目录**长期复用、不逐次清理**：冷 profile 引导要多花几秒，且并发新建 profile 会崩
    /// （见 <see cref="Services.LibreOfficeDocumentConverter"/> 的并发说明）。
    /// </remarks>
    public string? ProfileDirectory { get; set; }
}
