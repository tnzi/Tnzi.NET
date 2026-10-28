using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Tnzi.Modules;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 委派会话的约束：声明名、长度、到期时间。
/// </summary>
/// <remarks>
/// 声明名那几条守的是「应用按原名读得回来、且冒充不了框架自己的声明」：撞上入站映射表的名字进来就被改写成长 URI，
/// 应用的请求闸门读不到它，于是把一条受限会话当成普通会话放行。
/// </remarks>
public class DelegatedSessionRulesTests
{
    private static DelegatedSessionRequest Request(
        IReadOnlyDictionary<string, string>? claims = null,
        string groupKey = "pass:abc:def",
        DateTime? expiresAt = null,
        string? label = null) => new()
    {
        GroupKey = groupKey,
        ExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(1),
        Label = label,
        Claims = claims ?? new Dictionary<string, string> { ["app_pass"] = "p1" },
    };

    [Theory]
    [InlineData("app_pass")]
    [InlineData("app_pass_device")]
    [InlineData("app.scope")]
    [InlineData("App-Kind2")]
    public void ApplicationClaimNames_AreAllowed(string name)
        => DelegatedSessionRules.IsAllowedClaimType(name).ShouldBeTrue();

    [Theory]
    [InlineData("role")]
    [InlineData("ROLE")]
    [InlineData("sub")]
    [InlineData("exp")]
    [InlineData("iss")]
    [InlineData("jti")]
    [InlineData("email")]
    [InlineData("nameid")]
    [InlineData("unique_name")]
    [InlineData("session_id")]
    [InlineData("Session_Id")]
    [InlineData("tenant_id")]
    [InlineData("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")]
    [InlineData("app:pass")]
    [InlineData("1pass")]
    [InlineData("_pass")]
    [InlineData("pass id")]
    [InlineData("")]
    public void ReservedOrUnreadableNames_AreRefused(string name)
        => DelegatedSessionRules.IsAllowedClaimType(name).ShouldBeFalse();

    /// <summary>
    /// ★ 入站映射表里的每一个短名都必须被拒：它们进来就被改成长 URI，应用按原名永远读不到。
    /// 按运行时的表逐个查，.NET 将来新增映射时这条会跟着收紧。
    /// </summary>
    [Fact]
    public void EveryNameThatInboundMappingRenames_IsRefused()
    {
        foreach (var mapped in JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Keys)
        {
            DelegatedSessionRules.IsAllowedClaimType(mapped).ShouldBeFalse(mapped);
        }
    }

    [Fact]
    public void LongNames_AreRefused()
        => DelegatedSessionRules.IsAllowedClaimType("a" + new string('b', DelegatedSessionRules.MaxClaimTypeLength)).ShouldBeFalse();

    [Fact]
    public void AValidRequest_Passes()
        => DelegatedSessionRules.Validate(Request(label: "Shared laptop"), DateTime.UtcNow).ShouldBeNull();

    [Fact]
    public void AnExpiryInThePast_IsRefused()
        => DelegatedSessionRules.Validate(Request(expiresAt: DateTime.UtcNow.AddSeconds(-1)), DateTime.UtcNow).ShouldNotBeNull();

    [Fact]
    public void ABlankGroupKey_IsRefused()
        => DelegatedSessionRules.Validate(Request(groupKey: " "), DateTime.UtcNow).ShouldNotBeNull();

    [Fact]
    public void AGroupKeyThatIsTooLong_IsRefused()
        => DelegatedSessionRules.Validate(Request(groupKey: new string('k', DelegatedSessionRules.MaxGroupKeyLength + 1)), DateTime.UtcNow)
            .ShouldNotBeNull();

    [Fact]
    public void ALabelThatIsTooLong_IsRefused()
        => DelegatedSessionRules.Validate(Request(label: new string('l', DelegatedSessionRules.MaxLabelLength + 1)), DateTime.UtcNow)
            .ShouldNotBeNull();

    [Fact]
    public void TooManyClaims_AreRefused()
    {
        var claims = Enumerable.Range(0, DelegatedSessionRules.MaxClaims + 1).ToDictionary(i => $"app_c{i}", i => "v");
        DelegatedSessionRules.Validate(Request(claims), DateTime.UtcNow).ShouldNotBeNull();
    }

    [Fact]
    public void AValueThatIsTooLong_IsRefused()
        => DelegatedSessionRules.Validate(
            Request(new Dictionary<string, string> { ["app_v"] = new string('v', DelegatedSessionRules.MaxClaimValueLength + 1) }),
            DateTime.UtcNow).ShouldNotBeNull();

    [Fact]
    public void AReservedClaimName_IsRefused()
        => DelegatedSessionRules.Validate(Request(new Dictionary<string, string> { ["role"] = "admin" }), DateTime.UtcNow).ShouldNotBeNull();

    [Fact]
    public void StoredClaims_RoundTrip()
    {
        var claims = new Dictionary<string, string> { ["app_pass"] = "p1", ["app_pass_device"] = "d1" };

        var back = DelegatedSessionRules.DeserializeClaims(DelegatedSessionRules.SerializeClaims(claims));

        back.ShouldBe(claims);
    }

