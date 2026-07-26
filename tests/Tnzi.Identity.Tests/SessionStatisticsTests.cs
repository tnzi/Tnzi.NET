
namespace Tnzi.Identity.Tests;

/// <summary>
/// 会话统计测试（GetSessionStatisticsAsync）- Top device 聚合：
/// 忽略 null/空 DeviceInfo、排除已撤销会话、按出现次数倒序。
/// </summary>
public class SessionStatisticsTests
{
    private readonly Mock<IRepository<UserSession, Guid>> _repositoryMock;
    private readonly DatabaseSessionService _sessionService;

    public SessionStatisticsTests()
    {
        _repositoryMock = new Mock<IRepository<UserSession, Guid>>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _sessionService = new DatabaseSessionService(_repositoryMock.Object, serviceProviderMock.Object);
    }

    private void SetupSessionQueryable(List<UserSession> sessions)
    {
        var mock = sessions.BuildMock();
        _repositoryMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
        _repositoryMock.As<IQueryable<UserSession>>().Setup(q => q.Provider).Returns(mock.Provider);
        _repositoryMock.As<IQueryable<UserSession>>().Setup(q => q.Expression).Returns(mock.Expression);
        _repositoryMock.As<IQueryable<UserSession>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _repositoryMock.As<IQueryable<UserSession>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());
    }

    private static UserSession MakeSession(Guid userId, string? deviceInfo, bool isRevoked = false) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        DeviceInfo = deviceInfo,
        CreationTime = DateTime.UtcNow.AddHours(-1),
        LastActivityTime = DateTime.UtcNow,
        IsRevoked = isRevoked,
        RevokedAt = isRevoked ? DateTime.UtcNow : null
    };

    [Fact]
    public async Task GetSessionStatisticsAsync_AggregatesTopDevices_IgnoringNullAndEmpty()
    {
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var u3 = Guid.NewGuid();
        SetupSessionQueryable(
        [
            MakeSession(u1, "Chrome on Windows"),
            MakeSession(u1, "Chrome on Windows"),
            MakeSession(u2, "Chrome on Windows"),
            MakeSession(u2, "Safari on iOS"),
            MakeSession(u3, null),
            MakeSession(u3, string.Empty),
            MakeSession(Guid.NewGuid(), "Chrome on Windows", isRevoked: true)
        ]);

        var result = await _sessionService.GetSessionStatisticsAsync();

        result.Succeeded.ShouldBeTrue(result.Message);
        var stats = result.Data!;
        stats.ActiveSessionCount.ShouldBe(6);
        stats.OnlineUserCount.ShouldBe(3);

        var topDevices = stats.TopDevices.ToList();
        topDevices.Count.ShouldBe(2);
        topDevices[0].DeviceInfo.ShouldBe("Chrome on Windows");
        topDevices[0].Count.ShouldBe(3); // 已撤销会话不计入
        topDevices[1].DeviceInfo.ShouldBe("Safari on iOS");
        topDevices[1].Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetSessionStatisticsAsync_WithNoDeviceInfo_ReturnsEmptyTopDevices()
    {
        SetupSessionQueryable(
        [
            MakeSession(Guid.NewGuid(), null),
            MakeSession(Guid.NewGuid(), string.Empty)
        ]);

        var result = await _sessionService.GetSessionStatisticsAsync();

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ActiveSessionCount.ShouldBe(2);
        result.Data.TopDevices.ShouldBeEmpty();
    }
}
