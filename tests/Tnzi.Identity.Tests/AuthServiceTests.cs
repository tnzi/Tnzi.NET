
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
            _twoFactorServiceMock.Object
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

        _authTokenServiceMock.Setup(x => x.MarkTokenAsUsedAsync(tokenEntry.Id))
            .ReturnsAsync(true);

        _authTokenServiceMock.Setup(x => x.SaveTokenAsync(userId, "JWT", "RefreshToken", It.IsAny<string>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(Guid.NewGuid());

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
        _authTokenServiceMock.Setup(x => x.MarkTokenAsUsedAsync(tokenEntry.Id)).ReturnsAsync(true);
        // The rotated access token + refresh token stay bound to the same session.
        _tokenServiceMock.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>(), null, sessionId))
            .Returns("rotated_access");
        _tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("rotated_refresh");
        _authTokenServiceMock.Setup(x => x.SaveTokenAsync(userId, "JWT", "RefreshToken", It.IsAny<string>(), It.IsAny<DateTime?>(), sessionId))
            .ReturnsAsync(Guid.NewGuid());

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

        _twoFactorServiceMock.Setup(x => x.SendEmailCodeAsync(userId, user.Email!))
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

        _twoFactorServiceMock.Setup(x => x.VerifyCodeAsync(userId, code, TwoFactorType.Email))
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

        _twoFactorServiceMock.Setup(x => x.VerifyCodeAsync(userId, code, TwoFactorType.Email))
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
}