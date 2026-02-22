namespace Tnzi.Services;

/// <summary>
/// UserAgent解析服务接口
/// </summary>
public interface IUserAgentParserService
{
    /// <summary>
    /// 解析UserAgent字符串
    /// </summary>
    /// <param name="userAgent">UserAgent字符串</param>
    /// <returns>解析结果</returns>
    UserAgentInfo Parse(string? userAgent);
}

/// <summary>
/// UserAgent信息
/// </summary>
public class UserAgentInfo
{
    /// <summary>
    /// 获取或设置 浏览器名称
    /// </summary>
    public string? Browser { get; set; }

    /// <summary>
    /// 获取或设置 浏览器版本
    /// </summary>
    public string? BrowserVersion { get; set; }

    /// <summary>
    /// 获取或设置 操作系统名称
    /// </summary>
    public string? OperatingSystem { get; set; }

    /// <summary>
    /// 获取或设置 操作系统版本
    /// </summary>
    public string? OperatingSystemVersion { get; set; }

    /// <summary>
    /// 获取或设置 设备类型（Desktop, Mobile, Tablet, Bot等）
    /// </summary>
    public string? DeviceType { get; set; }

    /// <summary>
    /// 获取或设置 设备品牌
    /// </summary>
    public string? DeviceBrand { get; set; }

    /// <summary>
    /// 获取或设置 设备型号
    /// </summary>
    public string? DeviceModel { get; set; }

    /// <summary>
    /// 获取或设置 是否为移动设备
    /// </summary>
    public bool IsMobile { get; set; }

    /// <summary>
    /// 获取或设置 是否为机器人/爬虫
    /// </summary>
    public bool IsBot { get; set; }

    /// <summary>
    /// 获取或设置 原始UserAgent字符串
    /// </summary>
    public string? RawUserAgent { get; set; }
}

