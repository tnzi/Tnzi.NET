
namespace Tnzi.Services;

/// <summary>
/// UserAgent解析服务实现（基于正则表达式）
/// </summary>
public class UserAgentParserService : IUserAgentParserService
{
    /// <summary>
    /// 设备类型常量
    /// </summary>
    private static class DeviceTypes
    {
        public const string Bot = "Bot";
        public const string Mobile = "Mobile";
        public const string Tablet = "Tablet";
        public const string Desktop = "Desktop";
    }

    private static readonly Regex BrowserRegex = new(@"(?<browser>Chrome|Firefox|Safari|Edge|Opera|IE|MSIE|Trident|Edg)[/\s]?(?<version>[\d.]+)?", RegexOptions.IgnoreCase);
    private static readonly Regex OsRegex = new(@"(?<os>Windows|Mac OS X|Linux|Android|iOS|iPhone|iPad)[\s/]?(?<version>[\d._]+)?", RegexOptions.IgnoreCase);
    private static readonly Regex MobileRegex = new(@"(Mobile|Android|iPhone|iPad|iPod|BlackBerry|Windows Phone)", RegexOptions.IgnoreCase);
    private static readonly Regex BotRegex = new(@"(bot|crawler|spider|scraper|indexer|fetcher)", RegexOptions.IgnoreCase);

    /// <summary>
    /// 解析UserAgent字符串
    /// </summary>
    public UserAgentInfo Parse(string? userAgent)
    {
        var info = new UserAgentInfo
        {
            RawUserAgent = userAgent
        };

        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return info;
        }

        // 检测是否为机器人
        info.IsBot = BotRegex.IsMatch(userAgent);
        if (info.IsBot)
        {
            info.DeviceType = DeviceTypes.Bot;
            return info;
        }

        // 检测是否为移动设备
        info.IsMobile = MobileRegex.IsMatch(userAgent);
        if (info.IsMobile)
        {
            info.DeviceType = DeviceTypes.Mobile;
        }
        else if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase))
        {
            info.DeviceType = DeviceTypes.Tablet;
        }
        else
        {
            info.DeviceType = DeviceTypes.Desktop;
        }

        // 解析浏览器
        var browserMatch = BrowserRegex.Match(userAgent);
        if (browserMatch.Success)
        {
            info.Browser = NormalizeBrowserName(browserMatch.Groups["browser"].Value);
            info.BrowserVersion = browserMatch.Groups["version"].Value;
        }

        // 解析操作系统
        var osMatch = OsRegex.Match(userAgent);
        if (osMatch.Success)
        {
            var osName = osMatch.Groups["os"].Value;
            var osVersion = osMatch.Groups["version"].Value;

            info.OperatingSystem = NormalizeOsName(osName);
            info.OperatingSystemVersion = NormalizeOsVersion(osVersion);
        }

        // 解析设备信息（移动设备）
        if (info.IsMobile)
        {
            ParseMobileDevice(userAgent, info);
        }

        return info;
    }

    /// <summary>
    /// 规范化浏览器名称
    /// </summary>
    private string NormalizeBrowserName(string browser)
    {
        return browser.ToLower() switch
        {
            "msie" or "trident" => "Internet Explorer",
            "edg" => "Microsoft Edge",
            _ => browser
        };
    }

    /// <summary>
    /// 规范化操作系统名称
    /// </summary>
    private string NormalizeOsName(string os)
    {
        return os switch
        {
            "Mac OS X" => "macOS",
            "iPhone" or "iPad" => "iOS",
            _ => os
        };
    }

    /// <summary>
    /// 规范化操作系统版本
    /// </summary>
    private string NormalizeOsVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return version;

        // 将下划线替换为点
        return version.Replace("_", ".");
    }

    /// <summary>
    /// 解析移动设备信息
    /// </summary>
    private void ParseMobileDevice(string userAgent, UserAgentInfo info)
    {
        // Android设备
        if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            var androidMatch = Regex.Match(userAgent, @"Android\s+([\d.]+)");
            if (androidMatch.Success)
            {
                info.OperatingSystem = "Android";
                info.OperatingSystemVersion = androidMatch.Groups[1].Value;
            }

            // 提取设备型号
            var deviceMatch = Regex.Match(userAgent, @"\(([^)]+)\)");
            if (deviceMatch.Success)
            {
                var deviceInfo = deviceMatch.Groups[1].Value;
                var parts = deviceInfo.Split(';');
                if (parts.Length > 0)
                {
                    info.DeviceModel = parts[0].Trim();
                }
            }
        }

        // iOS设备
        if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
        {
            info.DeviceBrand = "Apple";
            info.DeviceModel = "iPhone";
            ParseIosVersion(userAgent, info);
        }
        else if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase))
        {
            info.DeviceBrand = "Apple";
            info.DeviceModel = "iPad";
            ParseIosVersion(userAgent, info);
        }
    }

    /// <summary>
    /// 解析 iOS 版本信息
    /// </summary>
    private static void ParseIosVersion(string userAgent, UserAgentInfo info)
    {
        var iosMatch = Regex.Match(userAgent, @"OS\s+([\d_]+)");
        if (iosMatch.Success)
        {
            info.OperatingSystemVersion = iosMatch.Groups[1].Value.Replace("_", ".");
        }
    }
}