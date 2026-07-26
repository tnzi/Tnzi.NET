using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// LoginSessionCoordinator 测试 —— 多设备/单设备/限并发策略在**建立会话时**统一处理，
/// Replace 采用"先建后撤其余"（并发竞态下也收敛），Reject 达上限拒绝本次登录。
/// </summary>
public class LoginSessionCoordinatorTests
{
    private readonly Mock<ISessionService> _sessionServiceMock = new();
    private readonly Mock<IOptionsMonitor<IdentityOptions>> _optionsMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly Guid _newSessionId = Guid.NewGuid();

    public LoginSessionCoordinatorTests()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _sessionServiceMock
            .Setup(x => x.CreateSessionAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(_newSessionId);
        _sessionServiceMock
            .Setup(x => x.RevokeAllSessionsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()))
            .ReturnsAsync(Result.Success());
        _sessionServiceMock
            .Setup(x => x.RevokeSessionAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result.Success());
    }

    private LoginSessionCoordinator CreateCoordinator(bool withSessionService = true)
        => new(_serviceProviderMock.Object, _optionsMock.Object,
            withSessionService ? _sessionServiceMock.Object : null, userAgentParser: null);

    private void SetOptions(MultiLoginOptions multi)
        => _optionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Jwt = new JwtOptions { EnableRefreshToken = true, RefreshTokenExpirationDays = 7 },
            MultiLogin = multi
        });

    private void SetExistingSessions(params UserSessionDto[] sessions)
        => _sessionServiceMock
            .Setup(x => x.GetUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<bool>()))
            .ReturnsAsync(Result<IEnumerable<UserSessionDto>>.Success(sessions));

    private static UserSessionDto Session(DateTime lastActivity)
        => new() { Id = Guid.NewGuid(), IsRevoked = false, LastActivityTime = lastActivity };

    [Fact]
    public async Task EstablishAsync_WithoutSessionService_ReturnsEmptyGuid()
    {
        SetOptions(new MultiLoginOptions());
        var coordinator = CreateCoordinator(withSessionService: false);

        var result = await coordinator.EstablishAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(Guid.Empty);
    }

    [Fact]
    public async Task EstablishAsync_SingleDevice_Replace_CreatesSessionAndRevokesOthers()
    {
        SetOptions(new MultiLoginOptions { AllowMultiLogin = false, OnConflict = LoginConflictPolicy.Replace });
        SetExistingSessions(Session(DateTime.UtcNow.AddMinutes(-5)));
        var coordinator = CreateCoordinator();

        var result = await coordinator.EstablishAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(_newSessionId);
        // 先建后撤：撤销除新会话外的全部旧会话。
        _sessionServiceMock.Verify(x => x.RevokeAllSessionsAsync(It.IsAny<Guid>(), _newSessionId), Times.Once);
    }

    [Fact]
    public async Task EstablishAsync_SingleDevice_Reject_WithExistingSession_Fails()
    {
        SetOptions(new MultiLoginOptions { AllowMultiLogin = false, OnConflict = LoginConflictPolicy.Reject });
        SetExistingSessions(Session(DateTime.UtcNow.AddMinutes(-5)));
        var coordinator = CreateCoordinator();

        var result = await coordinator.EstablishAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.ErrorCode.ShouldBe(ErrorCodes.IDENTITY_SESSION_ALREADY_ACTIVE);
        // 拒绝时不应创建新会话。
        _sessionServiceMock.Verify(x => x.CreateSessionAsync(
            It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>()), Times.Never);
    }

    [Fact]
    public async Task EstablishAsync_MaxConcurrent_Reject_AtLimit_Fails()
    {
        SetOptions(new MultiLoginOptions { AllowMultiLogin = true, MaxConcurrentSessions = 2, OnConflict = LoginConflictPolicy.Reject });
        SetExistingSessions(Session(DateTime.UtcNow.AddMinutes(-5)), Session(DateTime.UtcNow.AddMinutes(-3)));
        var coordinator = CreateCoordinator();

        var result = await coordinator.EstablishAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.IDENTITY_SESSION_LIMIT_REACHED);
    }

    [Fact]
    public async Task EstablishAsync_MaxConcurrent_Replace_OverLimit_RevokesOldest()
    {
        var oldest = Session(DateTime.UtcNow.AddMinutes(-30));
        var newer = Session(DateTime.UtcNow.AddMinutes(-5));
        SetOptions(new MultiLoginOptions { AllowMultiLogin = true, MaxConcurrentSessions = 2, OnConflict = LoginConflictPolicy.Replace });
        SetExistingSessions(oldest, newer);
        var coordinator = CreateCoordinator();

        var result = await coordinator.EstablishAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeTrue();
        // 已有 2 + 新 1 = 3 > 上限 2 → 撤销最旧的 1 个（且只撤最旧那个）。
        _sessionServiceMock.Verify(x => x.RevokeSessionAsync(oldest.Id), Times.Once);
        _sessionServiceMock.Verify(x => x.RevokeSessionAsync(newer.Id), Times.Never);
    }

    [Fact]
    public async Task EstablishAsync_MultiLoginUnlimited_JustCreates()
    {
        SetOptions(new MultiLoginOptions { AllowMultiLogin = true, MaxConcurrentSessions = 0 });
        SetExistingSessions(Session(DateTime.UtcNow.AddMinutes(-5)));
        var coordinator = CreateCoordinator();

        var result = await coordinator.EstablishAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(_newSessionId);
        _sessionServiceMock.Verify(x => x.RevokeAllSessionsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()), Times.Never);
        _sessionServiceMock.Verify(x => x.RevokeSessionAsync(It.IsAny<Guid>()), Times.Never);
    }
}
