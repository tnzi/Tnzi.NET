
namespace Tnzi.Identity.Tests;

public class OAuthServiceTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<SignInManager<User>> _signInManagerMock;
    private readonly Mock<ITokenService> _tokenServiceMock;
    private readonly Mock<IUserLoginService> _userLoginServiceMock;
    private readonly Mock<IAuthTokenService> _authTokenServiceMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IOptions<Tnzi.Identity.Options.IdentityOptions>> _identityOptionsMock;

    private readonly OAuthService _oauthService;

    public OAuthServiceTests()
    {
        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<User>>();
        var signInOptions = new Mock<IOptions<Microsoft.AspNetCore.Identity.IdentityOptions>>();
        var logger = new Mock<ILogger<SignInManager<User>>>();
        var schemes = new Mock<IAuthenticationSchemeProvider>();
        var confirmation = new Mock<IUserConfirmation<User>>();

        _signInManagerMock = new Mock<SignInManager<User>>(
            _userManagerMock.Object,
            contextAccessor.Object,
            claimsFactory.Object,
            signInOptions.Object,
            logger.Object,
            schemes.Object,
            confirmation.Object);

        _tokenServiceMock = new Mock<ITokenService>();
        _userLoginServiceMock = new Mock<IUserLoginService>();
        _authTokenServiceMock = new Mock<IAuthTokenService>();
        _eventBusMock = new Mock<IEventBus>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        // 创建 IdentityOptions mock
        _identityOptionsMock = new Mock<IOptions<Tnzi.Identity.Options.IdentityOptions>>();
        var identityOptions = new Tnzi.Identity.Options.IdentityOptions
        {
            Jwt = new Tnzi.Identity.Options.JwtOptions
            {
                RefreshTokenExpirationDays = 7
            }
        };
        _identityOptionsMock.Setup(x => x.Value).Returns(identityOptions);

        _oauthService = new OAuthService(
            _userManagerMock.Object,
            _signInManagerMock.Object,
            _tokenServiceMock.Object,
            _userLoginServiceMock.Object,
            _serviceProviderMock.Object,
            _identityOptionsMock.Object,
            _authTokenServiceMock.Object,
            _eventBusMock.Object
        );
    }

    [Fact]
    public async Task HandleOAuthCallbackAsync_WithExistingUser_ReturnsTokenResult()
    {
        // Arrange
        var provider = "Google";
        var providerKey = "google_user_id";
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };
        var tokenResult = new TokenResult
        {
            AccessToken = "access_token",
            RefreshToken = "refresh_token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, providerKey),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim(ClaimTypes.Name, "Test User")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, providerKey))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        _tokenServiceMock.Setup(x => x.GenerateTokenResult(user, It.IsAny<IList<string>>()))
            .Returns(tokenResult);

        _authTokenServiceMock.Setup(x => x.SaveTokenAsync(userId, "JWT", "RefreshToken", It.IsAny<string>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(Guid.NewGuid());

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _oauthService.HandleOAuthCallbackAsync(provider, principal);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data!.AccessToken);
    }

    [Fact]
    public async Task HandleOAuthCallbackAsync_WithNewUser_CreatesAccountAndReturnsToken()
    {
        // Arrange
        var provider = "Google";
        var providerKey = "new_google_user_id";
        var userId = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, providerKey),
            new Claim(ClaimTypes.Email, "newuser@example.com"),
            new Claim(ClaimTypes.Name, "New User")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, providerKey))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.FindByEmailAsync("newuser@example.com"))
            .ReturnsAsync((User?)null);

        // UserNameGenerator 会调用 FindByNameAsync 来检查用户名唯一性
        _userManagerMock.Setup(x => x.FindByNameAsync(It.IsAny<string>()))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) =>
            {
                u.Id = userId;
                return IdentityResult.Success;
            });

        _userManagerMock.Setup(x => x.AddLoginAsync(It.IsAny<User>(), It.IsAny<Microsoft.AspNetCore.Identity.UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.GetRolesAsync(It.IsAny<User>()))
            .ReturnsAsync(new List<string> { "User" });

        var tokenResult = new TokenResult
        {
            AccessToken = "access_token",
            RefreshToken = "refresh_token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
        _tokenServiceMock.Setup(x => x.GenerateTokenResult(It.IsAny<User>(), It.IsAny<IList<string>>()))
            .Returns(tokenResult);

        _authTokenServiceMock.Setup(x => x.SaveTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _oauthService.HandleOAuthCallbackAsync(provider, principal);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data.AccessToken);
    }

    [Fact]
    public async Task LinkOAuthAccountAsync_WithValidInput_LinksAccount()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var provider = "Google";
        var providerKey = "google_user_id";
        var displayName = "Test User";
        var user = new User { Id = userId, UserName = "testuser" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.AddLoginAsync(user, It.IsAny<Microsoft.AspNetCore.Identity.UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);

        _userLoginServiceMock.Setup(x => x.RecordLoginAsync(userId, provider, providerKey, displayName))
            .Returns(Task.CompletedTask);

        // Act
        await _oauthService.LinkOAuthAccountAsync(userId, provider, providerKey, displayName);

        // Assert
        _userManagerMock.Verify(x => x.AddLoginAsync(user, It.IsAny<Microsoft.AspNetCore.Identity.UserLoginInfo>()), Times.Once);
        _userLoginServiceMock.Verify(x => x.RecordLoginAsync(userId, provider, providerKey, displayName), Times.Once);
    }

    [Fact]
    public async Task UnlinkOAuthAccountAsync_WithValidInput_UnlinksAccount()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var provider = "Google";
        var providerKey = "google_user_id";
        var user = new User { Id = userId, UserName = "testuser" };

        var loginInfo = new Microsoft.AspNetCore.Identity.UserLoginInfo(provider, providerKey, provider);
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetLoginsAsync(user))
            .ReturnsAsync(new List<Microsoft.AspNetCore.Identity.UserLoginInfo> { loginInfo });

        _userManagerMock.Setup(x => x.RemoveLoginAsync(user, provider, providerKey))
            .ReturnsAsync(IdentityResult.Success);

        _userLoginServiceMock.Setup(x => x.RemoveLoginAsync(userId, provider, providerKey))
            .Returns(Task.CompletedTask);

        // Act
        await _oauthService.UnlinkOAuthAccountAsync(userId, provider);

        // Assert
        _userManagerMock.Verify(x => x.RemoveLoginAsync(user, provider, providerKey), Times.Once);
        _userLoginServiceMock.Verify(x => x.RemoveLoginAsync(userId, provider, providerKey), Times.Once);
    }
}