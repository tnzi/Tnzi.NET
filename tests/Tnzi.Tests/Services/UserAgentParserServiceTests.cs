using Tnzi.Services;

namespace Tnzi.Tests.Services;

/// <summary>
/// UA 解析要按特异性识别浏览器：Chromium 系浏览器（Edge、Opera）与 iOS 上的 Chrome / Firefox 都在 UA 里带着
/// <c>Chrome/</c> 或 <c>Safari/</c>，按字符串里最左出现的令牌取名会把它们统统记成 Chrome 或 Safari。
/// 解析结果写进访问日志的浏览器与机型列，也是登录会话的设备文案。
/// </summary>
public class UserAgentParserServiceTests
{
    private readonly UserAgentParserService _parser = new();

    [Theory]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.2210.91",
        "Microsoft Edge", "120.0.2210.91")]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 OPR/106.0.0.0",
        "Opera", "106.0.0.0")]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Chrome", "120.0.0.0")]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:121.0) Gecko/20100101 Firefox/121.0",
        "Firefox", "121.0")]
    [InlineData(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15",
        "Safari", "17.2")]
    [InlineData(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Mobile/15E148 Safari/604.1",
        "Safari", "17.2")]
    [InlineData(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/120.0.6099.119 Mobile/15E148 Safari/604.1",
        "Chrome", "120.0.6099.119")]
    [InlineData(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) FxiOS/121.0 Mobile/15E148 Safari/605.1.15",
        "Firefox", "121.0")]
    [InlineData(
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.6099.144 Mobile Safari/537.36 EdgA/120.0.2210.115",
        "Microsoft Edge", "120.0.2210.115")]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; WOW64; Trident/7.0; rv:11.0) like Gecko",
        "Internet Explorer", "11.0")]
    [InlineData(
        "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.2; Trident/6.0)",
        "Internet Explorer", "10.0")]
    public void Parse_NamesTheBrowserByItsMostSpecificToken(string userAgent, string browser, string version)
    {
        var info = _parser.Parse(userAgent);

        Assert.Equal(browser, info.Browser);
        Assert.Equal(version, info.BrowserVersion);
    }

    [Theory]
    [InlineData(
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.6099.144 Mobile Safari/537.36",
        "Pixel 8")]
    [InlineData(
        "Mozilla/5.0 (Linux; Android 10; SM-G973F Build/QP1A.190711.020; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/81.0.4044.138 Mobile Safari/537.36",
        "SM-G973F")]
    [InlineData(
        "Mozilla/5.0 (Linux; U; Android 4.0.3; ko-kr; LG-L160L Build/IML74K) AppleWebKit/534.30 (KHTML, like Gecko) Version/4.0 Mobile Safari/534.30",
        "LG-L160L")]
    public void Parse_TakesTheAndroidModelFromTheSegmentAfterTheAndroidVersion(string userAgent, string model)
    {
        var info = _parser.Parse(userAgent);

        Assert.Equal("Android", info.OperatingSystem);
        Assert.Equal("Mobile", info.DeviceType);
        Assert.Equal(model, info.DeviceModel);
    }

    [Fact]
    public void Parse_LeavesTheModelEmptyForTheReducedAndroidUserAgent()
    {
        // 精简 UA 把机型固定写成 "K"，那不是一个机型
        var info = _parser.Parse(
            "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36");

        Assert.Equal("Android", info.OperatingSystem);
        Assert.Equal("10", info.OperatingSystemVersion);
        Assert.Null(info.DeviceModel);
        Assert.Equal("Chrome", info.Browser);
    }

    [Theory]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Windows", "10.0", "Desktop")]
    [InlineData(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15",
        "macOS", "10.15.7", "Desktop")]
    [InlineData(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Mobile/15E148 Safari/604.1",
        "iOS", "17.2", "Mobile")]
    [InlineData(
        "Mozilla/5.0 (iPad; CPU OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Mobile/15E148 Safari/604.1",
        "iOS", "17.2", "Tablet")]
    public void Parse_ReadsTheOperatingSystemAndDeviceType(string userAgent, string os, string osVersion, string deviceType)
    {
        var info = _parser.Parse(userAgent);

        Assert.Equal(os, info.OperatingSystem);
        Assert.Equal(osVersion, info.OperatingSystemVersion);
        Assert.Equal(deviceType, info.DeviceType);
    }

    [Fact]
    public void Parse_FlagsCrawlersAsBots()
    {
        var info = _parser.Parse("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)");

        Assert.True(info.IsBot);
        Assert.Equal("Bot", info.DeviceType);
        Assert.Null(info.Browser);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_ReturnsAnEmptyResultForAMissingUserAgent(string? userAgent)
    {
        var info = _parser.Parse(userAgent);

        Assert.Null(info.Browser);
        Assert.Null(info.OperatingSystem);
        Assert.Null(info.DeviceType);
    }
}
