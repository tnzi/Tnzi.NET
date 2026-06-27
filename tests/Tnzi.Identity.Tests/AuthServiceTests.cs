
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
        // Arrange — specific switches + two OAuth providers with full creds, one empty.
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

        // Assert — each flag maps from the right option.
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

        // Only providers with BOTH ClientId + ClientSecret are listed — no secrets leak.
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

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
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