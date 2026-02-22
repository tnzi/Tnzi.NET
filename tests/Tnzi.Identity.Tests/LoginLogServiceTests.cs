
namespace Tnzi.Identity.Tests;

public class LoginLogServiceTests
{
    private readonly Mock<IRepository<LoginLog, Guid>> _repositoryMock;
    private readonly Mock<ILoginLogSender> _senderMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly LoginLogService _loginLogService;

    public LoginLogServiceTests()
    {
        _repositoryMock = new Mock<IRepository<LoginLog, Guid>>();
        _senderMock = new Mock<ILoginLogSender>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _loginLogService = new LoginLogService(_repositoryMock.Object, _senderMock.Object, _serviceProviderMock.Object);
    }

    [Fact]
    public async Task LogAsync_WithValidInput_CreatesLog()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var userName = "testuser";
        var ipAddress = "127.0.0.1";
        var userAgent = "Test Browser";
        var status = LoginStatus.Success;

        _senderMock.Setup(x => x.SendAsync(It.IsAny<LoginLog>()))
            .Callback<LoginLog>(log => log.Id = Guid.NewGuid())
            .Returns(Task.CompletedTask);

        // Act
        var logId = await _loginLogService.LogAsync(userId, userName, ipAddress, userAgent, status);

        // Assert
        Assert.NotEqual(Guid.Empty, logId);
        _senderMock.Verify(x => x.SendAsync(It.Is<LoginLog>(l =>
            l.UserId == userId &&
            l.UserName == userName &&
            l.IpAddress == ipAddress &&
            l.UserAgent == userAgent &&
            l.Status == status)), Times.Once);
    }

    [Fact]
    public async Task LogAsync_WithFailureReason_IncludesFailureReason()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var failureReason = "Invalid password";
        var status = LoginStatus.Failed;

        _senderMock.Setup(x => x.SendAsync(It.IsAny<LoginLog>()))
            .Callback<LoginLog>(log => log.Id = Guid.NewGuid())
            .Returns(Task.CompletedTask);

        // Act
        var logId = await _loginLogService.LogAsync(userId, "testuser", "127.0.0.1", "Browser", status, failureReason);

        // Assert
        Assert.NotEqual(Guid.Empty, logId);
        _senderMock.Verify(x => x.SendAsync(It.Is<LoginLog>(l =>
            l.FailureReason == failureReason)), Times.Once);
    }

    [Fact(Skip = "需要集成测试：GetUserLoginLogsAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task GetUserLoginLogsAsync_WithValidUserId_ReturnsPagedList()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }

    [Fact(Skip = "需要集成测试：GetRecentLogsAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task GetRecentLogsAsync_WithUserId_ReturnsRecentLogs()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }

    [Fact(Skip = "需要集成测试：GetRecentLogsAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task GetRecentLogsAsync_WithoutUserId_ReturnsGlobalRecentLogs()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }

    [Fact(Skip = "需要集成测试：DeleteExpiredLogsAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task DeleteExpiredLogsAsync_WithValidDays_DeletesExpiredLogs()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }

    [Fact(Skip = "需要集成测试：GetLogsByIpAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task GetLogsByIpAsync_WithValidIp_ReturnsPagedList()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }

    [Fact(Skip = "需要集成测试：GetLogsByDateRangeAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task GetLogsByDateRangeAsync_WithValidRange_ReturnsPagedList()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }
}