    /// <summary>★ 存坏了的声明读不出来时必须抛：返回空集合等于签出一枚不受限制的令牌。</summary>
    [Fact]
    public void CorruptStoredClaims_Throw()
        => Should.Throw<InvalidOperationException>(() => DelegatedSessionRules.DeserializeClaims("[not an object"));
}

/// <summary>
/// 签发与刷新委派会话：声明进每一枚令牌、任何令牌活不过绝对到期时间、不挑战二次验证、不碰多登录策略。
/// </summary>
/// <remarks>
/// 令牌由真实的 <see cref="JwtTokenService"/> 签出再解码检查 —— 用替身的话，测试只能证明「AuthService 把声明递了过去」，
/// 证明不了令牌里真的有它、过期时间真的被夹住。
/// </remarks>
public class DelegatedSessionIssueTests
{
    private const string UserAgent = "PortableClient/1.0 (Windows NT 10.0)";

    private readonly IdentityOptions _options = new()
    {
        Jwt = new JwtOptions
        {
            SecretKey = "test-secret-key-at-least-32-chars-long-1234567890!!",
            Issuer = "test-issuer",
            Audience = "test-audience",
            EnableRefreshToken = true,
            AccessTokenExpirationMinutes = 60,
            RefreshTokenExpirationDays = 7,
            RefreshTokenRotationOverlapSeconds = 10,
        },
        MultiLogin = new MultiLoginOptions { AllowMultiLogin = false, OnConflict = LoginConflictPolicy.Replace },
    };

    private readonly SessionOptions _sessionOptions = new() { EnforceSessionValidation = true };
    private readonly Mock<UserManager<User>> _userManager;
    private readonly Mock<IAuthTokenService> _authTokens = new();
    private readonly Mock<ISessionService> _sessions = new();
    private readonly Mock<ISessionRevocationService> _revocation = new();
    private readonly Mock<ITwoFactorService> _twoFactor = new(MockBehavior.Strict);
    private readonly Mock<IEventBus> _eventBus = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Dictionary<Guid, DelegatedSessionInfo> _stored = [];
    private readonly List<DelegatedSessionCreation> _created = [];
    private readonly AuthService _authService;

    public DelegatedSessionIssueTests()
    {
        var store = new Mock<IUserStore<User>>();
        _userManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        _userManager.Setup(x => x.SupportsUserLockout).Returns(true);
        _userManager.Setup(x => x.IsLockedOutAsync(It.IsAny<User>())).ReturnsAsync(false);
        _userManager.Setup(x => x.GetRolesAsync(It.IsAny<User>())).ReturnsAsync(new List<string> { "member" });
        _userManager.Setup(x => x.GetTwoFactorEnabledAsync(It.IsAny<User>())).ReturnsAsync(true);

        var signInManager = new Mock<SignInManager<User>>(
            _userManager.Object,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<User>>().Object,
            new Mock<IOptions<Microsoft.AspNetCore.Identity.IdentityOptions>>().Object,
            new Mock<ILogger<SignInManager<User>>>().Object,
            new Mock<IAuthenticationSchemeProvider>().Object,
            new Mock<IUserConfirmation<User>>().Object);

        var scopedContext = new Mock<IScopedContext>();
        scopedContext.Setup(x => x.UserAgent).Returns(UserAgent);
        scopedContext.Setup(x => x.ClientIpAddress).Returns("198.51.100.20");

        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        serviceProvider.Setup(x => x.GetService(typeof(IScopedContext))).Returns(scopedContext.Object);
        serviceProvider.Setup(x => x.GetService(typeof(ICurrentUser))).Returns(_currentUser.Object);
        serviceProvider.Setup(x => x.GetService(typeof(IOptions<SessionOptions>)))
            .Returns(() => Microsoft.Extensions.Options.Options.Create(_sessionOptions));

        var monitor = new Mock<IOptionsMonitor<IdentityOptions>>();
        monitor.Setup(x => x.CurrentValue).Returns(_options);

        _sessions
            .Setup(x => x.CreateDelegatedSessionAsync(It.IsAny<DelegatedSessionCreation>()))
            .ReturnsAsync((DelegatedSessionCreation creation) =>
            {
                var id = Guid.NewGuid();
                _created.Add(creation);
                _stored[id] = creation.Delegation;
                return id;
            });
        _sessions
            .Setup(x => x.GetDelegationAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => _stored.GetValueOrDefault(id));
        _sessions.Setup(x => x.IsSessionValidAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        _sessions.Setup(x => x.RenewSessionAsync(It.IsAny<Guid>(), It.IsAny<DateTime>())).ReturnsAsync(Result.Success());
        _revocation
            .Setup(x => x.RevokeSessionAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>()))
            .ReturnsAsync(1);

        var tokenService = new JwtTokenService(
            Microsoft.Extensions.Options.Options.Create(_options), null, serviceProvider.Object, null);

        var coordinator = new LoginSessionCoordinator(
            serviceProvider.Object, monitor.Object, _sessions.Object, userAgentParser: null, sessionRevocation: _revocation.Object);

        var guards = new LoginGuardEvaluator(
            [new LockedAccountLoginGuard(_userManager.Object), new PendingActionsLoginGuard()],
            new Mock<ILogger<LoginGuardEvaluator>>().Object);

        _authService = new AuthService(
            _userManager.Object,
            signInManager.Object,
            tokenService,
            monitor.Object,
            serviceProvider.Object,
            _eventBus.Object,
            authTokenService: _authTokens.Object,
            sessionService: _sessions.Object,
            twoFactorService: _twoFactor.Object,
            loginSessionCoordinator: coordinator,
            loginGuardEvaluator: guards,
            sessionRevocation: _revocation.Object);
    }

