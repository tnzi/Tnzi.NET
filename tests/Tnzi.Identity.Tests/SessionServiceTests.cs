using System.Linq.Expressions;

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

    // IReadOnlyRepository<T> 本身是 IQueryable<T>；_repository.Where(...)/.AnyAsync() 走 LINQ/EF
    // 扩展方法，需把仓储 mock 的 IQueryable 面接到 MockQueryable（支持 async LINQ）。
    private void SetupSessions(params UserSession[] sessions)
    {
        var mockQueryable = sessions.ToList().BuildMock();
        _repositoryMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
        _repositoryMock.As<IQueryable<UserSession>>().Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _repositoryMock.As<IQueryable<UserSession>>().Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _repositoryMock.As<IQueryable<UserSession>>().Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _repositoryMock.As<IQueryable<UserSession>>().Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
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

    [Fact]
    public async Task IsSessionValidAsync_WithActiveUnexpiredSession_ReturnsTrue()
    {
        var sessionId = Guid.NewGuid();
        SetupSessions(new UserSession
        {
            Id = sessionId,
            UserId = Guid.NewGuid(),
            IsRevoked = false,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });

        var valid = await _sessionService.IsSessionValidAsync(sessionId);

        Assert.True(valid);
    }

    [Fact]
    public async Task IsSessionValidAsync_WithRevokedSession_ReturnsFalse()
    {
        var sessionId = Guid.NewGuid();
        SetupSessions(new UserSession
        {
            Id = sessionId,
            UserId = Guid.NewGuid(),
            IsRevoked = true,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });

        var valid = await _sessionService.IsSessionValidAsync(sessionId);

        Assert.False(valid);
    }

    [Fact]
    public async Task IsSessionValidAsync_WithExpiredSession_ReturnsFalse()
    {
        var sessionId = Guid.NewGuid();
        SetupSessions(new UserSession
        {
            Id = sessionId,
            UserId = Guid.NewGuid(),
            IsRevoked = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        });

        var valid = await _sessionService.IsSessionValidAsync(sessionId);

        Assert.False(valid);
    }

    [Fact]
    public async Task IsSessionValidAsync_WithEmptyGuid_ReturnsFalseWithoutQuery()
    {
        // No queryable configured → if it did NOT short-circuit on Guid.Empty, the
        // Where/AnyAsync call would throw (null Provider). Returning false cleanly
        // proves the short-circuit.
        var valid = await _sessionService.IsSessionValidAsync(Guid.Empty);

        Assert.False(valid);
    }

    [Fact]
    public async Task RenewSessionAsync_ExtendsExpiryAndActivity()
    {
        var sessionId = Guid.NewGuid();
        var session = new UserSession
        {
            Id = sessionId,
            UserId = Guid.NewGuid(),
            IsRevoked = false,
            LastActivityTime = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _repositoryMock.Setup(x => x.GetAsync(sessionId)).ReturnsAsync(session);
        _repositoryMock.Setup(x => x.UpdateAsync(session)).Returns(Task.CompletedTask);

        var newExpiry = DateTime.UtcNow.AddDays(7);
        await _sessionService.RenewSessionAsync(sessionId, newExpiry);

        Assert.Equal(newExpiry, session.ExpiresAt);
        Assert.True(session.LastActivityTime > DateTime.UtcNow.AddMinutes(-1));
        _repositoryMock.Verify(x => x.UpdateAsync(session), Times.Once);
    }

    [Fact]
    public async Task RenewSessionAsync_OnRevokedSession_DoesNothing()
    {
        var sessionId = Guid.NewGuid();
        var session = new UserSession { Id = sessionId, IsRevoked = true };
        _repositoryMock.Setup(x => x.GetAsync(sessionId)).ReturnsAsync(session);

        await _sessionService.RenewSessionAsync(sessionId, DateTime.UtcNow.AddDays(7));

        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserSession>()), Times.Never);
    }
}
