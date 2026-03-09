
namespace Tnzi.Identity.Tests;

public class SessionServiceTests
{
    private readonly Mock<IRepository<UserSession, Guid>> _repositoryMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly DatabaseSessionService _sessionService;

    public SessionServiceTests()
    {
        _repositoryMock = new Mock<IRepository<UserSession, Guid>>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _sessionService = new DatabaseSessionService(_repositoryMock.Object, _serviceProviderMock.Object);
    }

    [Fact]
    public async Task CreateSessionAsync_WithValidInput_CreatesSession()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var deviceInfo = "Windows 10";
        var ipAddress = "127.0.0.1";
        var userAgent = "Test Browser";

        _repositoryMock.Setup(x => x.InsertAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()))
            .Callback<UserSession, CancellationToken>((s, c) => s.Id = Guid.NewGuid())
            .Returns(Task.CompletedTask);

        // Act
        var sessionId = await _sessionService.CreateSessionAsync(userId, deviceInfo, ipAddress, userAgent);

        // Assert
        Assert.NotEqual(Guid.Empty, sessionId);
        _repositoryMock.Verify(x => x.InsertAsync(It.Is<UserSession>(s =>
            s.UserId == userId &&
            s.DeviceInfo == deviceInfo &&
            s.IpAddress == ipAddress &&
            s.UserAgent == userAgent)), Times.Once);
    }

    [Fact]
    public async Task RevokeSessionAsync_WithValidSessionId_RevokesSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new UserSession
        {
            Id = sessionId,
            UserId = Guid.NewGuid(),
            IsRevoked = false
        };

        _repositoryMock.Setup(x => x.GetAsync(sessionId))
            .ReturnsAsync(session);

        _repositoryMock.Setup(x => x.UpdateAsync(session))
            .Returns(Task.CompletedTask);

        // Act
        await _sessionService.RevokeSessionAsync(sessionId);

        // Assert
        Assert.True(session.IsRevoked);
        Assert.NotNull(session.RevokedAt);
        _repositoryMock.Verify(x => x.UpdateAsync(session), Times.Once);
    }

    [Fact]
    public async Task UpdateActivityTimeAsync_WithValidSessionId_UpdatesActivityTime()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var oldTime = DateTime.UtcNow.AddHours(-1);
        var session = new UserSession
        {
            Id = sessionId,
            UserId = Guid.NewGuid(),
            IsRevoked = false,
            LastActivityTime = oldTime
        };

        _repositoryMock.Setup(x => x.GetAsync(sessionId))
            .ReturnsAsync(session);

        _repositoryMock.Setup(x => x.UpdateAsync(session))
            .Returns(Task.CompletedTask);

        // Act
        await _sessionService.UpdateActivityTimeAsync(sessionId);

        // Assert
        Assert.True(session.LastActivityTime > oldTime);
        _repositoryMock.Verify(x => x.UpdateAsync(session), Times.Once);
    }
}