    private static User NewUser()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "owner", TwoFactorEnabled = true };
        return user;
    }

    private static DelegatedSessionRequest Request(DateTime? expiresAt = null) => new()
    {
        GroupKey = "pass:p1:d1",
        Label = "Library computer",
        ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(3),
        Claims = new Dictionary<string, string> { ["app_pass"] = "p1", ["app_pass_device"] = "d1" },
    };

    private static JwtSecurityToken Decode(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    private static string? Claim(JwtSecurityToken token, string type) => token.Claims.FirstOrDefault(c => c.Type == type)?.Value;

    /// <summary>
    /// ★ 开着二次验证的账号照样直接拿到令牌，且一次挑战都不发：口令是本人在已过二次验证的会话里生成的，
    /// 换取它的客户端也没地方完成挑战。二次验证服务用的是严格替身，任何一次调用都会让这条红。
    /// </summary>
    [Fact]
    public async Task Issue_ForAnAccountWithTwoFactor_IssuesTokensWithoutAChallenge()
    {
        var user = NewUser();

        var result = await _authService.IssueDelegatedSessionAsync(user, Request());

        result.Succeeded.ShouldBeTrue(result.Message);
        var token = Decode(result.Data!.AccessToken);
        Claim(token, "app_pass").ShouldBe("p1");
        Claim(token, "app_pass_device").ShouldBe("d1");
        var sessionId = Guid.Parse(Claim(token, IdentityConstants.ClaimTypeNames.SessionId)!);
        _stored.ShouldContainKey(sessionId);
        result.Data.RefreshToken.ShouldNotBeNullOrEmpty();
        _authTokens.Verify(x => x.SaveTokenAsync(
            user.Id, IdentityConstants.TokenProvider.JWT, IdentityConstants.TokenName.RefreshToken,
            result.Data.RefreshToken, It.IsAny<DateTime?>(), sessionId), Times.Once);
    }

    [Fact]
    public async Task Issue_BindsTheSessionToTheRequestAndRecordsTheLogin()
    {
        var result = await _authService.IssueDelegatedSessionAsync(NewUser(), Request());

        result.Succeeded.ShouldBeTrue();
        var creation = _created.ShouldHaveSingleItem();
        creation.UserAgent.ShouldBe(UserAgent);
        creation.IpAddress.ShouldBe("198.51.100.20");
        creation.Delegation.Label.ShouldBe("Library computer");
        _eventBus.Verify(x => x.PublishAsync(
            It.Is<UserLoggedInEvent>(e => e.LoginProvider == IdentityConstants.LoginProvider.Delegated),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// ★★ 任何令牌都活不过绝对到期时间：口令 10 分钟后到期，而部署配置的 access token 是 60 分钟、刷新令牌 7 天。
    /// </summary>
    [Fact]
    public async Task Issue_CapsEveryLifetimeAtTheAbsoluteExpiry()
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(10);

        var result = await _authService.IssueDelegatedSessionAsync(NewUser(), Request(expiresAt));

        result.Succeeded.ShouldBeTrue();
        Decode(result.Data!.AccessToken).ValidTo.ShouldBeLessThanOrEqualTo(expiresAt);
        result.Data.ExpiresAt.ShouldBeLessThanOrEqualTo(expiresAt);
        result.Data.ExpiresIn.ShouldBeLessThanOrEqualTo(600);
        result.Data.RefreshTokenExpiresIn!.Value.ShouldBeLessThanOrEqualTo(600);
        _created.ShouldHaveSingleItem().ExpiresAt.ShouldBeLessThanOrEqualTo(expiresAt);
        _authTokens.Verify(x => x.SaveTokenAsync(
            It.IsAny<Guid>(), IdentityConstants.TokenProvider.JWT, IdentityConstants.TokenName.RefreshToken,
            It.IsAny<string>(), It.Is<DateTime?>(d => d <= expiresAt), It.IsAny<Guid>()), Times.Once);
    }

    /// <summary>★ 不占名额也不挤人：单设备策略下签发委派会话，本人的其它会话一条都不动，名额也不数。</summary>
    [Fact]
    public async Task Issue_DoesNotRunTheMultiLoginPolicy()
    {
        var result = await _authService.IssueDelegatedSessionAsync(NewUser(), Request());

        result.Succeeded.ShouldBeTrue();
        _sessions.Verify(x => x.GetUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never);
        _sessions.Verify(x => x.RevokeAllSessionsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()), Times.Never);
        _revocation.Verify(x => x.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()), Times.Never);
        _revocation.Verify(x => x.RevokeSessionAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>()), Times.Never);
        _sessions.Verify(x => x.CreateSessionAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>()), Times.Never);
    }

    [Fact]
    public async Task Issue_ForALockedAccount_IsRefusedBeforeASessionExists()
    {
        var user = NewUser();
        _userManager.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(true);

        var result = await _authService.IssueDelegatedSessionAsync(user, Request());

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.IDENTITY_USER_LOCKED);
        _created.ShouldBeEmpty();
        _authTokens.Verify(x => x.SaveTokenAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<Guid>()), Times.Never);
    }

    /// <summary>
    /// 欠着义务（例：管理员重置了密码、要求下次登录改密）时拒绝，且不发临时令牌 —— 便携客户端办不了这件事，
    /// 签出去的会话就等于替本人跳过了它。
    /// </summary>
    [Fact]
    public async Task Issue_WhenTheAccountOwesAnObligation_IsRefusedWithoutATempToken()
    {
        var user = NewUser();
        user.PendingActions = PendingUserActions.ChangePassword;

        var result = await _authService.IssueDelegatedSessionAsync(user, Request());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.ErrorCode.ShouldBe(ErrorCodes.IDENTITY_PENDING_ACTIONS_REQUIRED);
        JsonSerializer.Serialize(result.ErrorDetails).ShouldNotContain("TempToken");
        _created.ShouldBeEmpty();
    }

    [Fact]
    public async Task Issue_WithAReservedClaimName_IsRefusedBeforeAnything()
    {
        var request = Request() with { Claims = new Dictionary<string, string> { ["role"] = "admin" } };

        var result = await _authService.IssueDelegatedSessionAsync(NewUser(), request);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        _created.ShouldBeEmpty();
    }

    /// <summary>
    /// ★★ 会话服务建得出却读不回来（只实现了一半的自定义实现）：刷新出来的令牌会丢掉声明，
    /// 在应用那里就是一条不受限制的会话。在签发这一刻就拦下，并撤掉刚建的会话。
    /// </summary>
    [Fact]
    public async Task Issue_WhenTheSessionServiceCannotReadTheDelegationBack_WithdrawsTheSession()
    {
        _sessions.Setup(x => x.GetDelegationAsync(It.IsAny<Guid>())).ReturnsAsync((DelegatedSessionInfo?)null);

        var result = await _authService.IssueDelegatedSessionAsync(NewUser(), Request());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
        _revocation.Verify(x => x.RevokeSessionAsync(It.IsAny<Guid>(), SessionRevocationReason.DelegationRevoked), Times.Once);
        _authTokens.Verify(x => x.SaveTokenAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<Guid>()), Times.Never);
    }

    private (User User, AuthToken Entry) ArrangeRefresh(DateTime absoluteExpiresAt)
    {
        var user = NewUser();
        var sessionId = Guid.NewGuid();
        _stored[sessionId] = new DelegatedSessionInfo(
            "pass:p1:d1", "Library computer", new Dictionary<string, string> { ["app_pass"] = "p1" }, absoluteExpiresAt);
        var entry = new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SessionId = sessionId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        };
        _authTokens
            .Setup(x => x.FindTokenByValueAsync(IdentityConstants.TokenProvider.JWT, IdentityConstants.TokenName.RefreshToken, "current"))
            .ReturnsAsync(entry);
        _userManager.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        return (user, entry);
    }

    /// <summary>★★ 刷新之后签出的令牌带同一组声明，两个寿命仍夹在绝对到期时间之内。</summary>
    [Fact]
    public async Task Refresh_OfADelegatedSession_KeepsTheClaimsAndTheCap()
    {
        var absolute = DateTime.UtcNow.AddMinutes(20);
        var (_, entry) = ArrangeRefresh(absolute);
        DateTime? rotatedExpiry = null;
        _authTokens
            .Setup(x => x.RotateRefreshTokenAsync(entry.Id, "current", It.IsAny<string>(), It.IsAny<DateTime?>()))
            .Callback((Guid _, string _, string _, DateTime? expiresAt) => rotatedExpiry = expiresAt)
            .ReturnsAsync(true);

        var result = await _authService.RefreshTokenAsync("current");

        result.Succeeded.ShouldBeTrue(result.Message);
        var token = Decode(result.Data!.AccessToken);
        Claim(token, "app_pass").ShouldBe("p1");
        Claim(token, IdentityConstants.ClaimTypeNames.SessionId).ShouldBe(entry.SessionId.ToString());
        token.ValidTo.ShouldBeLessThanOrEqualTo(absolute);
        rotatedExpiry!.Value.ShouldBeLessThanOrEqualTo(absolute);
        result.Data.RefreshTokenExpiresIn!.Value.ShouldBeLessThanOrEqualTo(20 * 60);
        _sessions.Verify(x => x.RenewSessionAsync(entry.SessionId, It.Is<DateTime>(d => d <= absolute)), Times.Once);
    }

    /// <summary>
    /// ★ 过了绝对到期时间刷新一律失败，而且不轮换。会话校验关掉时这是最后一道。
    /// </summary>
    [Fact]
    public async Task Refresh_PastTheAbsoluteExpiry_FailsWithoutRotating()
    {
        _sessionOptions.EnforceSessionValidation = false;
        ArrangeRefresh(DateTime.UtcNow.AddMinutes(-1));

        var result = await _authService.RefreshTokenAsync("current");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(401);
        _authTokens.Verify(x => x.RotateRefreshTokenAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>()), Times.Never);
    }

    /// <summary>宽限窗里的并发刷新也要带回声明 —— 那是另一条签发 access token 的路径。</summary>
    [Fact]
    public async Task Refresh_InTheRotationOverlapWindow_KeepsTheClaims()
    {
        var user = NewUser();
        var sessionId = Guid.NewGuid();
        var absolute = DateTime.UtcNow.AddMinutes(20);
        _stored[sessionId] = new DelegatedSessionInfo(
            "pass:p1:d1", null, new Dictionary<string, string> { ["app_pass"] = "p1" }, absolute);
        var rotated = new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SessionId = sessionId,
            ExpiresAt = absolute,
            RotatedAt = DateTime.UtcNow,
        };
        _authTokens
            .Setup(x => x.FindTokenByPreviousValueAsync(IdentityConstants.TokenProvider.JWT, IdentityConstants.TokenName.RefreshToken, "previous"))
            .ReturnsAsync(rotated);
        _authTokens.Setup(x => x.RevealTokenValue(rotated)).Returns("current-generation");
        _userManager.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);

        var result = await _authService.RefreshTokenAsync("previous");

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.RefreshToken.ShouldBe("current-generation");
        var token = Decode(result.Data.AccessToken);
        Claim(token, "app_pass").ShouldBe("p1");
        token.ValidTo.ShouldBeLessThanOrEqualTo(absolute);
    }

    /// <summary>从委派会话登出：只结束这一条，本人别的会话一条不动。</summary>
    [Fact]
    public async Task Logout_FromADelegatedSession_EndsOnlyThatSession()
    {
        var user = NewUser();
        var sessionId = Guid.NewGuid();
        _userManager.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _currentUser.Setup(x => x.FindClaim(IdentityConstants.ClaimTypeNames.SessionId)).Returns(sessionId.ToString());

        var result = await _authService.LogoutAsync(user.Id);

        result.Succeeded.ShouldBeTrue();
        _revocation.Verify(x => x.RevokeSessionAsync(sessionId, SessionRevocationReason.Logout), Times.Once);
        _revocation.Verify(x => x.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()), Times.Never);
    }

    /// <summary>
    /// ★★ 认不出当前会话时什么都不撤销。此前回退成「撤销全部会话」：碰不到发起登出的那一方，
    /// 却把本人别的设备和应用签发的委派会话一起踢下线。
    /// </summary>
    [Fact]
    public async Task Logout_WhenTheCurrentSessionCannotBeTold_RevokesNothing()
    {
        var user = NewUser();
        _userManager.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);

        var result = await _authService.LogoutAsync(user.Id);

        result.Succeeded.ShouldBeTrue();
        _revocation.Verify(x => x.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()), Times.Never);
        _revocation.Verify(x => x.RevokeSessionAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>()), Times.Never);
        _sessions.Verify(x => x.RevokeAllSessionsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()), Times.Never);
    }
}

