
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

    private const RegexOptions TokenOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>精简 UA（Chrome 110 起的默认）把 Android 机型固定写成这个占位值。</summary>
    private const string ReducedAndroidModel = "K";

    /// <summary>
    /// 浏览器令牌，按特异性排序，取第一个命中的。
    /// </summary>
    /// <remarks>
    /// 不能按令牌在字符串里出现的位置取最左者：Chromium 系浏览器（Edge、Opera）的 UA 同时带着 <c>Chrome/</c> 与
    /// <c>Safari/</c>，iOS 上的 Chrome / Firefox 带着 <c>Safari/</c>，而 <c>Edg/</c>、<c>OPR/</c>、<c>CriOS/</c>、<c>FxiOS/</c>
    /// 都排在后面，最左匹配会把它们统统记成 Chrome 或 Safari。Safari 的 <c>Safari/x</c> 是 WebKit 构建号，
    /// 真实版本在 <c>Version/x</c>。
    /// </remarks>
    private static readonly (Regex Token, string Name)[] BrowserTokens =
    [
        (new Regex(@"\b(?:Edg|EdgA|EdgiOS|Edge)/(?<version>[\d.]+)", TokenOptions), "Microsoft Edge"),
        (new Regex(@"\b(?:OPR|Opera)[/\s](?<version>[\d.]+)", TokenOptions), "Opera"),
        (new Regex(@"\bCriOS/(?<version>[\d.]+)", TokenOptions), "Chrome"),
        (new Regex(@"\bFxiOS/(?<version>[\d.]+)", TokenOptions), "Firefox"),
        (new Regex(@"\bFirefox/(?<version>[\d.]+)", TokenOptions), "Firefox"),
        (new Regex(@"\b(?:Chrome|Chromium)/(?<version>[\d.]+)", TokenOptions), "Chrome"),
        (new Regex(@"\bMSIE\s(?<version>[\d.]+)", TokenOptions), "Internet Explorer"),
        (new Regex(@"\bTrident/[\d.]+.*?\brv:(?<version>[\d.]+)", TokenOptions), "Internet Explorer"),
        (new Regex(@"\bVersion/(?<version>[\d.]+).*\bSafari/", TokenOptions), "Safari"),
        (new Regex(@"\bSafari/(?<version>[\d.]+)", TokenOptions), "Safari"),
    ];

    private static readonly Regex OsRegex = new(@"(?<os>Windows NT|Windows|Mac OS X|Linux|Android|iOS|iPhone|iPad)[\s/]?(?<version>[\d._]+)?", RegexOptions.IgnoreCase);
    private static readonly Regex AndroidPlatformRegex = new(@"\(([^)]*\bAndroid\b[^)]*)\)", TokenOptions);
    private static readonly Regex AndroidVersionRegex = new(@"Android\s+([\d.]+)", TokenOptions);
    private static readonly Regex LocaleSegmentRegex = new(@"^[a-z]{2}(?:[-_][a-z]{2,4})?$", TokenOptions);
    private static readonly Regex BuildSuffixRegex = new(@"\s*Build/.*$", TokenOptions);
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
        // 平板判定必须先于移动判定：MobileRegex 本身就匹配 iPad，
        // 若先判 IsMobile，Tablet 分支永远不可达，iPad 会被归为 Mobile
        info.IsMobile = MobileRegex.IsMatch(userAgent);
        if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase))
        {
            info.DeviceType = DeviceTypes.Tablet;
        }
        else if (info.IsMobile)
        {
            info.DeviceType = DeviceTypes.Mobile;
        }
        else
        {
            info.DeviceType = DeviceTypes.Desktop;
        }

        // 解析浏览器
        foreach (var (token, name) in BrowserTokens)
        {
            var browserMatch = token.Match(userAgent);
            if (browserMatch.Success)
            {
                info.Browser = name;
                info.BrowserVersion = browserMatch.Groups["version"].Value;
                break;
            }
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
    /// 规范化操作系统名称
    /// </summary>
    private string NormalizeOsName(string os)
    {
        return os switch
        {
            "Windows NT" => "Windows",
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
            // 平台段以 "Linux;" 开头，OsRegex 先命中的是 Linux
            info.OperatingSystem = "Android";
            var androidMatch = AndroidVersionRegex.Match(userAgent);
            info.OperatingSystemVersion = androidMatch.Success ? androidMatch.Groups[1].Value : null;
            info.DeviceModel = ParseAndroidModel(userAgent);
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
    /// 从 Android 平台段取机型：<c>(Linux; Android 14; Pixel 8)</c> 里 Android 版本之后的那一段。
    /// 跳过 WebView 标记 <c>wv</c>、加密强度 <c>U</c> 与老式 UA 的语言段（<c>ko-kr</c>），去掉 <c>Build/...</c> 后缀；
    /// 精简 UA 的占位值 <c>K</c> 不是机型，返回 null。
    /// </summary>
    private static string? ParseAndroidModel(string userAgent)
    {
        var platform = AndroidPlatformRegex.Match(userAgent);
        if (!platform.Success)
            return null;

        var segments = platform.Groups[1].Value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var androidIndex = Array.FindIndex(segments, s => s.StartsWith("Android", StringComparison.OrdinalIgnoreCase));
        foreach (var segment in segments.Skip(androidIndex + 1))
        {
            if (segment is "wv" or "U" || LocaleSegmentRegex.IsMatch(segment))
                continue;

            var model = BuildSuffixRegex.Replace(segment, string.Empty);
            return model.Length == 0 || model == ReducedAndroidModel ? null : model;
        }

        return null;
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