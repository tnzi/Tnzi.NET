using Tnzi.Identity.Events;
using Tnzi.Identity.Events.Handlers;
using Tnzi.Services;

namespace Tnzi.Identity.Tests;

/// <summary>
/// UserLoggedInEventHandler 测试 — 会话创建时从 UserAgent 解析 DeviceInfo
/// （供 Sessions 统计 Top device 聚合），解析器缺失或 UA 为空时回退 null。
/// </summary>
public class UserLoggedInEventHandlerTests
{
    private const string ChromeWindowsUa =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";

    private readonly Mock<ISessionService> _sessionServiceMock;

    public UserLoggedInEventHandlerTests()
    {
        _sessionServiceMock = new Mock<ISessionService>();
        _sessionServiceMock
            .Setup(s => s.CreateSessionAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(Guid.NewGuid());
    }

    private static UserLoggedInEvent CreateEvent(string? userAgent) => new()
    {
        UserId = Guid.NewGuid(),
        UserName = "tester",
        LoginTime = DateTime.UtcNow,
        IpAddress = "127.0.0.1",
        UserAgent = userAgent
    };

    [Fact]
    public async Task HandleAsync_WithParser_DerivesDeviceInfoFromUserAgent()
    {
        var handler = new UserLoggedInEventHandler(
            sessionService: _sessionServiceMock.Object,
            userAgentParser: new UserAgentParserService());

        await handler.HandleAsync(CreateEvent(ChromeWindowsUa));

        _sessionServiceMock.Verify(s => s.CreateSessionAsync(
            It.IsAny<Guid>(), "Chrome on Windows", "127.0.0.1", ChromeWindowsUa), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithoutParser_PassesNullDeviceInfo()
    {
        var handler = new UserLoggedInEventHandler(sessionService: _sessionServiceMock.Object);

        await handler.HandleAsync(CreateEvent(ChromeWindowsUa));

        _sessionServiceMock.Verify(s => s.CreateSessionAsync(
            It.IsAny<Guid>(), null, "127.0.0.1", ChromeWindowsUa), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithBlankUserAgent_PassesNullDeviceInfo()
    {
        var handler = new UserLoggedInEventHandler(
            sessionService: _sessionServiceMock.Object,
            userAgentParser: new UserAgentParserService());

        await handler.HandleAsync(CreateEvent("   "));

        _sessionServiceMock.Verify(s => s.CreateSessionAsync(
            It.IsAny<Guid>(), null, "127.0.0.1", "   "), Times.Once);
    }
}
