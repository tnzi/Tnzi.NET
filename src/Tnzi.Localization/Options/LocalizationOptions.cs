namespace Tnzi.Localization.Options;

/// <summary>
/// 资源文件格式
/// </summary>
public enum ResourceFormat
{
    /// <summary>
    /// 使用 .resx 资源文件（默认）
    /// </summary>
    Resx = 0,

    /// <summary>
    /// 使用 JSON 资源文件
    /// </summary>
    Json = 1
}

/// <summary>
/// 本地化配置选项
/// </summary>
public class LocalizationOptions
{
    /// <summary>
    /// 是否启用本地化（默认 true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 资源文件格式（默认 Resx）
    /// </summary>
    public ResourceFormat ResourceFormat { get; set; } = ResourceFormat.Resx;

    /// <summary>
    /// 资源文件路径（默认 "Resources"）
    /// </summary>
    public string? ResourcesPath { get; set; } = "Resources";

    /// <summary>
    /// 默认语言（默认 "en"）
    /// </summary>
    public string? DefaultCulture { get; set; } = "en";

    /// <summary>
    /// 支持的语言列表（默认 ["en"]）
    /// </summary>
    public string[]? SupportedCultures { get; set; }

    /// <summary>
    /// 是否从查询字符串检测语言（如 ?culture=zh-CN）
    /// </summary>
    public bool QueryStringCultureProvider { get; set; } = true;

    /// <summary>
    /// 是否从Cookie检测语言
    /// </summary>
    public bool CookieCultureProvider { get; set; } = true;

    /// <summary>
    /// 是否从Accept-Language header检测语言（最常用）
    /// </summary>
    public bool AcceptLanguageHeaderCultureProvider { get; set; } = true;
}
