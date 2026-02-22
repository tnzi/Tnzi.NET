namespace Tnzi.System.Options;

/// <summary>
/// 应用程序配置选项
/// 从配置文件读取标准字段
/// 配置路径：App
/// </summary>
public class ApplicationOptions
{
    /// <summary>
    /// 获取或设置 应用程序名称
    /// </summary>
    public string AppName { get; set; } = "Tnzi.NET";

    /// <summary>
    /// 获取或设置 站点名称
    /// </summary>
    public string SiteName { get; set; } = "Tnzi.NET";

    /// <summary>
    /// 获取或设置 前端URL
    /// </summary>
    public string? FrontendUrl { get; set; }

    /// <summary>
    /// 获取或设置 后端API基础URL（用于生成邮箱确认等回调链接）
    /// 例如：https://api.example.com 或 https://example.com/api
    /// </summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>
    /// 获取或设置 联系邮箱
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 获取或设置 联系电话
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// 获取或设置 公司/组织名称
    /// </summary>
    public string? CompanyName { get; set; }

    /// <summary>
    /// 获取或设置 公司地址
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// 获取或设置 网站URL
    /// </summary>
    public string? WebsiteUrl { get; set; }

    /// <summary>
    /// 获取或设置 Logo URL
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// 获取或设置 版权信息
    /// </summary>
    public string? Copyright { get; set; }

    /// <summary>
    /// 获取或设置 ICP备案号
    /// </summary>
    public string? IcpNumber { get; set; }
}

