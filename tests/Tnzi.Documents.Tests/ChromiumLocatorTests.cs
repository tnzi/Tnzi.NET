namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="ChromiumLocator"/> 的配置优先规则与错误消息。
/// </summary>
/// <remarks>
/// 刻意不断言「本机能探测到浏览器」：那取决于机器上装了什么，写进断言只会让 CI 变脆。
/// 这里只钉住与机器无关的两条 —— 配了路径就不回退、错误消息要能把人指到配置键上。
/// </remarks>
public class ChromiumLocatorTests
{
    [Fact]
    public void Resolve_ConfiguredPathThatDoesNotExist_ReturnsNull_WithoutFallingBackToAutoDetection()
    {
        // 配错路径必须立刻失败：悄悄换一个版本的浏览器跑，出的 PDF 会莫名其妙地变样
        var missing = Path.Combine(Path.GetTempPath(), "tnzi-no-such-browser-" + Guid.NewGuid().ToString("N"));

        ChromiumLocator.Resolve(missing).ShouldBeNull();
    }

    [Fact]
    public void Resolve_ConfiguredFile_IsTakenAsIs()
    {
        // 只要求「是个存在的文件」：真不是浏览器的话，启动那一步会带着它的输出报错
        var file = Path.Combine(Path.GetTempPath(), "tnzi-browser-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(file, string.Empty);

        try
        {
            ChromiumLocator.Resolve(file).ShouldBe(file);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Resolve_ConfiguredDirectory_FindsAKnownBrowserExecutableInside()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tnzi-browser-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "msedge.exe" : "chromium");
        File.WriteAllText(executable, string.Empty);

        try
        {
            ChromiumLocator.Resolve(directory).ShouldBe(executable);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NotFoundMessage_WithConfiguredPath_PointsAtTheConfigurationKey()
    {
        var message = ChromiumLocator.NotFoundMessage(@"C:\nope\chrome.exe");

        message.ShouldContain("Documents:Html:BrowserPath");
        message.ShouldContain(@"C:\nope\chrome.exe");
    }

    [Fact]
    public void NotFoundMessage_WithoutConfiguredPath_NamesTheBrowsersAndTheOptOut()
    {
        var message = ChromiumLocator.NotFoundMessage(null);

        message.ShouldContain("Chrome");
        message.ShouldContain("Edge");
        // 找不到浏览器的人需要知道还有一条显式退路，否则只会以为功能坏了
        message.ShouldContain("Documents:Html:Enabled");
    }
}
