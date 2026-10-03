
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

public class AuthServiceTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<SignInManager<User>> _signInManagerMock;
    private readonly Mock<ITokenService> _tokenServiceMock;
    private readonly Mock<IOptionsMonitor<IdentityOptions>> _identityOptionsMock;
    private readonly Mock<IScopedContext> _scopedContextMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<ICaptchaService> _captchaServiceMock;
    private readonly Mock<ICaptchaVerifier> _captchaVerifierMock;
    private readonly Mock<IAuthTokenService> _authTokenServiceMock;

    // Optional services
    private readonly Mock<IPasswordPolicyService> _passwordPolicyServiceMock;
    private readonly Mock<ISessionService> _sessionServiceMock;
    private readonly Mock<ILoginSecurityService> _loginSecurityServiceMock;
    private readonly Mock<ITwoFactorService> _twoFactorServiceMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    // The 2FA temp token is persisted through a *fresh scope* (independent of the
    // request's rolled-back UnitOfWork) - this mock backs that scope so tests can
    // assert the token is saved there, not on the ambient _authTokenServiceMock.
    private readonly Mock<IAuthTokenService> _scopedAuthTokenServiceMock;
    private readonly Mock<ISessionRevocationService> _sessionRevocationMock;

    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<User>>();
        var options = new Mock<IOptions<Microsoft.AspNetCore.Identity.IdentityOptions>>();
        var logger = new Mock<ILogger<SignInManager<User>>>();
        var schemes = new Mock<IAuthenticationSchemeProvider>();
        var confirmation = new Mock<IUserConfirmation<User>>();

        _signInManagerMock = new Mock<SignInManager<User>>(
            _userManagerMock.Object,
            contextAccessor.Object,
            claimsFactory.Object,
            options.Object,
            logger.Object,
            schemes.Object,
            confirmation.Object);

        _tokenServiceMock = new Mock<ITokenService>();
        _identityOptionsMock = new Mock<IOptionsMonitor<IdentityOptions>>();
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions());
        _scopedContextMock = new Mock<IScopedContext>();
        _scopedContextMock.Setup(x => x.ClientIpAddress).Returns("127.0.0.1");
        _scopedContextMock.Setup(x => x.UserAgent).Returns("Test Browser");
        _eventBusMock = new Mock<IEventBus>();
        _captchaServiceMock = new Mock<ICaptchaService>();
        _captchaVerifierMock = new Mock<ICaptchaVerifier>();
        _captchaVerifierMock.SetupGet(x => x.ProviderName).Returns("image");
        _captchaVerifierMock.SetupGet(x => x.IsEnabled).Returns(true);
        _captchaVerifierMock.Setup(x => x.GetClientConfig()).Returns(new CaptchaClientConfigDto { Enabled = true, Provider = "image" });
        // 默认：任何令牌都按「没交 / 不对」拒绝；具体用例再为某个令牌设 Pass（Moq 后设的 Setup 优先）。
        _captchaVerifierMock.Setup(x => x.VerifyAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? token, string _, CancellationToken _) => CaptchaVerification.Fail("image", string.IsNullOrEmpty(token) ? CaptchaFailure.MissingToken : CaptchaFailure.Rejected));
        _captchaServiceMock.Setup(x => x.RecordLoginFailureAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _captchaServiceMock.Setup(x => x.ClearLoginFailureAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _authTokenServiceMock = new Mock<IAuthTokenService>();

        _passwordPolicyServiceMock = new Mock<IPasswordPolicyService>();
        _sessionServiceMock = new Mock<ISessionService>();
        _sessionServiceMock.Setup(x => x.GetUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<bool>()))
            .ReturnsAsync(Result<IEnumerable<UserSessionDto>>.Success(Enumerable.Empty<UserSessionDto>()));
        _loginSecurityServiceMock = new Mock<ILoginSecurityService>();
        _twoFactorServiceMock = new Mock<ITwoFactorService>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _serviceProviderMock.Setup(x => x.GetService(typeof(IScopedContext)))
            .Returns(_scopedContextMock.Object);

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        // Wire a scope factory so AuthService.PersistTwoFactorTempTokenAsync's
        // `ServiceProvider.CreateScope()` resolves a scoped IAuthTokenService - the
        // 2FA temp token is saved in a fresh scope to survive the request's
        // UnitOfWork rollback (the challenge returns a 403 failure envelope).
        _scopedAuthTokenServiceMock = new Mock<IAuthTokenService>();
        var scopedProvider = new Mock<IServiceProvider>();
        scopedProvider.Setup(x => x.GetService(typeof(IAuthTokenService)))
            .Returns(_scopedAuthTokenServiceMock.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(x => x.ServiceProvider).Returns(scopedProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(x => x.CreateScope()).Returns(scope.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(scopeFactory.Object);

        _sessionRevocationMock = new Mock<ISessionRevocationService>();
        _sessionRevocationMock
            .Setup(x => x.RevokeSessionAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>()))
            .ReturnsAsync(1);
        _sessionRevocationMock
            .Setup(x => x.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()))
            .ReturnsAsync(1);

        // ★ 守卫链装的是**真实**的求值器 + 框架内置的 LockedAccountLoginGuard，不是 mock。
        // 账号锁定/停用的判定就住在这条链上，用 mock 顶替等于把被测对象换掉：
        // 那样测试只能证明「AuthService 会问求值器」，证明不了「被停用的账号进不来」。
        var loginGuardEvaluator = new LoginGuardEvaluator(
            [new LockedAccountLoginGuard(_userManagerMock.Object), new PendingActionsLoginGuard()],
            new Mock<ILogger<LoginGuardEvaluator>>().Object);

        _authService = new AuthService(
            _userManagerMock.Object,
            _signInManagerMock.Object,
            _tokenServiceMock.Object,
            _identityOptionsMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            _captchaServiceMock.Object,
            _authTokenServiceMock.Object,
            _passwordPolicyServiceMock.Object,
            _sessionServiceMock.Object,
            _loginSecurityServiceMock.Object,
            _twoFactorServiceMock.Object,
            loginGuardEvaluator: loginGuardEvaluator,
            sessionRevocation: _sessionRevocationMock.Object,
            captchaVerifier: _captchaVerifierMock.Object
        );
    }

    [Fact]
    public void GetAuthConfig_MapsOptionsToDto_AndFiltersEnabledOAuthProviders()
    {
        // Arrange - specific switches + two OAuth providers with full creds, one empty.
        var options = new IdentityOptions
        {
            SignIn = { AllowUserNameLogin = true, AllowEmailLogin = true, AllowSmsLogin = false, UseEmailAsUserName = true },
            Otp = { EnableSms = false, EnableEmail = true },
            Registration = { EnableQuickRegisterEmail = true, EnableQuickRegisterSms = false },
            Recovery = { EnablePasswordResetByEmail = true, EnablePasswordResetBySms = false },
            Captcha = { EnableCaptchaOnLogin = true, EnableCaptchaOnRegister = false },
            Passkey = { Enabled = true },
            OAuth =
            {
                GitHub = { ClientId = "gh-id", ClientSecret = "gh-secret" },
                Google = { ClientId = "g-id", ClientSecret = "g-secret" },
                // Microsoft/Facebook/Twitter left empty → must be excluded.
            },
        };
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(options);

        // Act
        var result = _authService.GetAuthConfig();

        // Assert - each flag maps from the right option.
        Assert.True(result.Succeeded);
        var dto = result.Data!;
        Assert.True(dto.AllowUserNameLogin);
        Assert.True(dto.AllowEmailLogin);
        Assert.False(dto.AllowSmsLogin);
        Assert.True(dto.UseEmailAsUserName);
        Assert.True(dto.EnableCodeLogin);            // email OR sms
        Assert.True(dto.CodeLoginViaEmail);
        Assert.False(dto.CodeLoginViaSms);
        Assert.True(dto.EnableRegistration);         // quick email OR sms
        Assert.True(dto.RegisterViaEmail);
        Assert.False(dto.RegisterViaSms);
        Assert.True(dto.EnablePasswordRecovery);
        Assert.True(dto.RecoveryViaEmail);
        Assert.False(dto.RecoveryViaSms);
        Assert.True(dto.EnableCaptchaOnLogin);
        Assert.False(dto.EnableCaptchaOnRegister);
        Assert.True(dto.EnablePasskey);

        // Only providers with BOTH ClientId + ClientSecret are listed - no secrets leak.
        Assert.Equal(2, dto.OAuthProviders.Count);
        Assert.Contains(dto.OAuthProviders, p => p.Provider == "github" && p.DisplayName == "GitHub");
        Assert.Contains(dto.OAuthProviders, p => p.Provider == "google" && p.DisplayName == "Google");
        Assert.DoesNotContain(dto.OAuthProviders, p => p.Provider == "microsoft");
    }

    [Fact]
    public void GetAuthConfig_AllChannelsOff_DisablesCodeLoginRecoveryAndProviders()
    {
        var options = new IdentityOptions
        {
            Otp = { EnableSms = false, EnableEmail = false },
            Registration = { EnableQuickRegisterEmail = false, EnableQuickRegisterSms = false },
            Recovery = { EnablePasswordResetByEmail = false, EnablePasswordResetBySms = false },
        };
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(options);

        var dto = _authService.GetAuthConfig().Data!;
        Assert.False(dto.EnableCodeLogin);
        Assert.False(dto.EnableRegistration);
        // Passkey defaults to off. The client gates its enrolment UI on this flag,
        // so reporting it as available on a deployment that has it disabled would
        // offer a flow that can only 400.
        Assert.False(dto.EnablePasskey);
        Assert.False(dto.EnablePasswordRecovery);
        Assert.Empty(dto.OAuthProviders);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsSuccess()
    {
        // Arrange
        var username = "testuser";
        var password = "Password123!";
        var user = new User { Id = Guid.NewGuid(), UserName = username, Email = "test@example.com", EmailConfirmed = true };

        _captchaServiceMock.Setup(x => x.IsCaptchaRequiredAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        _userManagerMock.Setup(x => x.FindByNameAsync(username))
            .ReturnsAsync(user);

        _signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, password, It.IsAny<bool>()))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        _loginSecurityServiceMock.Setup(x => x.DetectAbnormalLoginAsync(user.Id, It.IsAny<string>(), It.IsAny<string>()))
             .ReturnsAsync(AbnormalLoginResult.Normal());

        _passwordPolicyServiceMock.Setup(x => x.CheckPasswordExpirationAsync(user.Id))
             .ReturnsAsync(new PasswordExpirationResult { IsExpired = false });

        _userManagerMock.Setup(x => x.GetTwoFactorEnabledAsync(user))
            .ReturnsAsync(false);

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        _tokenServiceMock.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>()))
            .Returns("access_token");

        // Act
        var result = await _authService.LoginAsync(new LoginDto { UserName = username, Password = password });

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("access_token", result.Data);
    }

    /// <summary>
    /// 「邮箱未确认」只在密码校验通过之后才说得出口。对一个被登录守卫（IP 允许列表）挡着的账号，
    /// 这句 403 就是在证明密码是对的；守卫不放行时必须答守卫的（与密码错误同形的）回答。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoginAsync_WithAnUnconfirmedEmail_AnswersTheGuardFirst(bool guardDenies)
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "unconfirmed", Email = "u@example.com", EmailConfirmed = false };
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Registration = new RegistrationOptions { RequireConfirmedEmail = true }
        });
        _userManagerMock.Setup(x => x.FindByNameAsync("unconfirmed")).ReturnsAsync(user);
        _signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, "right", It.IsAny<bool>()))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        var guard = new Mock<ILoginGuard>();
        guard.Setup(g => g.EvaluateAsync(It.IsAny<LoginGuardContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(guardDenies ? LoginGuardResult.DenyAsInvalidCredentials("IP allow-list") : LoginGuardResult.Allow());
        var service = new AuthService(
            _userManagerMock.Object, _signInManagerMock.Object, _tokenServiceMock.Object, _identityOptionsMock.Object,
            _serviceProviderMock.Object, _eventBusMock.Object, _captchaServiceMock.Object, _authTokenServiceMock.Object,
            _passwordPolicyServiceMock.Object, _sessionServiceMock.Object, _loginSecurityServiceMock.Object, _twoFactorServiceMock.Object,
            loginGuardEvaluator: new LoginGuardEvaluator([guard.Object], new Mock<ILogger<LoginGuardEvaluator>>().Object),
            sessionRevocation: _sessionRevocationMock.Object,
            captchaVerifier: _captchaVerifierMock.Object);

        var result = await service.LoginAsync(new LoginDto { UserName = "unconfirmed", Password = "right" });

        Assert.False(result.Succeeded);
        if (guardDenies)
        {
            Assert.Equal(InvalidCredentialsResponse.StatusCode, result.Code);
            Assert.Equal(InvalidCredentialsResponse.ErrorCode, result.ErrorCode);
            Assert.Equal(InvalidCredentialsResponse.Message, result.Message);
        }
        else
        {
            Assert.Equal(ErrorCodes.IDENTITY_EMAIL_NOT_CONFIRMED, result.ErrorCode);
        }
    }

    [Fact]
    public async Task LoginAsync_WithInvalidCredentials_ReturnsFailure()
    {
        // Arrange
        var username = "testuser";
        var password = "WrongPassword";
        var user = new User { Id = Guid.NewGuid(), UserName = username };

        _captchaServiceMock.Setup(x => x.IsCaptchaRequiredAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        _userManagerMock.Setup(x => x.FindByNameAsync(username))
            .ReturnsAsync(user);

        _signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, password, It.IsAny<bool>()))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserLoginFailedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _authService.LoginAsync(new LoginDto { UserName = username, Password = password });

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("Invalid", result.Message);
    }

    [Fact]
    public async Task LoginAsync_WithUserNotFound_ReturnsFailure()
    {
        // Arrange
        var username = "nonexistent";
        var password = "Password123!";

        _captchaServiceMock.Setup(x => x.IsCaptchaRequiredAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        _userManagerMock.Setup(x => x.FindByNameAsync(username))
            .ReturnsAsync((User?)null);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserLoginFailedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _authService.LoginAsync(new LoginDto { UserName = username, Password = password });

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("Invalid", result.Message);
    }

    [Fact]
    public async Task LoginAsync_WhenCaptchaRequiredAndMissing_ReturnsCaptchaRequiredWithFreshImage()
    {
        // Arrange - captcha enabled AND the adaptive gate says a captcha is now
        // required (failure threshold reached), but the client sent none.
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Captcha = { EnableCaptchaOnLogin = true },
        });
        _captchaServiceMock.Setup(x => x.IsCaptchaRequiredAsync(It.IsAny<string>())).ReturnsAsync(true);
        _captchaServiceMock.Setup(x => x.IsCacheAvailable).Returns(true);
        _captchaServiceMock.Setup(x => x.GenerateAsync("login")).ReturnsAsync(new CaptchaResult
        {
            CaptchaId = "fresh-cid",
            ImageBytes = [1, 2, 3],
            ExpirationSeconds = 300,
        });

        // Act
        var result = await _authService.LoginAsync(new LoginDto { UserName = "u", Password = "p" });

        // Assert - dedicated error code + a fresh captcha the UI can render inline.
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, result.ErrorCode);
        var captcha = Assert.IsType<CaptchaDto>(result.ErrorDetails);
        Assert.Equal("fresh-cid", captcha.CaptchaId);
        Assert.False(string.IsNullOrEmpty(captcha.ImageBase64));
        // The password is never checked when the captcha gate fails first.
        _userManagerMock.Verify(x => x.FindByNameAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoginWithRefreshTokenAsync_WithValidCredentials_ReturnsTokenResult()
    {
        // Arrange
        var username = "testuser";
        var password = "Password123!";
        var user = new User { Id = Guid.NewGuid(), UserName = username, Email = "test@example.com", EmailConfirmed = true };
        var tokenResult = new TokenResult
        {
            AccessToken = "access_token",
            RefreshToken = "refresh_token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        _captchaServiceMock.Setup(x => x.IsCaptchaRequiredAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        _userManagerMock.Setup(x => x.FindByNameAsync(username))
            .ReturnsAsync(user);

        _signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, password, It.IsAny<bool>()))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        _loginSecurityServiceMock.Setup(x => x.DetectAbnormalLoginAsync(user.Id, It.IsAny<string>(), It.IsAny<string>()))
             .ReturnsAsync(AbnormalLoginResult.Normal());

        _passwordPolicyServiceMock.Setup(x => x.CheckPasswordExpirationAsync(user.Id))
             .ReturnsAsync(new PasswordExpirationResult { IsExpired = false });

        _userManagerMock.Setup(x => x.GetTwoFactorEnabledAsync(user))
            .ReturnsAsync(false);

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Jwt = new JwtOptions { EnableRefreshToken = true, RefreshTokenExpirationDays = 7, AccessTokenExpirationMinutes = 30 },
            SignIn = new TnziSignInOptions(),
            MultiLogin = new MultiLoginOptions { AllowMultiLogin = true },
            Captcha = new CaptchaOptions(),
            Registration = new RegistrationOptions()
        });

        _tokenServiceMock.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>()))
            .Returns("access_token");

        _authTokenServiceMock.Setup(x => x.SaveTokenAsync(user.Id, "JWT", "RefreshToken", It.IsAny<string>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(Guid.NewGuid());

        _sessionServiceMock.Setup(x => x.CreateSessionAsync(user.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Guid.NewGuid());

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _authService.LoginWithRefreshTokenAsync(new LoginDto { UserName = username, Password = password });

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data.AccessToken);
        Assert.NotNull(result.Data.RefreshToken);
    }

    [Fact]
    public async Task LoginWithRefreshTokenAsync_When2FaEnabled_PersistsTempTokenInFreshScope_AndReturnsChallenge()
    {
        // Arrange - a valid password login for a 2FA-enabled user.
        var username = "testuser";
        var password = "Password123!";
        var user = new User { Id = Guid.NewGuid(), UserName = username, Email = "test@example.com", EmailConfirmed = true };

        _captchaServiceMock.Setup(x => x.IsCaptchaRequiredAsync(It.IsAny<string>())).ReturnsAsync(false);
        _userManagerMock.Setup(x => x.FindByNameAsync(username)).ReturnsAsync(user);
        _signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, password, It.IsAny<bool>()))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
        _loginSecurityServiceMock.Setup(x => x.DetectAbnormalLoginAsync(user.Id, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AbnormalLoginResult.Normal());
        _passwordPolicyServiceMock.Setup(x => x.CheckPasswordExpirationAsync(user.Id))
            .ReturnsAsync(new PasswordExpirationResult { IsExpired = false });
        // 2FA is on → login must return a challenge, not tokens.
        _userManagerMock.Setup(x => x.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);
        _twoFactorServiceMock.Setup(x => x.GetEnabledTwoFactorTypesAsync(user))
            .ReturnsAsync(new List<TwoFactorType> { TwoFactorType.Totp, TwoFactorType.Email });

        // Act
        var result = await _authService.LoginWithRefreshTokenAsync(new LoginDto { UserName = username, Password = password });

        // Assert - challenge returned (403 / 2FA_REQUIRED), no tokens.
        Assert.False(result.Succeeded);
        Assert.Equal("2FA_REQUIRED", result.ErrorCode);

        // The temp token MUST be saved in the FRESH scope (survives the request's
        // UnitOfWork rollback), never on the ambient (rolled-back) token service.
        _scopedAuthTokenServiceMock.Verify(
            x => x.SaveTokenAsync(user.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>()),
            Times.Once);
        _authTokenServiceMock.Verify(
            x => x.SaveTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>()),
            Times.Never);
    }

    [Fact]
    public async Task LoginWithRefreshTokenAsync_When2FaEnabledButNoUsableMethod_SignsInWithoutChallenge()
    {
        // Arrange - the 2FA master flag is on, but every enabled method's channel
        // is disabled at the deployment level (GetEnabledTwoFactorTypesAsync returns
        // empty) → treat as 2FA off and sign in normally, never challenge.
        var username = "testuser";
        var password = "Password123!";
        var user = new User { Id = Guid.NewGuid(), UserName = username, Email = "test@example.com", EmailConfirmed = true };

        _captchaServiceMock.Setup(x => x.IsCaptchaRequiredAsync(It.IsAny<string>())).ReturnsAsync(false);
        _userManagerMock.Setup(x => x.FindByNameAsync(username)).ReturnsAsync(user);
        _signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, password, It.IsAny<bool>()))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
        _loginSecurityServiceMock.Setup(x => x.DetectAbnormalLoginAsync(user.Id, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AbnormalLoginResult.Normal());
        _passwordPolicyServiceMock.Setup(x => x.CheckPasswordExpirationAsync(user.Id))
            .ReturnsAsync(new PasswordExpirationResult { IsExpired = false });

        // Master flag on, but no method is currently usable.
        _userManagerMock.Setup(x => x.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);
        _twoFactorServiceMock.Setup(x => x.GetEnabledTwoFactorTypesAsync(user))
            .ReturnsAsync(new List<TwoFactorType>());

        _userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string> { "User" });
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Jwt = new JwtOptions { EnableRefreshToken = true, RefreshTokenExpirationDays = 7, AccessTokenExpirationMinutes = 30 },
            SignIn = new TnziSignInOptions(),
            MultiLogin = new MultiLoginOptions { AllowMultiLogin = true },
            Captcha = new CaptchaOptions(),
            Registration = new RegistrationOptions()
        });
        _tokenServiceMock.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>())).Returns("access_token");
        _authTokenServiceMock.Setup(x => x.SaveTokenAsync(user.Id, "JWT", "RefreshToken", It.IsAny<string>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(Guid.NewGuid());
        _sessionServiceMock.Setup(x => x.CreateSessionAsync(user.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Guid.NewGuid());
        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _authService.LoginWithRefreshTokenAsync(new LoginDto { UserName = username, Password = password });

        // Assert - full login (tokens), no 2FA challenge, no temp token persisted.
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data.AccessToken);
        Assert.NotEqual("2FA_REQUIRED", result.ErrorCode);
        _scopedAuthTokenServiceMock.Verify(
            x => x.SaveTokenAsync(user.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithValidRefreshToken_ReturnsNewTokenResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var refreshToken = "valid_refresh_token";
        var user = new User { Id = userId, UserName = "testuser" };
        var tokenResult = new TokenResult
        {
            AccessToken = "new_access_token",
            RefreshToken = "new_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        var tokenEntry = new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Value = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsUsed = false
        };

        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync("JWT", "RefreshToken", refreshToken))
            .ReturnsAsync(tokenEntry);

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        _tokenServiceMock.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>()))
            .Returns("new_access_token");

        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Jwt = new JwtOptions { RefreshTokenExpirationDays = 7, AccessTokenExpirationMinutes = 30 }
        });

        _tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("new_refresh_token");

        // 轮换是一次条件更新（旧值作为抢占条件），不再是「标记已用 + upsert」两步。
        _authTokenServiceMock
            .Setup(x => x.RotateRefreshTokenAsync(tokenEntry.Id, refreshToken, It.IsAny<string>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(true);

        // Act
        var result = await _authService.RefreshTokenAsync(refreshToken);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("new_access_token", result.Data.AccessToken);
        Assert.NotNull(result.Data.RefreshToken);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithInvalidRefreshToken_ReturnsFailure()
    {
        // Arrange
        var refreshToken = "invalid_refresh_token";

        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), refreshToken))
            .ReturnsAsync((AuthToken?)null);

        // Act
        var result = await _authService.RefreshTokenAsync(refreshToken);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenBoundSessionRevoked_Returns401AndDoesNotRotate()
    {
        // Arrange - a refresh token bound to a session that has since been revoked.
        var refreshToken = "session_bound_refresh";
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };
        var tokenEntry = new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Value = refreshToken,
            SessionId = sessionId,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsUsed = false
        };

        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync("JWT", "RefreshToken", refreshToken))
            .ReturnsAsync(tokenEntry);
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Jwt = new JwtOptions { RefreshTokenExpirationDays = 7, AccessTokenExpirationMinutes = 30 }
        });
        // Session is gone (revoked/expired) → refresh must be rejected.
        _sessionServiceMock.Setup(x => x.IsSessionValidAsync(sessionId)).ReturnsAsync(false);

        // Act
        var result = await _authService.RefreshTokenAsync(refreshToken);

        // Assert - 401, session-revoked code, and the token is NOT rotated.
        Assert.False(result.Succeeded);
        Assert.Equal(401, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_SESSION_REVOKED, result.ErrorCode);
        _authTokenServiceMock.Verify(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenBoundSessionValid_RotatesAndRenewsSession()
    {
        // Arrange - a refresh token bound to a still-valid session.
        var refreshToken = "session_bound_refresh_ok";
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };
        var tokenEntry = new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Value = refreshToken,
            SessionId = sessionId,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsUsed = false
        };

        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync("JWT", "RefreshToken", refreshToken))
            .ReturnsAsync(tokenEntry);
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string> { "User" });
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Jwt = new JwtOptions { EnableRefreshToken = true, RefreshTokenExpirationDays = 7, AccessTokenExpirationMinutes = 30 }
        });
        _sessionServiceMock.Setup(x => x.IsSessionValidAsync(sessionId)).ReturnsAsync(true);
        _sessionServiceMock.Setup(x => x.RenewSessionAsync(sessionId, It.IsAny<DateTime>())).ReturnsAsync(Result.Success());
        // The rotated access token + refresh token stay bound to the same session.
        _tokenServiceMock.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>(), null, sessionId))
            .Returns("rotated_access");
        _tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("rotated_refresh");
        _authTokenServiceMock
            .Setup(x => x.RotateRefreshTokenAsync(tokenEntry.Id, refreshToken, "rotated_refresh", It.IsAny<DateTime?>()))
            .ReturnsAsync(true);

        // Act
        var result = await _authService.RefreshTokenAsync(refreshToken);

        // Assert - rotated, and the session was renewed (sliding expiry).
        Assert.True(result.Succeeded);
        Assert.Equal("rotated_access", result.Data!.AccessToken);
        _sessionServiceMock.Verify(x => x.RenewSessionAsync(sessionId, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_WithValidUserId_ReturnsSuccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _sessionServiceMock.Setup(x => x.RevokeAllSessionsAsync(userId, null))
            .ReturnsAsync(Result.Success());

        _authTokenServiceMock.Setup(x => x.RemoveAllTokensAsync(userId, null))
            .Returns(Task.CompletedTask);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserLoggedOutEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _authService.LogoutAsync(userId);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SendTwoFactorCodeAsync_WithValidInput_ReturnsChallenge()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tempToken = "temp_token";
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com", PhoneNumber = "13800138000" };

        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), tempToken))
            .ReturnsAsync(new AuthToken { UserId = userId, Value = tempToken });

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _twoFactorServiceMock.Setup(x => x.SendEmailCodeAsync(userId, user.Email!, VerificationCodePurpose.TwoFactor))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _authService.SendTwoFactorCodeAsync(new SendTwoFactorCodeDto
        {
            TempToken = tempToken,
            Type = TwoFactorType.Email
        });

        // Assert - challenge succeeds and now surfaces CodeSent + a masked
        // destination so the login page can show "sent to t***@example.com".
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.True(result.Data!.CodeSent);
        Assert.Equal("t***@example.com", result.Data.MaskedAddress);
    }

    [Fact]
    public async Task VerifyTwoFactorAndLoginAsync_WithValidCode_ReturnsTokenResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tempToken = "temp_token";
        var code = "123456";
        var user = new User { Id = userId, UserName = "testuser" };
        var tokenResult = new TokenResult
        {
            AccessToken = "access_token",
            RefreshToken = "refresh_token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), tempToken))
            .ReturnsAsync(new AuthToken { UserId = userId, Value = tempToken });

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _twoFactorServiceMock.Setup(x => x.VerifyCodeAsync(userId, code, TwoFactorType.Email, VerificationCodePurpose.TwoFactor))
            .ReturnsAsync(Result.Success());

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        _tokenServiceMock.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>()))
            .Returns("access_token");

        _tokenServiceMock.Setup(x => x.GenerateRefreshToken())
            .Returns("refresh_token");

        _authTokenServiceMock.Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>()))
            .ReturnsAsync(true);

        _authTokenServiceMock.Setup(x => x.SaveTokenAsync(userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _authService.VerifyTwoFactorAndLoginAsync(new VerifyTwoFactorDto
        {
            TempToken = tempToken,
            Code = code,
            Type = TwoFactorType.Email
        });

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("access_token", result.Data.AccessToken);
    }

    // ------------------------------------------------- 第二步用 passkey / 安全密钥

    /// <summary>The account is in the middle of a login challenge that offers the passkey method.</summary>
    private (User user, Mock<IPasskeyService> passkeys) GivenAPasskeyTwoFactorChallenge(string tempToken, bool offersPasskey = true)
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "keyholder" };
        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), tempToken))
            .ReturnsAsync(new AuthToken { Id = Guid.NewGuid(), UserId = user.Id, Value = tempToken });
        _userManagerMock.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _twoFactorServiceMock.Setup(x => x.GetEnabledTwoFactorTypesAsync(user))
            .ReturnsAsync(offersPasskey ? [TwoFactorType.Passkey, TwoFactorType.Email] : [TwoFactorType.Email]);

        // IPasskeyService is resolved at the call site (PasskeyService itself depends on IAuthService).
        var passkeys = new Mock<IPasskeyService>();
        _serviceProviderMock.Setup(x => x.GetService(typeof(IPasskeyService))).Returns(passkeys.Object);

        _userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string> { "User" });
        _tokenServiceMock.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>())).Returns("access_token");
        _tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("refresh_token");
        _authTokenServiceMock.Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        _authTokenServiceMock.Setup(x => x.SaveTokenAsync(user.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(Guid.NewGuid());
        return (user, passkeys);
    }

    [Fact]
    public async Task BeginTwoFactorPasskeyAsync_BuildsTheOptionsForTheChallengedAccount()
    {
        var (user, passkeys) = GivenAPasskeyTwoFactorChallenge("temp");
        passkeys.Setup(x => x.BeginAssertionForUserAsync(user.Id))
            .ReturnsAsync(Result<PasskeyOptionsDto>.Success(new PasskeyOptionsDto { OptionsJson = "{}", StateId = "s" }));

        var result = await _authService.BeginTwoFactorPasskeyAsync(new TwoFactorPasskeyBeginDto { TempToken = "temp" });

        Assert.True(result.Succeeded);
        // ★ Bound to the account the temp token names, never to a caller-supplied user: that is what
        //   puts the account's own credentials into allowCredentials, which a YubiKey needs.
        passkeys.Verify(x => x.BeginAssertionForUserAsync(user.Id), Times.Once);
    }

    /// <summary>
    /// 与发码 / 验码同一口径：挑战没提供 Passkey（用户没启用、渠道关着、凭据删光）时拒绝，
    /// 否则持有临时令牌者可以绕过用户单独关掉的方式。
    /// </summary>
    [Fact]
    public async Task BeginTwoFactorPasskeyAsync_RefusesWhenTheChallengeDoesNotOfferPasskey()
    {
        var (_, passkeys) = GivenAPasskeyTwoFactorChallenge("temp", offersPasskey: false);

        var result = await _authService.BeginTwoFactorPasskeyAsync(new TwoFactorPasskeyBeginDto { TempToken = "temp" });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        passkeys.Verify(x => x.BeginAssertionForUserAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task VerifyTwoFactorWithPasskeyAndLoginAsync_WithTheAccountsOwnPasskey_SignsIn()
    {
        var (user, passkeys) = GivenAPasskeyTwoFactorChallenge("temp");
        passkeys.Setup(x => x.VerifyAssertionAsync(It.IsAny<PasskeyCompleteDto>()))
            .ReturnsAsync(Result<Guid>.Success(user.Id));

        var result = await _authService.VerifyTwoFactorWithPasskeyAndLoginAsync(
            new TwoFactorPasskeyCompleteDto { TempToken = "temp", StateId = "s", CredentialJson = "{}" });

        Assert.True(result.Succeeded);
        Assert.Equal("access_token", result.Data!.AccessToken);
        // Same tail as the code path: the temp token is burnt.
        _authTokenServiceMock.Verify(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>()), Times.Once);
    }

    /// <summary>
    /// ★★ 断言成功只说明「有一把登记过的 passkey 在场」。属于另一个账号时证明的是别人在场，
    /// 少了这一比，任何持有自己 passkey 的人都能替一个猜对了密码的账号完成第二步。
    /// </summary>
    [Fact]
    public async Task VerifyTwoFactorWithPasskeyAndLoginAsync_WithSomeoneElsesPasskey_IsRejectedLikeAWrongCode()
    {
        var (_, passkeys) = GivenAPasskeyTwoFactorChallenge("temp");
        passkeys.Setup(x => x.VerifyAssertionAsync(It.IsAny<PasskeyCompleteDto>()))
            .ReturnsAsync(Result<Guid>.Success(Guid.NewGuid()));

        var result = await _authService.VerifyTwoFactorWithPasskeyAndLoginAsync(
            new TwoFactorPasskeyCompleteDto { TempToken = "temp", StateId = "s", CredentialJson = "{}" });

        Assert.False(result.Succeeded);
        Assert.Equal(401, result.Code);
        Assert.Equal("Invalid passkey", result.Message);
        _authTokenServiceMock.Verify(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>()), Times.Never);
        _tokenServiceMock.Verify(x => x.GenerateToken(It.IsAny<User>(), It.IsAny<IList<string>>()), Times.Never);
    }

    /// <summary>A failed assertion answers with the same words as a foreign passkey: nothing to probe.</summary>
    [Fact]
    public async Task VerifyTwoFactorWithPasskeyAndLoginAsync_WithAnInvalidAssertion_IsRejectedTheSameWay()
    {
        var (_, passkeys) = GivenAPasskeyTwoFactorChallenge("temp");
        passkeys.Setup(x => x.VerifyAssertionAsync(It.IsAny<PasskeyCompleteDto>()))
            .ReturnsAsync(Result<Guid>.Failure("Invalid passkey", 401, "UNAUTHORIZED"));

        var result = await _authService.VerifyTwoFactorWithPasskeyAndLoginAsync(
            new TwoFactorPasskeyCompleteDto { TempToken = "temp", StateId = "s", CredentialJson = "{}" });

        Assert.False(result.Succeeded);
        Assert.Equal(401, result.Code);
        Assert.Equal("Invalid passkey", result.Message);
    }

    /// <summary>The code endpoint does not accept a passkey: the assertion has its own two legs.</summary>
    [Fact]
    public async Task VerifyTwoFactorAndLoginAsync_RefusesThePasskeyTypeAsACode()
    {
        var (_, _) = GivenAPasskeyTwoFactorChallenge("temp");

        var result = await _authService.VerifyTwoFactorAndLoginAsync(new VerifyTwoFactorDto { TempToken = "temp", Code = "123456", Type = TwoFactorType.Passkey });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        _twoFactorServiceMock.Verify(x => x.VerifyCodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>()), Times.Never);
    }

    /// <summary>
    /// 临时令牌的消费是条件更新：false 说明并发的另一个请求已经拿它完成了第二步。
    /// 不按返回值放行，同一枚令牌 + 同一个验证码并发打两次就建出两条会话。
    /// </summary>
    [Fact]
    public async Task VerifyTwoFactorAndLoginAsync_WhenTheTempTokenWasConsumedConcurrently_IssuesNothing()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };
        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), "temp"))
            .ReturnsAsync(new AuthToken { Id = Guid.NewGuid(), UserId = userId, Value = "temp" });
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _twoFactorServiceMock.Setup(x => x.VerifyCodeAsync(userId, "123456", TwoFactorType.Email, VerificationCodePurpose.TwoFactor))
            .ReturnsAsync(Result.Success());
        _authTokenServiceMock.Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>())).ReturnsAsync(false);

        var result = await _authService.VerifyTwoFactorAndLoginAsync(
            new VerifyTwoFactorDto { TempToken = "temp", Code = "123456", Type = TwoFactorType.Email });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        _tokenServiceMock.Verify(
            x => x.GenerateToken(It.IsAny<User>(), It.IsAny<IList<string>>(), It.IsAny<IEnumerable<Claim>>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    /// <summary>
    /// 失败计数经 <c>UpdateAsync</c> 落库、先过用户校验器。存量账号过不了当前规则时计数写不进去，
    /// 锁定永远到不了阈值 —— 这时退到「一枚令牌只有一次机会」：猜错即烧掉临时令牌。
    /// </summary>
    [Fact]
    public async Task VerifyTwoFactorAndLoginAsync_WhenTheFailureCannotBeRecorded_ConsumesTheTempToken()
    {
        var userId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "legacy" };
        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), "temp"))
            .ReturnsAsync(new AuthToken { Id = tokenId, UserId = userId, Value = "temp" });
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.SupportsUserLockout).Returns(true);
        _userManagerMock.Setup(x => x.AccessFailedAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "DuplicateUserName", Description = "Username is taken." }));
        _twoFactorServiceMock.Setup(x => x.VerifyCodeAsync(userId, "000000", TwoFactorType.Email, VerificationCodePurpose.TwoFactor))
            .ReturnsAsync(Result.Failure("Invalid code"));

        var result = await _authService.VerifyTwoFactorAndLoginAsync(
            new VerifyTwoFactorDto { TempToken = "temp", Code = "000000", Type = TwoFactorType.Email });

        Assert.False(result.Succeeded);
        _authTokenServiceMock.Verify(x => x.MarkTokenAsUsedAsync(tokenId), Times.Once);
    }

    [Fact]
    public async Task VerifyTwoFactorAndLoginAsync_WithInvalidCode_ReturnsFailure()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tempToken = "temp_token";
        var code = "wrong_code";
        var user = new User { Id = userId, UserName = "testuser" };

        _authTokenServiceMock.Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), tempToken))
            .ReturnsAsync(new AuthToken { UserId = userId, Value = tempToken });

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _twoFactorServiceMock.Setup(x => x.VerifyCodeAsync(userId, code, TwoFactorType.Email, VerificationCodePurpose.TwoFactor))
            .ReturnsAsync(Result.Failure("Invalid code"));

        // Act
        var result = await _authService.VerifyTwoFactorAndLoginAsync(new VerifyTwoFactorDto
        {
            TempToken = tempToken,
            Code = code,
            Type = TwoFactorType.Email
        });

        // Assert
        Assert.False(result.Succeeded);
    }

    // ------------------------------------------------- 账号锁定 / 停用：逐条签发路径

    /// <summary>
    /// A disabled account must not be able to sign in through a login method whose credential
    /// check happens outside SignInManager (passkey today, anything added later).
    /// </summary>
    /// <remarks>
    /// "Disable this account" is <c>SetLockoutEndDateAsync(+100 years)</c> in this framework.
    /// Password login never reaches here while locked out because
    /// <c>CheckPasswordSignInAsync</c> rejects first - which is exactly why the check had to be
    /// pulled out into <c>LockedAccountLoginGuard</c>: it was riding on "verify the password",
    /// so every credential-elsewhere method silently skipped it.
    /// </remarks>
    [Fact]
    public async Task IssueTokenAsync_WhenAccountIsLockedOut_RejectsAndIssuesNothing()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "disabled-user" };
        _userManagerMock.Setup(x => x.SupportsUserLockout).Returns(true);
        _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(true);

        var result = await _authService.IssueTokenAsync(user, LoginMethod.Passkey);

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_LOCKED, result.ErrorCode);

        // Rejected before anything happens: no token minted, no login-success trace.
        _tokenServiceMock.Verify(
            x => x.GenerateToken(It.IsAny<User>(), It.IsAny<IList<string>>(), It.IsAny<IEnumerable<Claim>>(), It.IsAny<Guid?>()),
            Times.Never);
        _eventBusMock.Verify(
            x => x.PublishAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The lockout guard must not swallow the normal path - without this the previous test
    /// would still pass on an implementation that rejects everyone.
    /// </summary>
    [Fact]
    public async Task IssueTokenAsync_WhenAccountIsUsable_IssuesThroughTheSharedExit()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "passkey-user" };
        _userManagerMock.Setup(x => x.SupportsUserLockout).Returns(true);
        _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(false);
        _userManagerMock.Setup(x => x.GetTwoFactorEnabledAsync(user)).ReturnsAsync(false);
        _userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string> { "User" });
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Jwt = new JwtOptions { EnableRefreshToken = true, RefreshTokenExpirationDays = 7, AccessTokenExpirationMinutes = 30 }
        });
        _tokenServiceMock
            .Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>(), It.IsAny<IEnumerable<Claim>>(), It.IsAny<Guid?>()))
            .Returns("issued");
        _tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("refresh");
        _authTokenServiceMock
            .Setup(x => x.SaveTokenAsync(user.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<Guid>()))
            .ReturnsAsync(Guid.NewGuid());

        var result = await _authService.IssueTokenAsync(user, LoginMethod.Passkey);

        Assert.True(result.Succeeded);
        Assert.Equal("issued", result.Data!.AccessToken);
    }

    /// <summary>
    /// 验证码登录同样挡得住被停用的账号。
    /// </summary>
    /// <remarks>
    /// ★ 这条路径的凭据是一次性验证码，全程不碰 <c>SignInManager</c> ——
    /// 在守卫收口之前它和 passkey 一样是敞开的，只是没人注意到。
    /// 单测守卫本身证明不了这个：守卫对不对，与它有没有挂在这条路径上，是两回事。
    /// </remarks>
    [Fact]
    public async Task CodeLoginAsync_WhenAccountIsLockedOut_RejectsAndIssuesNothing()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "code-login-user",
            Email = "code@example.com",
            EmailConfirmed = true,
            PasswordHash = "hash"
        };

        _userManagerMock.Setup(x => x.SupportsUserLockout).Returns(true);
        _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(true);
        _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _twoFactorServiceMock
            .Setup(x => x.VerifyCodeByAddressAndMarkUsedAsync(user.Email, It.IsAny<string>(), TwoFactorType.Email, VerificationCodePurpose.CodeLogin))
            .ReturnsAsync(Result<Guid?>.Success(user.Id));

        var result = await _authService.CodeLoginAsync(new CodeLoginDto
        {
            Email = user.Email,
            Code = "123456",
            Type = TwoFactorType.Email
        });

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_LOCKED, result.ErrorCode);
        _tokenServiceMock.Verify(
            x => x.GenerateToken(It.IsAny<User>(), It.IsAny<IList<string>>(), It.IsAny<IEnumerable<Claim>>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    /// <summary>
    /// ★★★ 验证码登录不再是 2FA 的绕过路径：账号开着 TOTP 时，一封邮件只完成第一步。
    /// </summary>
    /// <remarks>
    /// 修复前这条路径手工复制了 <c>IssueTokenAsync</c> 的后半段、唯独漏掉 2FA 判定，
    /// 于是强度阶梯是反的 —— passkey 这种强凭据要过 2FA，邮箱验证码反而不用。
    /// </remarks>
    [Fact]
    public async Task CodeLoginAsync_WhenAnotherFactorIsEnabled_ChallengesInsteadOfIssuing()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "code-2fa-user",
            Email = "code2fa@example.com",
            EmailConfirmed = true,
            PasswordHash = "hash"
        };

        _userManagerMock.Setup(x => x.SupportsUserLockout).Returns(false);
        _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _twoFactorServiceMock
            .Setup(x => x.VerifyCodeByAddressAndMarkUsedAsync(user.Email, It.IsAny<string>(), TwoFactorType.Email, VerificationCodePurpose.CodeLogin))
            .ReturnsAsync(Result<Guid?>.Success(user.Id));

        // 账号启用的是 TOTP —— 与本次用掉的邮箱渠道不是同一个因子，必须照常挑战。
        _userManagerMock.Setup(x => x.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);
        _twoFactorServiceMock.Setup(x => x.GetEnabledTwoFactorTypesAsync(user))
            .ReturnsAsync(new List<TwoFactorType> { TwoFactorType.Totp });

        var result = await _authService.CodeLoginAsync(new CodeLoginDto
        {
            Email = user.Email,
            Code = "123456",
            Type = TwoFactorType.Email
        });

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_2FA_REQUIRED, result.ErrorCode);

        // 临时令牌必须原样带出，否则前端拿不到 tempToken，挑战无从继续。
        Assert.NotNull(result.ErrorDetails);

        _tokenServiceMock.Verify(
            x => x.GenerateToken(It.IsAny<User>(), It.IsAny<IList<string>>(), It.IsAny<IEnumerable<Claim>>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    /// <summary>
    /// 另一半：本次用掉的渠道正是账号唯一启用的 2FA 方式时，不再问第二遍 —— 那是同一个因子。
    /// </summary>
    /// <remarks>
    /// ★ 少了这条，上一条会在一个「验证码登录一律挑战」的实现上照样通过，
    /// 而那个实现会让邮箱 2FA 的用户连着输两次邮箱验证码。
    /// </remarks>
    [Fact]
    public async Task CodeLoginAsync_WhenOnlyTheUsedFactorIsEnabled_IssuesWithoutSecondChallenge()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "code-email2fa-user",
            Email = "email2fa@example.com",
            EmailConfirmed = true,
            PasswordHash = "hash"
        };

        _userManagerMock.Setup(x => x.SupportsUserLockout).Returns(false);
        _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string> { "User" });
        _twoFactorServiceMock
            .Setup(x => x.VerifyCodeByAddressAndMarkUsedAsync(user.Email, It.IsAny<string>(), TwoFactorType.Email, VerificationCodePurpose.CodeLogin))
            .ReturnsAsync(Result<Guid?>.Success(user.Id));

        // 唯一启用的 2FA 方式就是邮箱，而本次登录已经证明了「能收这个邮箱」。
        _userManagerMock.Setup(x => x.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);
        _twoFactorServiceMock.Setup(x => x.GetEnabledTwoFactorTypesAsync(user))
            .ReturnsAsync(new List<TwoFactorType> { TwoFactorType.Email });

        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Jwt = new JwtOptions { EnableRefreshToken = true, RefreshTokenExpirationDays = 7, AccessTokenExpirationMinutes = 30 }
        });
        _tokenServiceMock
            .Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>(), It.IsAny<IEnumerable<Claim>>(), It.IsAny<Guid?>()))
            .Returns("issued");
        _tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("refresh");
        _authTokenServiceMock
            .Setup(x => x.SaveTokenAsync(user.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<Guid>()))
            .ReturnsAsync(Guid.NewGuid());
        _sessionServiceMock.Setup(x => x.CreateSessionAsync(user.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Guid.NewGuid());

        var result = await _authService.CodeLoginAsync(new CodeLoginDto
        {
            Email = user.Email,
            Code = "123456",
            Type = TwoFactorType.Email
        });

        Assert.True(result.Succeeded);
        Assert.Equal("issued", result.Data!.AccessToken);

        // 没有发出任何挑战：临时令牌一次都没存过。
        _scopedAuthTokenServiceMock.Verify(
            x => x.SaveTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>()),
            Times.Never);
    }

    /// <summary>
    /// 开着登录图形验证码的部署，免密验证码登录的发码入口同样要过图形验证码。
    /// </summary>
    /// <remarks>
    /// ★ 这是唯一一条每次调用都真的产生短信/邮件费用的匿名入口。此前它完全不受
    /// <c>EnableCaptchaOnLogin</c> 管辖，等于开着验证码的部署仍留着一个无门的发信口。
    /// </remarks>
    [Fact]
    public async Task SendCodeLoginCodeAsync_WhenLoginCaptchaEnabledAndMissing_RejectsBeforeSending()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Captcha = new CaptchaOptions { EnableCaptchaOnLogin = true },
            Otp = new OtpOptions { EnableEmail = true }
        });

        var result = await _authService.SendCodeLoginCodeAsync(new SendCodeLoginCodeDto
        {
            Email = "nocaptcha@example.com",
            Type = TwoFactorType.Email
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, result.ErrorCode);
        _twoFactorServiceMock.Verify(
            x => x.SendCodeByAddressAsync(It.IsAny<string>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    /// <summary>
    /// 对照组：图形验证码过了就照常发码，且用途必须是 <c>CodeLogin</c>。
    /// </summary>
    /// <remarks>
    /// ★ 用途写死在这条断言里 —— 发成别的用途，这枚码在 <c>code-login</c> 上验不过，
    /// 而那种失效不会让任何编译或既有测试变红。
    /// </remarks>
    /// <summary>
    /// 「未启用，放行」不是「校验通过」：登录验证码开着，验证器却报告没有生效的提供商，必须拒绝而不是照发。
    /// </summary>
    [Fact]
    public async Task SendCodeLoginCodeAsync_WhenLoginCaptchaIsOnButTheVerifierReportsNotEnabled_Rejects()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Captcha = new CaptchaOptions { EnableCaptchaOnLogin = true },
            Otp = new OtpOptions { EnableEmail = true },
            Registration = new RegistrationOptions { EnableQuickRegisterEmail = true }
        });
        _captchaVerifierMock.Setup(x => x.VerifyAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CaptchaVerification.NotEnabled());

        var result = await _authService.SendCodeLoginCodeAsync(new SendCodeLoginCodeDto
        {
            Email = "captcha@example.com",
            Type = TwoFactorType.Email,
            CaptchaId = "cid",
            CaptchaCode = "whatever"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, result.ErrorCode);
        _twoFactorServiceMock.Verify(
            x => x.SendCodeByAddressAsync(It.IsAny<string>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Fact]
    public async Task SendCodeLoginCodeAsync_WhenCaptchaValid_SendsWithCodeLoginPurpose()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Captcha = new CaptchaOptions { EnableCaptchaOnLogin = true },
            Otp = new OtpOptions { EnableEmail = true },
            // 地址是未知的，所以快速注册必须开着才会真的发出去 —— 关着时这条路径
            // 现在只回同一句话而不发信（见 SendCodeLoginCode_ForAnUnknownAddress_*）。
            Registration = new RegistrationOptions { EnableQuickRegisterEmail = true }
        });
        _captchaVerifierMock.Setup(x => x.VerifyAsync("cid:good", CaptchaPurpose.Login, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CaptchaVerification.Pass("image"));
        _userManagerMock.Setup(x => x.FindByEmailAsync("captcha@example.com")).ReturnsAsync((User?)null);
        _twoFactorServiceMock
            .Setup(x => x.SendCodeByAddressAsync("captcha@example.com", TwoFactorType.Email, VerificationCodePurpose.CodeLogin, null))
            .ReturnsAsync(Result.Success());

        var result = await _authService.SendCodeLoginCodeAsync(new SendCodeLoginCodeDto
        {
            Email = "captcha@example.com",
            Type = TwoFactorType.Email,
            CaptchaId = "cid",
            CaptchaCode = "good"
        });

        Assert.True(result.Succeeded);
        _twoFactorServiceMock.Verify(
            x => x.SendCodeByAddressAsync("captcha@example.com", TwoFactorType.Email, VerificationCodePurpose.CodeLogin, null),
            Times.Once);
    }

    /// <summary>
    /// ★ 关掉 `AllowCodeLogin` 的部署，发码入口自己就要拒绝，而不是靠前端不显示按钮。
    /// </summary>
    /// <remarks>
    /// 这个开关存在的理由是解耦：在它之前，想关掉免密验证码登录只能关 OTP 渠道总闸，
    /// 而那会连带关掉邮箱/短信 2FA 与验证码找回密码 —— 想少一种登录方式，
    /// 不该被迫连 2FA 一起放弃。
    /// </remarks>
    [Fact]
    public async Task SendCodeLoginCodeAsync_WhenCodeLoginDisabled_RefusesBeforeSending()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            SignIn = new TnziSignInOptions { AllowCodeLogin = false },
            Otp = new OtpOptions { EnableEmail = true }
        });

        var result = await _authService.SendCodeLoginCodeAsync(new SendCodeLoginCodeDto
        {
            Email = "disabled@example.com",
            Type = TwoFactorType.Email
        });

        Assert.False(result.Succeeded);
        _twoFactorServiceMock.Verify(
            x => x.SendCodeByAddressAsync(It.IsAny<string>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    /// <summary>
    /// 登录入口也要挡：关掉开关之后，手里还攥着一枚未用码的人依然能直接调 code-login。
    /// </summary>
    /// <remarks>
    /// ★ 只挡发码等于给那些已经发出去、尚未过期的码留了一扇后门。
    /// </remarks>
    [Fact]
    public async Task CodeLoginAsync_WhenCodeLoginDisabled_RefusesEvenWithAValidCode()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            SignIn = new TnziSignInOptions { AllowCodeLogin = false }
        });

        var result = await _authService.CodeLoginAsync(new CodeLoginDto
        {
            Email = "disabled@example.com",
            Code = "123456",
            Type = TwoFactorType.Email
        });

        Assert.False(result.Succeeded);
        // 码根本没被核销 - 拒绝发生在验码之前。
        _twoFactorServiceMock.Verify(
            x => x.VerifyCodeByAddressAndMarkUsedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>()),
            Times.Never);
        _tokenServiceMock.Verify(
            x => x.GenerateToken(It.IsAny<User>(), It.IsAny<IList<string>>(), It.IsAny<IEnumerable<Claim>>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    /// <summary>
    /// `/auth/config` 的 `enableCodeLogin` 是「登录方式开着 **且** 至少一条渠道能送达」。
    /// </summary>
    /// <remarks>
    /// ★ 对照组不可省：只断言「关掉时为 false」的话，一个恒返回 false 的实现也能通过，
    /// 而那会让每个部署的登录页都少一个入口。
    /// </remarks>
    [Fact]
    public void GetAuthConfig_CodeLoginNeedsBothTheMethodAndAChannel()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            SignIn = new TnziSignInOptions { AllowCodeLogin = false },
            Otp = new OtpOptions { EnableEmail = true, EnableSms = true }
        });
        var off = _authService.GetAuthConfig();
        Assert.False(off.Data!.EnableCodeLogin);
        Assert.False(off.Data.CodeLoginViaEmail);
        Assert.False(off.Data.CodeLoginViaSms);

        // 对照组：方式开着 + 渠道开着 → 可用（且渠道各自独立反映）。
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            SignIn = new TnziSignInOptions { AllowCodeLogin = true },
            Otp = new OtpOptions { EnableEmail = true, EnableSms = false }
        });
        var on = _authService.GetAuthConfig();
        Assert.True(on.Data!.EnableCodeLogin);
        Assert.True(on.Data.CodeLoginViaEmail);
        Assert.False(on.Data.CodeLoginViaSms);
    }

    /// <summary>
    /// `/auth/config` 报出邮件 / 短信验证码的位数，且按请求现读：运行时把位数从 6 改成 8，
    /// 下一次请求就是 8。前端据此决定输入框格数 —— 报错或报旧值，用户收到的 8 位码就敲不进 6 格。
    /// </summary>
    [Fact]
    public void GetAuthConfig_ReportsTheCurrentOtpCodeLength()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions());
        Assert.Equal(6, _authService.GetAuthConfig().Data!.OtpCodeLength);

        // 同一个服务实例、配置换了：必须反映新值，而不是构造期捕获的那份。
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions { Otp = new OtpOptions { CodeLength = 8 } });
        Assert.Equal(8, _authService.GetAuthConfig().Data!.OtpCodeLength);

        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions { Otp = new OtpOptions { CodeLength = 4 } });
        Assert.Equal(4, _authService.GetAuthConfig().Data!.OtpCodeLength);
    }

    /// <summary>
    /// 2FA 第二步同样挡得住 —— 第一步之后账号才被停用的竞态。
    /// </summary>
    [Fact]
    public async Task VerifyTwoFactorAndLoginAsync_WhenAccountIsLockedOut_RejectsAndIssuesNothing()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "2fa-user" };
        const string tempToken = "temp_token";

        _userManagerMock.Setup(x => x.SupportsUserLockout).Returns(true);
        _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(true);
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _authTokenServiceMock
            .Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), tempToken))
            .ReturnsAsync(new AuthToken { UserId = userId, Value = tempToken, ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
        _authTokenServiceMock.Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        _twoFactorServiceMock
            .Setup(x => x.VerifyCodeAsync(userId, It.IsAny<string>(), TwoFactorType.Email, VerificationCodePurpose.TwoFactor))
            .ReturnsAsync(Result.Success());

        var result = await _authService.VerifyTwoFactorAndLoginAsync(new VerifyTwoFactorDto
        {
            TempToken = tempToken,
            Code = "123456",
            Type = TwoFactorType.Email
        });

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_LOCKED, result.ErrorCode);
        _tokenServiceMock.Verify(
            x => x.GenerateToken(It.IsAny<User>(), It.IsAny<IList<string>>(), It.IsAny<IEnumerable<Claim>>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    #region 待办义务挑战

    /// <summary>
    /// ★★★ 欠着义务时**不签发令牌**，而是发一个带临时令牌的挑战。
    /// </summary>
    /// <remarks>
    /// 拦在建立会话之前：会话建起来、令牌签出去之后再拦是没有意义的，
    /// 那时业务接口已经能访问，「强制」二字就没有了。
    /// </remarks>
    [Fact]
    public async Task IssueTokenAsync_WhenObligationsAreOwed_ChallengesInsteadOfIssuing()
    {
        var user = OwingUser(PendingUserActions.ChangePassword);
        _userManagerMock.Setup(x => x.GetTwoFactorEnabledAsync(user)).ReturnsAsync(false);

        var result = await _authService.IssueTokenAsync(user, LoginMethod.Password);

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_PENDING_ACTIONS_REQUIRED, result.ErrorCode);
        Assert.Null(result.Data);
    }

    /// <summary>
    /// 挑战要把临时令牌与欠的事一起交给前端，否则那一步无从继续
    /// （与 2FA 挑战丢掉 ErrorDetails 是同一类缺陷）。
    /// </summary>
    [Fact]
    public async Task PendingActionChallenge_CarriesTempTokenAndActionNames()
    {
        var user = OwingUser(PendingUserActions.ChangePassword | PendingUserActions.EnrollTotp);
        _userManagerMock.Setup(x => x.GetTwoFactorEnabledAsync(user)).ReturnsAsync(false);

        var result = await _authService.IssueTokenAsync(user, LoginMethod.Password);

        var details = result.ErrorDetails!;
        var tempToken = details.GetType().GetProperty("TempToken")!.GetValue(details) as string;
        var actions = details.GetType().GetProperty("RequiredActions")!.GetValue(details) as IReadOnlyList<string>;

        Assert.False(string.IsNullOrWhiteSpace(tempToken));
        Assert.Equal(2, actions!.Count);
        Assert.Contains(nameof(PendingUserActions.ChangePassword), actions);
    }

    /// <summary>
    /// ★★ 阻断位不该被当成义务：那个人根本不该进来，给他一个改密表单是错的
    /// （他还没有密码可改）。守卫在更早的地方就否决了。
    /// </summary>
    [Fact]
    public async Task BlockingActions_AreRejectedByTheGuard_NotTurnedIntoAnObligationChallenge()
    {
        var user = OwingUser(PendingUserActions.InvitationPending);

        var result = await _authService.IssueTokenAsync(user, LoginMethod.Password);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_ACTIVATION_PENDING, result.ErrorCode);
    }

    /// <summary>
    /// ★★★ 义务在 2FA <b>之后</b>才问 —— 这是安全属性不是体验偏好。
    /// </summary>
    /// <remarks>
    /// 反过来的话，拿到泄露密码的人可以直接进入改密流程、<b>绕过两步验证</b>
    /// 把密码改成自己的。所以开着 2FA 的账号即使欠着改密，也必须先看到 2FA 挑战。
    /// </remarks>
    [Fact]
    public async Task TwoFactorChallenge_ComesBeforeTheObligationChallenge()
    {
        var user = OwingUser(PendingUserActions.ChangePassword);
        _userManagerMock.Setup(x => x.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);
        _twoFactorServiceMock
            .Setup(x => x.GetEnabledTwoFactorTypesAsync(user))
            .ReturnsAsync([TwoFactorType.Totp]);

        var result = await _authService.IssueTokenAsync(user, LoginMethod.Password);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_2FA_REQUIRED, result.ErrorCode);
    }

    /// <summary>
    /// 对照组：什么都不欠时照常签发。没有它，一个「全都拒绝」的实现也能让上面几条通过。
    /// </summary>
    [Fact]
    public async Task IssueTokenAsync_WithNothingOwed_IssuesNormally()
    {
        var user = OwingUser(PendingUserActions.None);
        _userManagerMock.Setup(x => x.GetTwoFactorEnabledAsync(user)).ReturnsAsync(false);

        var result = await _authService.IssueTokenAsync(user, LoginMethod.Password);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
    }

    /// <summary>
    /// ★★★ 账号不存在且快速注册关着时不发信，但回答与发了信逐字相同。
    /// </summary>
    /// <remarks>
    /// 那枚码拿到 <c>CodeLoginAsync</c> 必然 404，所以发出去是纯支出：短信/邮件费用、
    /// 发信人信誉，以及一封带着本部署品牌的验证码邮件落进一个与本站无关的邮箱。
    /// 节流按地址分桶，换个地址就是新桶，挡不住这件事。
    /// 回答必须与「真的发了」一字不差，否则这个匿名端点就成了账号枚举预言机 ——
    /// 所以断言的是两次调用的 <c>Message</c> 相等，不是某个具体字符串。
    /// </remarks>
    [Fact]
    public async Task SendCodeLoginCode_ForAnUnknownAddress_SendsNothingAndAnswersIdentically()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            SignIn = new TnziSignInOptions { AllowCodeLogin = true },
            Captcha = new CaptchaOptions { EnableCaptchaOnLogin = false },
            Otp = new OtpOptions { EnableEmail = true },
            Registration = new RegistrationOptions { EnableQuickRegisterEmail = false },
        });

        var known = new User { Id = Guid.NewGuid(), UserName = "known", Email = "known@example.com" };
        _userManagerMock.Setup(x => x.FindByEmailAsync(known.Email)).ReturnsAsync(known);
        _userManagerMock.Setup(x => x.FindByEmailAsync("nobody@example.com")).ReturnsAsync((User?)null);
        _twoFactorServiceMock
            .Setup(x => x.SendCodeByAddressAsync(It.IsAny<string>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>(), It.IsAny<Guid?>()))
            .ReturnsAsync(Result<string>.Success("sent"));

        var unknown = await _authService.SendCodeLoginCodeAsync(
            new SendCodeLoginCodeDto { Email = "nobody@example.com", Type = TwoFactorType.Email });
        var existing = await _authService.SendCodeLoginCodeAsync(
            new SendCodeLoginCodeDto { Email = known.Email, Type = TwoFactorType.Email });

        Assert.True(unknown.Succeeded);
        Assert.True(existing.Succeeded);
        Assert.Equal(existing.Message, unknown.Message);
        Assert.Equal(existing.Code, unknown.Code);

        // 只为存在的账号发过一次。
        _twoFactorServiceMock.Verify(
            x => x.SendCodeByAddressAsync("nobody@example.com", It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>(), It.IsAny<Guid?>()),
            Times.Never);
        _twoFactorServiceMock.Verify(
            x => x.SendCodeByAddressAsync(known.Email, TwoFactorType.Email, VerificationCodePurpose.CodeLogin, known.Id),
            Times.Once);
    }

    /// <summary>
    /// 对照组：快速注册开着时，未知地址就是一个待注册的新用户 —— 照发。
    /// </summary>
    [Fact]
    public async Task SendCodeLoginCode_ForAnUnknownAddress_StillSendsWhenQuickRegistrationIsOn()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            SignIn = new TnziSignInOptions { AllowCodeLogin = true },
            Captcha = new CaptchaOptions { EnableCaptchaOnLogin = false },
            Otp = new OtpOptions { EnableEmail = true },
            Registration = new RegistrationOptions { EnableQuickRegisterEmail = true },
        });

        _userManagerMock.Setup(x => x.FindByEmailAsync("newcomer@example.com")).ReturnsAsync((User?)null);
        _twoFactorServiceMock
            .Setup(x => x.SendCodeByAddressAsync(It.IsAny<string>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>(), It.IsAny<Guid?>()))
            .ReturnsAsync(Result<string>.Success("sent"));

        var result = await _authService.SendCodeLoginCodeAsync(
            new SendCodeLoginCodeDto { Email = "newcomer@example.com", Type = TwoFactorType.Email });

        Assert.True(result.Succeeded);
        _twoFactorServiceMock.Verify(
            x => x.SendCodeByAddressAsync("newcomer@example.com", TwoFactorType.Email, VerificationCodePurpose.CodeLogin, null),
            Times.Once);
    }

    /// <summary>
    /// ★ 关掉验证码登录的部署里，发码请求注定被拒 —— 那就不该先消费掉用户手里那张图形验证码。
    /// </summary>
    [Fact]
    public async Task SendCodeLoginCode_WhenCodeLoginIsOff_RefusesBeforeSpendingTheCaptcha()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            SignIn = new TnziSignInOptions { AllowCodeLogin = false },
            Captcha = new CaptchaOptions { EnableCaptchaOnLogin = true },
        });

        var result = await _authService.SendCodeLoginCodeAsync(new SendCodeLoginCodeDto
        {
            Email = "someone@example.com",
            Type = TwoFactorType.Email,
            CaptchaId = "captcha-id",
            CaptchaCode = "1234",
        });

        Assert.False(result.Succeeded);
        _captchaServiceMock.Verify(
            x => x.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// ★★★ 密码到期的义务必须活过 2FA 那一跳。
    /// </summary>
    /// <remarks>
    /// 到期判定发生在密码校验里，而开着 2FA 的登录在那之后**提前返回挑战**；
    /// 用户带着临时令牌回来时，服务端从库里取回的是一个全新实体。义务位只置在内存上，
    /// 就随着第一个请求的实体一起消失了 —— 于是安全性最高的那批账号整体绕过密码到期策略，
    /// 且没有任何症状。本测试刻意把「重新取回的用户」建成**另一个对象**，
    /// 否则内存里的那次置位会顺着同一个引用溜进第二个请求，把测试变成假绿。
    /// </remarks>
    [Fact]
    public async Task PasswordExpiry_OnATwoFactorAccount_IsStillOwedAfterTheChallenge()
    {
        var userId = Guid.NewGuid();
        const string UserName = "expiring";
        const string Password = "Password123!";
        const string TempToken = "temp_token";

        // 「库」：第一个请求写进去的义务位，第二个请求必须还读得到。
        var storedActions = PendingUserActions.None;

        User NewEntity() => new()
        {
            Id = userId,
            UserName = UserName,
            Email = "expiring@example.com",
            EmailConfirmed = true,
            PasswordHash = "hash",
            PendingActions = storedActions,
        };

        var loginEntity = NewEntity();

        _userManagerMock.Setup(x => x.SupportsUserLockout).Returns(false);
        _captchaServiceMock.Setup(x => x.IsCaptchaRequiredAsync(It.IsAny<string>())).ReturnsAsync(false);
        _userManagerMock.Setup(x => x.FindByNameAsync(UserName)).ReturnsAsync(loginEntity);
        _signInManagerMock
            .Setup(x => x.CheckPasswordSignInAsync(loginEntity, Password, It.IsAny<bool>()))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
        _passwordPolicyServiceMock
            .Setup(x => x.CheckPasswordExpirationAsync(userId))
            .ReturnsAsync(new PasswordExpirationResult { IsExpired = true });
        _userManagerMock.Setup(x => x.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) =>
            {
                storedActions = u.PendingActions;
                return IdentityResult.Success;
            });

        // 账号开着 2FA：密码这一步之后立刻挑战。
        _userManagerMock.Setup(x => x.GetTwoFactorEnabledAsync(It.IsAny<User>())).ReturnsAsync(true);
        _twoFactorServiceMock.Setup(x => x.GetEnabledTwoFactorTypesAsync(It.IsAny<User>()))
            .ReturnsAsync([TwoFactorType.Totp]);
        _userManagerMock.Setup(x => x.GetRolesAsync(It.IsAny<User>())).ReturnsAsync([]);

        var login = await _authService.LoginAsync(new LoginDto { UserName = UserName, Password = Password });

        Assert.False(login.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_2FA_REQUIRED, login.ErrorCode);

        // 第二跳：临时令牌换令牌。用户是从库里**重新取回**的，不是上一段那个对象。
        _authTokenServiceMock
            .Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), TempToken))
            .ReturnsAsync(new AuthToken { Id = Guid.NewGuid(), UserId = userId, Value = TempToken });
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(NewEntity);
        _twoFactorServiceMock
            .Setup(x => x.VerifyCodeAsync(userId, It.IsAny<string>(), TwoFactorType.Totp, VerificationCodePurpose.TwoFactor))
            .ReturnsAsync(Result.Success());
        _authTokenServiceMock.Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>())).ReturnsAsync(true);

        var verified = await _authService.VerifyTwoFactorAndLoginAsync(new VerifyTwoFactorDto
        {
            TempToken = TempToken,
            Code = "123456",
            Type = TwoFactorType.Totp,
        });

        Assert.False(verified.Succeeded);
        Assert.Equal(403, verified.Code);
        Assert.Equal(ErrorCodes.IDENTITY_PENDING_ACTIONS_REQUIRED, verified.ErrorCode);

        _tokenServiceMock.Verify(
            x => x.GenerateToken(It.IsAny<User>(), It.IsAny<IList<string>>(), It.IsAny<IEnumerable<Claim>>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    private User OwingUser(PendingUserActions actions)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "owing",
            Email = "owing@example.com",
            PendingActions = actions,
        };

        _userManagerMock.Setup(x => x.SupportsUserLockout).Returns(true);
        _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(false);
        _userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync([]);
        return user;
    }

    #endregion

    #region 人机验证提供商层（2026-09-16）

    /// <summary>
    /// 找回密码的发码入口与验证码登录的发码入口同形：开着 <c>EnableCaptchaOnPasswordRecovery</c> 时无条件先过人机验证。
    /// 此前它不受任何验证码开关管辖，而每次调用都真的产生短信 / 邮件费用。
    /// </summary>
    [Fact]
    public async Task SendPasswordRecoveryCodeAsync_WhenRecoveryCaptchaEnabledAndMissing_RejectsBeforeSending()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Captcha = new CaptchaOptions { EnableCaptchaOnPasswordRecovery = true },
            Otp = new OtpOptions { EnableEmail = true },
            Recovery = new RecoveryOptions { EnablePasswordResetByEmail = true }
        });

        var result = await _authService.SendPasswordRecoveryCodeAsync(new SendPasswordRecoveryCodeDto
        {
            Email = "recover@example.com",
            Type = TwoFactorType.Email
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, result.ErrorCode);
        _userManagerMock.Verify(x => x.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        _twoFactorServiceMock.Verify(
            x => x.SendCodeByAddressAsync(It.IsAny<string>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    /// <summary>
    /// 对照组：过了验证码就照常走后面的流程，且用途是 <c>password-recovery</c>（注册页解出的令牌在这里必须验不过）。
    /// </summary>
    [Fact]
    public async Task SendPasswordRecoveryCodeAsync_WhenRecoveryCaptchaValid_ProceedsUnderThePasswordRecoveryPurpose()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Captcha = new CaptchaOptions { EnableCaptchaOnPasswordRecovery = true },
            Otp = new OtpOptions { EnableEmail = true },
            Recovery = new RecoveryOptions { EnablePasswordResetByEmail = true }
        });
        _captchaVerifierMock.Setup(x => x.VerifyAsync("widget-token", CaptchaPurpose.PasswordRecovery, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CaptchaVerification.Pass("turnstile"));
        _userManagerMock.Setup(x => x.FindByEmailAsync("recover@example.com")).ReturnsAsync((User?)null);

        var result = await _authService.SendPasswordRecoveryCodeAsync(new SendPasswordRecoveryCodeDto
        {
            Email = "recover@example.com",
            Type = TwoFactorType.Email,
            CaptchaToken = "widget-token"
        });

        // 账号不存在时统一回成功（枚举预言机那条规则），但验证器一定被问过、用的是找回密码的用途。
        Assert.True(result.Succeeded);
        _captchaVerifierMock.Verify(x => x.VerifyAsync("widget-token", CaptchaPurpose.PasswordRecovery, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 生效的不是内置图形验证码时，拒绝响应只带提供商名、不出图 —— 前端按 /auth/config 的客户端配置重置控件。
    /// </summary>
    [Fact]
    public async Task LoginAsync_WhenCaptchaRequired_AndProviderIsNotImage_ReturnsProviderNameWithoutAnImage()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Captcha = new CaptchaOptions { EnableCaptchaOnLogin = true },
        });
        _captchaServiceMock.Setup(x => x.IsCaptchaRequiredAsync(It.IsAny<string>())).ReturnsAsync(true);
        _captchaServiceMock.Setup(x => x.IsCacheAvailable).Returns(true);
        _captchaVerifierMock.SetupGet(x => x.ProviderName).Returns("turnstile");

        var result = await _authService.LoginAsync(new LoginDto { UserName = "someone", Password = "whatever" });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, result.ErrorCode);
        var challenge = Assert.IsType<CaptchaDto>(result.ErrorDetails);
        Assert.Equal("turnstile", challenge.Provider);
        Assert.Null(challenge.CaptchaId);
        Assert.Null(challenge.ImageBase64);
        _captchaServiceMock.Verify(x => x.GenerateAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// 登录页只请求一次 /auth/config：人机验证的客户端配置与新开关随它一起下发。
    /// </summary>
    [Fact]
    public void GetAuthConfig_CarriesTheCaptchaClientConfig_AndTheRecoverySwitch()
    {
        _identityOptionsMock.Setup(x => x.CurrentValue).Returns(new IdentityOptions
        {
            Captcha = new CaptchaOptions { EnableCaptchaOnPasswordRecovery = true },
        });
        _captchaVerifierMock.Setup(x => x.GetClientConfig()).Returns(new CaptchaClientConfigDto
        {
            Enabled = true, Provider = "turnstile", SiteKey = "site", ScriptUrl = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit"
        });

        var dto = _authService.GetAuthConfig().Data!;

        Assert.True(dto.EnableCaptchaOnPasswordRecovery);
        Assert.True(dto.Captcha.Enabled);
        Assert.Equal("turnstile", dto.Captcha.Provider);
        Assert.Equal("site", dto.Captcha.SiteKey);
    }

    #endregion
}
