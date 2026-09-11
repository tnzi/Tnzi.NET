using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 刷新令牌轮换 + 重放检测。
/// </summary>
/// <remarks>
/// 这一组守的是「轮换」与「防护」之间的差别：只换新值不检测重放，被盗令牌被用过之后，
/// 真用户拿到的回答与「令牌过期了」一模一样，于是重新登录一次，而攻击者那条会话继续活着。
/// 每条用例都对应一个具体的失效形态，删掉实现里的哪一步会红写在用例注释里。
/// </remarks>
public class RefreshTokenRotationTests
{
    private const string Provider = "JWT";
    private const string TokenName = "RefreshToken";

    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<ITokenService> _tokenServiceMock;
    private readonly Mock<IAuthTokenService> _authTokenServiceMock;
    private readonly Mock<ISessionService> _sessionServiceMock;
    private readonly Mock<ISessionRevocationService> _sessionRevocationMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<IOptionsMonitor<IdentityOptions>> _identityOptionsMock;
    private readonly AuthService _authService;

    public RefreshTokenRotationTests()
    {
        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<User>>();
        var identityOptions = new Mock<IOptions<Microsoft.AspNetCore.Identity.IdentityOptions>>();
        var logger = new Mock<ILogger<SignInManager<User>>>();
        var schemes = new Mock<IAuthenticationSchemeProvider>();
        var confirmation = new Mock<IUserConfirmation<User>>();
        var signInManager = new Mock<SignInManager<User>>(
            _userManagerMock.Object, contextAccessor.Object, claimsFactory.Object,
            identityOptions.Object, logger.Object, schemes.Object, confirmation.Object);

        _tokenServiceMock = new Mock<ITokenService>();
        _authTokenServiceMock = new Mock<IAuthTokenService>();
        _sessionServiceMock = new Mock<ISessionService>();
        _sessionRevocationMock = new Mock<ISessionRevocationService>();
        _eventBusMock = new Mock<IEventBus>();

        _identityOptionsMock = new Mock<IOptionsMonitor<IdentityOptions>>();
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Jwt = new JwtOptions
            {
                EnableRefreshToken = true,
                AccessTokenExpirationMinutes = 30,
                RefreshTokenExpirationDays = 7,
                RefreshTokenRotationOverlapSeconds = 10,
            }
        });

        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        serviceProvider.Setup(x => x.GetService(typeof(IOptions<SessionOptions>)))
            .Returns(Microsoft.Extensions.Options.Options.Create(new SessionOptions { EnforceSessionValidation = true }));

        _sessionRevocationMock
            .Setup(x => x.RevokeSessionAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>()))
            .ReturnsAsync(1);

        // 守卫链装真实求值器 + 内置守卫：刷新路径要不要过守卫，正是本组的一条用例。
        var guardEvaluator = new LoginGuardEvaluator(
            [new LockedAccountLoginGuard(_userManagerMock.Object)],
            new Mock<ILogger<LoginGuardEvaluator>>().Object);

        _authService = new AuthService(
            _userManagerMock.Object,
            signInManager.Object,
            _tokenServiceMock.Object,
            _identityOptionsMock.Object,
            serviceProvider.Object,
            _eventBusMock.Object,
            authTokenService: _authTokenServiceMock.Object,
            sessionService: _sessionServiceMock.Object,
            loginGuardEvaluator: guardEvaluator,
            sessionRevocation: _sessionRevocationMock.Object);
    }

    private (User User, AuthToken Entry) ArrangeCurrentToken(string value, Guid? sessionId = null)
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u" };
        var entry = new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionId = sessionId ?? Guid.NewGuid(),
            Value = value,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        };

        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync(Provider, TokenName, value)).ReturnsAsync(entry);
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string>());
        _userManagerMock.Setup(x => x.SupportsUserLockout).Returns(true);
        _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(false);
        _sessionServiceMock.Setup(x => x.IsSessionValidAsync(entry.SessionId)).ReturnsAsync(true);
        _sessionServiceMock.Setup(x => x.RenewSessionAsync(entry.SessionId, It.IsAny<DateTime>()))
            .ReturnsAsync(Result.Success());
        _tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("next_refresh");
        _tokenServiceMock.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>(), null, entry.SessionId))
            .Returns("next_access");

        return (user, entry);
    }

    /// <summary>正常轮换：换新值、续期会话、返回新的一对令牌。</summary>
    [Fact]
    public async Task Refresh_WithCurrentToken_RotatesAndRenewsSession()
    {
        var (_, entry) = ArrangeCurrentToken("current");
        _authTokenServiceMock
            .Setup(x => x.RotateRefreshTokenAsync(entry.Id, "current", "next_refresh", It.IsAny<DateTime?>()))
            .ReturnsAsync(true);

        var result = await _authService.RefreshTokenAsync("current");

        Assert.True(result.Succeeded);
        Assert.Equal("next_access", result.Data!.AccessToken);
        Assert.Equal("next_refresh", result.Data.RefreshToken);
        _sessionServiceMock.Verify(x => x.RenewSessionAsync(entry.SessionId, It.IsAny<DateTime>()), Times.Once);
        _sessionRevocationMock.Verify(
            x => x.RevokeSessionAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>()), Times.Never);
    }

    /// <summary>
    /// ★ 重放：一枚已被轮换掉、且超出宽限窗的令牌 → 撤销整条会话 + 发事件 + 专用错误码。
    /// 删掉 <c>OnRefreshTokenReuseDetectedAsync</c> 里的撤销或事件，这条即红。
    /// </summary>
    [Fact]
    public async Task Refresh_WithReplayedTokenOutsideOverlap_RevokesSessionAndRaisesEvent()
    {
        var sessionId = Guid.NewGuid();
        var rotated = new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            SessionId = sessionId,
            Value = "current",
            PreviousValueHash = OneTimeToken.Hash("stolen"),
            RotatedAt = DateTime.UtcNow.AddMinutes(-5),   // 远在 10 秒宽限窗之外
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        };

        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync(Provider, TokenName, "stolen"))
            .ReturnsAsync((AuthToken?)null);
        _authTokenServiceMock.Setup(x => x.FindTokenByPreviousValueAsync(Provider, TokenName, "stolen"))
            .ReturnsAsync(rotated);

        var result = await _authService.RefreshTokenAsync("stolen");

        Assert.False(result.Succeeded);
        Assert.Equal(401, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_REFRESH_TOKEN_REUSED, result.ErrorCode);
        _sessionRevocationMock.Verify(
            x => x.RevokeSessionAsync(sessionId, SessionRevocationReason.RefreshTokenReuse), Times.Once);
        _eventBusMock.Verify(
            x => x.PublishAsync(It.IsAny<RefreshTokenReuseDetectedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// ★ 宽限窗内的并发刷新（多标签页）：返回<b>当前这一代</b>，不再轮换、不撤销任何东西。
    /// 去掉宽限窗判断，这条会变成「用户开两个标签页就被踢掉」。
    /// </summary>
    [Fact]
    public async Task Refresh_WithPreviousTokenInsideOverlap_ReturnsCurrentPairWithoutRotating()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u" };
        var rotated = new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionId = sessionId,
            Value = "current",
            PreviousValueHash = OneTimeToken.Hash("previous"),
            RotatedAt = DateTime.UtcNow.AddSeconds(-2),   // 宽限窗内
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        };

        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync(Provider, TokenName, "previous"))
            .ReturnsAsync((AuthToken?)null);
        _authTokenServiceMock.Setup(x => x.FindTokenByPreviousValueAsync(Provider, TokenName, "previous"))
            .ReturnsAsync(rotated);
        // AuthToken.Value 存的是密文，宽限窗要经撤销出口还原出当代明文再交回给并发的那一方。
        _authTokenServiceMock.Setup(x => x.RevealTokenValue(rotated)).Returns("current");
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string>());
        _userManagerMock.Setup(x => x.SupportsUserLockout).Returns(true);
        _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(false);
        _sessionServiceMock.Setup(x => x.IsSessionValidAsync(sessionId)).ReturnsAsync(true);
        _tokenServiceMock.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>(), null, sessionId))
            .Returns("concurrent_access");

        var result = await _authService.RefreshTokenAsync("previous");

        Assert.True(result.Succeeded);
        Assert.Equal("concurrent_access", result.Data!.AccessToken);
        // 返回的是当前这一代，而不是又轮换出来的第三代。
        Assert.Equal("current", result.Data.RefreshToken);
        _authTokenServiceMock.Verify(
            x => x.RotateRefreshTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>()),
            Times.Never);
        _sessionRevocationMock.Verify(
            x => x.RevokeSessionAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>()), Times.Never);
    }

    /// <summary>对照组：哪一代都不是的令牌，回答与「过期」同形，且不撤销任何会话。</summary>
    [Fact]
    public async Task Refresh_WithUnknownToken_FailsWithoutRevoking()
    {
        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync(Provider, TokenName, "garbage"))
            .ReturnsAsync((AuthToken?)null);
        _authTokenServiceMock.Setup(x => x.FindTokenByPreviousValueAsync(Provider, TokenName, "garbage"))
            .ReturnsAsync((AuthToken?)null);

        var result = await _authService.RefreshTokenAsync("garbage");

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.NotEqual(ErrorCodes.IDENTITY_REFRESH_TOKEN_REUSED, result.ErrorCode);
        _sessionRevocationMock.Verify(
            x => x.RevokeSessionAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>()), Times.Never);
        _eventBusMock.Verify(
            x => x.PublishAsync(It.IsAny<RefreshTokenReuseDetectedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// ★★ 账号被停用（锁定到 100 年后）之后，手里的刷新令牌不再换得到新令牌，且会话被撤销。
    /// 删掉刷新路径上的 <c>RunLoginGuardsAsync</c>，这条即红 —— 那正是「停用账号后仍可无限续期」的形态。
    /// </summary>
    [Fact]
    public async Task Refresh_WhenAccountLockedOut_IsDeniedAndSessionRevoked()
    {
        var (user, entry) = ArrangeCurrentToken("current");
        _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(true);

        var result = await _authService.RefreshTokenAsync("current");

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_USER_LOCKED, result.ErrorCode);
        _authTokenServiceMock.Verify(
            x => x.RotateRefreshTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>()),
            Times.Never);
        _sessionRevocationMock.Verify(
            x => x.RevokeSessionAsync(entry.SessionId, SessionRevocationReason.GuardDenied), Times.Once);
    }

    /// <summary>
    /// 轮换的条件更新被别的请求抢先（CAS 失败）→ 走并发分支，而不是报错。
    /// 这是真并发下最常见的一条路径：两个标签页几乎同时拿着同一枚当前令牌来刷新。
    /// </summary>
    [Fact]
    public async Task Refresh_WhenRotationLosesTheRace_FallsBackToConcurrentBranch()
    {
        var (user, entry) = ArrangeCurrentToken("current");

        _authTokenServiceMock
            .Setup(x => x.RotateRefreshTokenAsync(entry.Id, "current", "next_refresh", It.IsAny<DateTime?>()))
            .ReturnsAsync(false);   // 抢占失败

        // 抢先的那个请求已经把当前值换成了 winner_refresh，旧值成了上一代。
        var winner = new AuthToken
        {
            Id = entry.Id,
            UserId = user.Id,
            SessionId = entry.SessionId,
            Value = "winner_refresh",
            PreviousValueHash = OneTimeToken.Hash("current"),
            RotatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        };
        _authTokenServiceMock.Setup(x => x.FindTokenByPreviousValueAsync(Provider, TokenName, "current"))
            .ReturnsAsync(winner);
        _authTokenServiceMock.Setup(x => x.RevealTokenValue(winner)).Returns("winner_refresh");

        var result = await _authService.RefreshTokenAsync("current");

        Assert.True(result.Succeeded);
        Assert.Equal("winner_refresh", result.Data!.RefreshToken);
        _sessionRevocationMock.Verify(
            x => x.RevokeSessionAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>()), Times.Never);
    }

    /// <summary>会话已被撤销时，刷新拒绝 —— 「踢下线」对刷新链路同样生效。</summary>
    [Fact]
    public async Task Refresh_WhenSessionRevoked_IsRejected()
    {
        var (_, entry) = ArrangeCurrentToken("current");
        _sessionServiceMock.Setup(x => x.IsSessionValidAsync(entry.SessionId)).ReturnsAsync(false);

        var result = await _authService.RefreshTokenAsync("current");

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_SESSION_REVOKED, result.ErrorCode);
    }
}