/// <summary>
/// 多登录策略与委派会话：两个方向都互不干扰。
/// </summary>
public class DelegatedSessionLoginPolicyTests
{
    private readonly Mock<ISessionService> _sessions = new();
    private readonly Mock<ISessionRevocationService> _revocation = new();
    private readonly Mock<IOptionsMonitor<IdentityOptions>> _options = new();
    private readonly Guid _newSessionId = Guid.NewGuid();
    private readonly LoginSessionCoordinator _coordinator;

    public DelegatedSessionLoginPolicyTests()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _sessions
            .Setup(x => x.CreateSessionAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(_newSessionId);

        _coordinator = new LoginSessionCoordinator(
            serviceProvider.Object, _options.Object, _sessions.Object, userAgentParser: null, sessionRevocation: _revocation.Object);
    }

    private void SetPolicy(MultiLoginOptions multi)
        => _options.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Jwt = new JwtOptions { EnableRefreshToken = true, RefreshTokenExpirationDays = 7 },
            MultiLogin = multi,
        });

    private void SetExistingSessions(params UserSessionDto[] sessions)
        => _sessions
            .Setup(x => x.GetUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<bool>()))
            .ReturnsAsync(Result<IEnumerable<UserSessionDto>>.Success(sessions));

    private static UserSessionDto Session(DateTime lastActivity, string? groupKey = null) => new()
    {
        Id = Guid.NewGuid(),
        LastActivityTime = lastActivity,
        ExpiresAt = DateTime.UtcNow.AddDays(1),
        GroupKey = groupKey,
    };

    /// <summary>单设备 + Reject：只有一条委派会话在线时，本人照样登得进来。</summary>
    [Fact]
    public async Task ADelegatedSession_DoesNotOccupyASlot()
    {
        SetPolicy(new MultiLoginOptions { AllowMultiLogin = false, OnConflict = LoginConflictPolicy.Reject });
        SetExistingSessions(Session(DateTime.UtcNow, groupKey: "pass:p1:d1"));

        var result = await _coordinator.EstablishAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(_newSessionId);
    }

    /// <summary>单设备 + Replace：本人在别处登录，挤掉的是他别的登录会话，委派会话留着。</summary>
    [Fact]
    public async Task SingleDeviceReplace_DoesNotEvictADelegatedSession()
    {
        SetPolicy(new MultiLoginOptions { AllowMultiLogin = false, OnConflict = LoginConflictPolicy.Replace });
        var normal = Session(DateTime.UtcNow.AddMinutes(-5));
        var delegated = Session(DateTime.UtcNow.AddMinutes(-1), groupKey: "pass:p1:d1");
        SetExistingSessions(normal, delegated);

        await _coordinator.EstablishAsync(Guid.NewGuid());

        _revocation.Verify(x => x.RevokeSessionAsync(normal.Id, SessionRevocationReason.MultiLoginReplaced), Times.Once);
        _revocation.Verify(x => x.RevokeSessionAsync(delegated.Id, It.IsAny<SessionRevocationReason>()), Times.Never);
        _revocation.Verify(x => x.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()), Times.Never);
    }

    /// <summary>限并发 + Replace：最旧的那条若是委派会话，挤掉的是下一条登录会话，而且只挤一条。</summary>
    [Fact]
    public async Task ConcurrencyLimit_NeitherCountsNorEvictsADelegatedSession()
    {
        SetPolicy(new MultiLoginOptions { AllowMultiLogin = true, MaxConcurrentSessions = 2, OnConflict = LoginConflictPolicy.Replace });
        var delegated = Session(DateTime.UtcNow.AddHours(-3), groupKey: "pass:p1:d1");
        var older = Session(DateTime.UtcNow.AddHours(-2));
        var newer = Session(DateTime.UtcNow.AddHours(-1));
        SetExistingSessions(delegated, older, newer);

        await _coordinator.EstablishAsync(Guid.NewGuid());

        _revocation.Verify(x => x.RevokeSessionAsync(older.Id, SessionRevocationReason.MultiLoginReplaced), Times.Once);
        _revocation.Verify(x => x.RevokeSessionAsync(newer.Id, It.IsAny<SessionRevocationReason>()), Times.Never);
        _revocation.Verify(x => x.RevokeSessionAsync(delegated.Id, It.IsAny<SessionRevocationReason>()), Times.Never);
    }

    [Fact]
    public async Task EstablishDelegated_ClampsTheSlidingExpiryToTheAbsoluteExpiry()
    {
        SetPolicy(new MultiLoginOptions { AllowMultiLogin = false, OnConflict = LoginConflictPolicy.Reject });
        DelegatedSessionCreation? created = null;
        var sessionId = Guid.NewGuid();
        _sessions
            .Setup(x => x.CreateDelegatedSessionAsync(It.IsAny<DelegatedSessionCreation>()))
            .Callback((DelegatedSessionCreation c) => created = c)
            .ReturnsAsync(sessionId);
        var expiresAt = DateTime.UtcNow.AddHours(2);

        var result = await _coordinator.EstablishDelegatedAsync(Guid.NewGuid(), new DelegatedSessionRequest
        {
            GroupKey = "pass:p1:d1",
            ExpiresAt = expiresAt,
            Claims = new Dictionary<string, string> { ["app_pass"] = "p1" },
        });

        result.Data.ShouldBe(sessionId);
        created!.ExpiresAt.ShouldBe(expiresAt);
        created.Delegation.AbsoluteExpiresAt.ShouldBe(expiresAt);
        _sessions.Verify(x => x.GetUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>不支持委派会话的会话服务（默认接口实现答 <see cref="Guid.Empty"/>）：拒绝，而不是退化成普通会话。</summary>
    [Fact]
    public async Task EstablishDelegated_WhenTheSessionServiceCannotStoreIt_Fails()
    {
        SetPolicy(new MultiLoginOptions());
        _sessions.Setup(x => x.CreateDelegatedSessionAsync(It.IsAny<DelegatedSessionCreation>())).ReturnsAsync(Guid.Empty);

        var result = await _coordinator.EstablishDelegatedAsync(Guid.NewGuid(), new DelegatedSessionRequest
        {
            GroupKey = "pass:p1:d1",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
        _sessions.Verify(x => x.CreateSessionAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>()), Times.Never);
    }
}

/// <summary>
/// 按分组撤销委派会话：一次撤掉同组的全部会话与刷新令牌，别的会话一条不碰。
/// </summary>
public class DelegatedSessionGroupRevocationTests
{
    private readonly Mock<ISessionService> _sessions = new();
    private readonly Mock<IAuthTokenService> _authTokens = new();
    private readonly Mock<IEventBus> _eventBus = new();
    private readonly Mock<IUserTenantScopeProvider> _scope = new();
    private readonly SessionRevocationService _service;

    public DelegatedSessionGroupRevocationTests()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _sessions.Setup(x => x.RevokeSessionAsync(It.IsAny<Guid>())).ReturnsAsync(Result.Success());
        _scope.Setup(x => x.Current).Returns(UserTenantScope.Unrestricted);
        _scope.Setup(x => x.ContainsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _service = new SessionRevocationService(
            serviceProvider.Object, _sessions.Object, _authTokens.Object, _scope.Object, _eventBus.Object);
    }

    private void SetSessions(Guid userId, params UserSessionDto[] sessions)
        => _sessions
            .Setup(x => x.GetUserSessionsAsync(userId, true))
            .ReturnsAsync(Result<IEnumerable<UserSessionDto>>.Success(sessions));

    [Fact]
    public async Task RevokeGroup_RevokesTheGroupAndItsTokensOnly()
    {
        var userId = Guid.NewGuid();
        var first = new UserSessionDto { Id = Guid.NewGuid(), UserId = userId, GroupKey = "pass:p1:d1" };
        var second = new UserSessionDto { Id = Guid.NewGuid(), UserId = userId, GroupKey = "pass:p1:d1" };
        var otherDevice = new UserSessionDto { Id = Guid.NewGuid(), UserId = userId, GroupKey = "pass:p1:d2" };
        var signIn = new UserSessionDto { Id = Guid.NewGuid(), UserId = userId };
        SetSessions(userId, first, second, otherDevice, signIn);
        IReadOnlyCollection<Guid>? tokenTargets = null;
        _authTokens
            .Setup(x => x.RemoveSessionTokensAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .Callback((IReadOnlyCollection<Guid> ids) => tokenTargets = ids)
            .ReturnsAsync(2);

        var revoked = await _service.RevokeSessionGroupAsync(userId, "pass:p1:d1", SessionRevocationReason.DelegationRevoked);

        revoked.ShouldBe(2);
        _sessions.Verify(x => x.RevokeSessionAsync(first.Id), Times.Once);
        _sessions.Verify(x => x.RevokeSessionAsync(second.Id), Times.Once);
        _sessions.Verify(x => x.RevokeSessionAsync(otherDevice.Id), Times.Never);
        _sessions.Verify(x => x.RevokeSessionAsync(signIn.Id), Times.Never);
        tokenTargets!.ShouldBe([first.Id, second.Id], ignoreOrder: true);
        _eventBus.Verify(x => x.PublishAsync(
            It.Is<SessionsRevokedEvent>(e => e.UserId == userId && e.GroupKey == "pass:p1:d1"
                && e.Reason == SessionRevocationReason.DelegationRevoked && e.SessionCount == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>已经撤销的组员不再撤一遍，但它残留的刷新令牌照样清掉（与单条撤销同一口径）。</summary>
    [Fact]
    public async Task RevokeGroup_StillRemovesTheTokensOfAnAlreadyRevokedMember()
    {
        var userId = Guid.NewGuid();
        var revokedMember = new UserSessionDto { Id = Guid.NewGuid(), UserId = userId, GroupKey = "pass:p1:d1", IsRevoked = true };
        SetSessions(userId, revokedMember);
        _authTokens.Setup(x => x.RemoveSessionTokensAsync(It.IsAny<IReadOnlyCollection<Guid>>())).ReturnsAsync(1);

        var revoked = await _service.RevokeSessionGroupAsync(userId, "pass:p1:d1", SessionRevocationReason.DelegationRevoked);

        revoked.ShouldBe(0);
        _sessions.Verify(x => x.RevokeSessionAsync(It.IsAny<Guid>()), Times.Never);
        _authTokens.Verify(x => x.RemoveSessionTokensAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(revokedMember.Id))), Times.Once);
    }

    [Fact]
    public async Task RevokeGroup_OfAUserOutsideTheCallersScope_TouchesNothing()
    {
        var userId = Guid.NewGuid();
        _scope.Setup(x => x.ContainsAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var revoked = await _service.RevokeSessionGroupAsync(userId, "pass:p1:d1", SessionRevocationReason.DelegationRevoked);

        revoked.ShouldBe(0);
        _sessions.Verify(x => x.GetUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never);
        _authTokens.Verify(x => x.RemoveSessionTokensAsync(It.IsAny<IReadOnlyCollection<Guid>>()), Times.Never);
    }
}

/// <summary>
/// 声明穿过<b>真实的</b> JWT Bearer 配置（<see cref="IdentityModule"/> 注册的那一份，开着 <c>MapInboundClaims</c>）
/// 之后名字原样不变：普通请求读 <c>HttpContext.User</c>，SignalR 的 <c>Context.User</c> 来自同一个处理器、令牌从查询串读。
/// </summary>
public class DelegatedSessionBearerTests
{
    private static async Task<ServiceProvider> IdentityServicesAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns("Development");
        services.AddSingleton(environment.Object);

        var context = new ServiceConfigurationContext(services, new ConfigurationBuilder().Build());
        var module = new IdentityModule();
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        // 每请求会话校验在这里不是被测对象：给一个答「有效」的会话服务，免得去解析没有数据库的仓储。
        var sessions = new Mock<ISessionService>();
        sessions.Setup(x => x.ValidateAsync(It.IsAny<Guid>(), It.IsAny<SessionValidationContext>()))
            .ReturnsAsync(SessionValidationResult.Valid);
        services.AddSingleton(sessions.Object);

        return services.BuildServiceProvider();
    }

    private static string IssueToken(IServiceProvider services, DateTime notAfter)
    {
        using var scope = services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = new User { Id = Guid.NewGuid(), UserName = "owner" };
        return tokenService.GenerateToken(
            user,
            ["member"],
            [new Claim("app_pass", "p1"), new Claim("app_pass_device", "d1")],
            Guid.NewGuid(),
            notAfter);
    }

    private static async Task<ClaimsPrincipal?> AuthenticateAsync(IServiceProvider services, Action<HttpRequest> carryToken, string path)
    {
        using var scope = services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Path = path;
        carryToken(http.Request);

        var result = await http.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        return result.Succeeded ? result.Principal : null;
    }

    [Fact]
    public async Task OnAnApiRequest_TheClaimsKeepTheirNames()
    {
        await using var services = await IdentityServicesAsync();
        var token = IssueToken(services, DateTime.UtcNow.AddMinutes(30));

        var principal = await AuthenticateAsync(
            services, request => request.Headers.Authorization = $"Bearer {token}", "/api/ai/threads");

        principal.ShouldNotBeNull();
        principal.FindFirst("app_pass")?.Value.ShouldBe("p1");
        principal.FindFirst("app_pass_device")?.Value.ShouldBe("d1");
    }

    [Fact]
    public async Task OnAHubConnection_WithTheTokenInTheQueryString_TheClaimsKeepTheirNames()
    {
        await using var services = await IdentityServicesAsync();
        var token = IssueToken(services, DateTime.UtcNow.AddMinutes(30));

        var principal = await AuthenticateAsync(
            services, request => request.QueryString = new QueryString($"?access_token={token}"), "/hubs/device");

        principal.ShouldNotBeNull();
        principal.FindFirst("app_pass")?.Value.ShouldBe("p1");
    }

    /// <summary>★ <c>notAfter</c> 真的进了令牌：解码出来的过期时间不晚于它（JWT 按秒计）。</summary>
    [Fact]
    public async Task TheTokenDoesNotOutliveNotAfter()
    {
        await using var services = await IdentityServicesAsync();
        var notAfter = DateTime.UtcNow.AddMinutes(5);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(IssueToken(services, notAfter));

        token.ValidTo.ShouldBeLessThanOrEqualTo(notAfter);
        token.ValidTo.ShouldBeGreaterThan(notAfter.AddSeconds(-2));
    }
}

/// <summary>
/// Redis 会话后端同样要把委派事实存下来、读得回来、在列表上标出来 —— 两个后端给出不同的强度是不可接受的。
/// </summary>
public class DistributedDelegatedSessionTests
{
    private const string UserAgent = "PortableClient/1.0 (Windows NT 10.0)";

    private static DistributedSessionService CreateService(Microsoft.Extensions.Caching.Distributed.IDistributedCache cache, int absoluteLifetimeHours = 720)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        return new DistributedSessionService(
            cache,
            Microsoft.Extensions.Options.Options.Create(new SessionOptions { RedisKeyPrefix = "Test:Session", AbsoluteLifetimeHours = absoluteLifetimeHours }),
            serviceProvider.Object,
            UnscopedUserTenantScope.Create());
    }

    private static Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache NewCache()
        => new(Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions()));

    private static DelegatedSessionCreation Creation(Guid userId, DateTime absoluteExpiresAt) => new(
        userId,
        "PortableClient",
        "198.51.100.20",
        UserAgent,
        absoluteExpiresAt,
        new DelegatedSessionInfo(
            "pass:p1:d1", "Library computer", new Dictionary<string, string> { ["App_Pass"] = "p1" }, absoluteExpiresAt));

    /// <summary>★ 声明名的大小写原样往返：会话记录按 camelCase 序列化，而字典的键不能跟着被改写。</summary>
    [Fact]
    public async Task ADelegatedSession_RoundTripsItsDelegation()
    {
        var cache = NewCache();
        var userId = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.AddHours(2);

        var sessionId = await CreateService(cache).CreateDelegatedSessionAsync(Creation(userId, expiresAt));
        var delegation = await CreateService(cache).GetDelegationAsync(sessionId);

        delegation.ShouldNotBeNull();
        delegation.GroupKey.ShouldBe("pass:p1:d1");
        delegation.Claims.ShouldBe(new Dictionary<string, string> { ["App_Pass"] = "p1" });
        delegation.AbsoluteExpiresAt.ShouldBe(expiresAt, TimeSpan.FromSeconds(1));

        var listed = (await CreateService(cache).GetUserSessionsAsync(userId)).Data!.ShouldHaveSingleItem();
        listed.GroupKey.ShouldBe("pass:p1:d1");
        listed.Label.ShouldBe("Library computer");
    }

    [Fact]
    public async Task TheConfiguredAbsoluteLifetime_WinsWhenItIsEarlier()
    {
        var cache = NewCache();

        var sessionId = await CreateService(cache, absoluteLifetimeHours: 24)
            .CreateDelegatedSessionAsync(Creation(Guid.NewGuid(), DateTime.UtcNow.AddDays(3)));
        var delegation = await CreateService(cache).GetDelegationAsync(sessionId);

        delegation!.AbsoluteExpiresAt.ShouldBe(DateTime.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AnOrdinarySession_HasNoDelegation()
    {
        var cache = NewCache();
        var sessionId = await CreateService(cache).CreateSessionAsync(Guid.NewGuid(), "Chrome", "203.0.113.10", UserAgent, DateTime.UtcNow.AddDays(7));

        (await CreateService(cache).GetDelegationAsync(sessionId)).ShouldBeNull();
    }
}
