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
    private readonly Mock<ISessionRevocationService> _revocationMock = new();
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
            withSessionService ? _sessionServiceMock.Object : null, userAgentParser: null,
            sessionRevocation: _revocationMock.Object);

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
        // 先建后撤：撤销除新会话外的全部旧会话，**连同它们的刷新令牌**（走撤销出口）。
        _revocationMock.Verify(
            x => x.RevokeUserSessionsAsync(It.IsAny<Guid>(), SessionRevocationReason.MultiLoginReplaced, _newSessionId),
            Times.Once);
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
        // 已有 2 + 新 1 = 3 > 上限 2 → 撤销最旧的 1 个（且只撤最旧那个），连同它的刷新令牌。
        _revocationMock.Verify(
            x => x.RevokeSessionAsync(oldest.Id, SessionRevocationReason.MultiLoginReplaced), Times.Once);
        _revocationMock.Verify(
            x => x.RevokeSessionAsync(newer.Id, It.IsAny<SessionRevocationReason>()), Times.Never);
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


/// <summary>
/// 多登录策略踢人时，<b>刷新令牌必须跟着一起作废</b>。
/// </summary>
/// <remarks>
/// ★★ 只调 <c>ISessionService.RevokeSessionAsync</c> 的话，会话行被标成已撤销而绑定其上的
/// 刷新令牌原样留在库里 —— 「被踢的设备进不来」就完全押在 <c>EnforceSessionValidation</c>
/// 这个逃生开关上。一旦有人把它关掉，被踢的设备能用旧刷新令牌换一枚全新的 access token，
/// <b>单设备登录与限并发一起变成装饰</b>，而管理端的会话列表看上去一切正常。
/// </remarks>
public class LoginSessionCoordinatorRevocationTests
{
    private readonly Mock<ISessionService> _sessionServiceMock = new();
    private readonly Mock<ISessionRevocationService> _revocationMock = new();
    private readonly Mock<IOptionsMonitor<IdentityOptions>> _optionsMock = new();
    private readonly Guid _newSessionId = Guid.NewGuid();
    private readonly LoginSessionCoordinator _coordinator;

    public LoginSessionCoordinatorRevocationTests()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _sessionServiceMock
            .Setup(x => x.CreateSessionAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(_newSessionId);

        _coordinator = new LoginSessionCoordinator(
            serviceProvider.Object, _optionsMock.Object, _sessionServiceMock.Object,
            userAgentParser: null, sessionRevocation: _revocationMock.Object);
    }

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

    /// <summary>单设备登录：踢掉其余会话要走撤销出口（连带删刷新令牌），保留刚建的这条。</summary>
    [Fact]
    public async Task SingleDevice_KicksOthersThroughTheRevocationExit()
    {
        SetOptions(new MultiLoginOptions { AllowMultiLogin = false, OnConflict = LoginConflictPolicy.Replace });
        SetExistingSessions();
        var userId = Guid.NewGuid();

        await _coordinator.EstablishAsync(userId);

        _revocationMock.Verify(
            x => x.RevokeUserSessionsAsync(userId, SessionRevocationReason.MultiLoginReplaced, _newSessionId),
            Times.Once);
        _sessionServiceMock.Verify(
            x => x.RevokeAllSessionsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()), Times.Never);
    }

    /// <summary>限并发：挤掉最旧的那条同样走撤销出口。</summary>
    [Fact]
    public async Task ConcurrencyLimit_KicksOldestThroughTheRevocationExit()
    {
        SetOptions(new MultiLoginOptions
        {
            AllowMultiLogin = true,
            OnConflict = LoginConflictPolicy.Replace,
            MaxConcurrentSessions = 2,
        });

        var now = DateTime.UtcNow;
        var oldest = new UserSessionDto { Id = Guid.NewGuid(), LastActivityTime = now.AddHours(-3), ExpiresAt = now.AddDays(7) };
        var newer = new UserSessionDto { Id = Guid.NewGuid(), LastActivityTime = now.AddMinutes(-5), ExpiresAt = now.AddDays(7) };
        SetExistingSessions(oldest, newer);

        await _coordinator.EstablishAsync(Guid.NewGuid());

        _revocationMock.Verify(
            x => x.RevokeSessionAsync(oldest.Id, SessionRevocationReason.MultiLoginReplaced), Times.Once);
        _revocationMock.Verify(
            x => x.RevokeSessionAsync(newer.Id, It.IsAny<SessionRevocationReason>()), Times.Never);
        _sessionServiceMock.Verify(x => x.RevokeSessionAsync(It.IsAny<Guid>()), Times.Never);
    }

    /// <summary>
    /// ★ 并发计数要排掉已经超过绝对上限的会话 —— 它在每请求校验里早已判死，
    /// 却还占着名额，症状是「明明只登了一台设备，却说已达上限」。
    /// </summary>
    [Fact]
    public async Task AbsolutelyExpiredSessions_DoNotOccupyASlot()
    {
        SetOptions(new MultiLoginOptions
        {
            AllowMultiLogin = true,
            OnConflict = LoginConflictPolicy.Reject,
            MaxConcurrentSessions = 1,
        });

        var now = DateTime.UtcNow;
        SetExistingSessions(new UserSessionDto
        {
            Id = Guid.NewGuid(),
            LastActivityTime = now,
            ExpiresAt = now.AddDays(7),          // 滑动窗口还没到
            AbsoluteExpiresAt = now.AddMinutes(-1), // 但绝对上限过了
        });

        var result = await _coordinator.EstablishAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeTrue();
    }
}
