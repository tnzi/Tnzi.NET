
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

public class TwoFactorServiceTests
{
    private readonly Mock<IRepository<TwoFactorCode, Guid>> _repositoryMock;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<IOptionsSnapshot<IdentityOptions>> _identityOptionsMock;
    private readonly Mock<ILogger<TwoFactorService>> _loggerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    private readonly TwoFactorService _twoFactorService;

    public TwoFactorServiceTests()
    {
        _repositoryMock = new Mock<IRepository<TwoFactorCode, Guid>>();

        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _eventBusMock = new Mock<IEventBus>();
        _identityOptionsMock = new Mock<IOptionsSnapshot<IdentityOptions>>();
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            Otp = new OtpOptions
            {
                EnableSms = true,
                EnableEmail = true,
                CodeLength = 6,
                ExpirationMinutes = 5,
                ResendIntervalSeconds = 60
            }
        });
        _loggerMock = new Mock<ILogger<TwoFactorService>>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _twoFactorService = new TwoFactorService(
            _repositoryMock.Object,
            _userManagerMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            _identityOptionsMock.Object
        );
    }

    [Fact]
    public async Task SendSmsCodeAsync_WhenSmsDisabled_ReturnsFalse()
    {
        // Arrange
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            Otp = new OtpOptions { EnableSms = false }
        });

        var service = new TwoFactorService(
            _repositoryMock.Object,
            _userManagerMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            _identityOptionsMock.Object
        );

        // Act
        var result = await service.SendSmsCodeAsync(Guid.NewGuid(), "13800138000");

        // Assert
        Assert.False(result.Succeeded);
    }

    #region GetTotpSetupInfoAsync

    [Fact]
    public async Task GetTotpSetupInfoAsync_WithValidUser_ReturnsSetupInfo()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        const string rawKey = "JBSWY3DPEHPK3PXP";

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user))
            .ReturnsAsync(rawKey);
        _userManagerMock.Setup(x => x.GetEmailAsync(user))
            .ReturnsAsync(user.Email);

        // Act
        var result = await _twoFactorService.GetTotpSetupInfoAsync(userId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data.SharedKey);
        Assert.Contains("otpauth://totp/", result.Data.AuthenticatorUri);
        Assert.Contains(rawKey, result.Data.AuthenticatorUri);

        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(user), Times.Once);
        _userManagerMock.Verify(x => x.GetAuthenticatorKeyAsync(user), Times.Once);
    }

    [Fact]
    public async Task GetTotpSetupInfoAsync_WithNonExistentUser_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _twoFactorService.GetTotpSetupInfoAsync(userId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task GetTotpSetupInfoAsync_WhenKeyGenerationFails_ReturnsInternalError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        // GetAuthenticatorKeyAsync returns null/empty to simulate generation failure
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _twoFactorService.GetTotpSetupInfoAsync(userId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(500, result.Code);
    }

    [Fact]
    public async Task GetTotpSetupInfoAsync_FormatsSharedKeyWithSpaces()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        // Key longer than 4 chars so FormatKey inserts spaces
        const string rawKey = "JBSWY3DPEHPK3PXP";

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user))
            .ReturnsAsync(rawKey);
        _userManagerMock.Setup(x => x.GetEmailAsync(user))
            .ReturnsAsync(user.Email);

        // Act
        var result = await _twoFactorService.GetTotpSetupInfoAsync(userId);

        // Assert
        Assert.True(result.Succeeded);
        // FormatKey inserts a space every 4 characters
        Assert.Contains(" ", result.Data!.SharedKey);
    }

    #endregion

    #region EnableTotpAsync

    [Fact]
    public async Task EnableTotpAsync_WithValidCode_EnablesTwoFactor()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };
        const string verificationCode = "123456";

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(
                user, It.IsAny<string>(), verificationCode))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.SetTwoFactorEnabledAsync(user, true))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _twoFactorService.EnableTotpAsync(userId, verificationCode);

        // Assert
        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.SetTwoFactorEnabledAsync(user, true), Times.Once);
    }

    [Fact]
    public async Task EnableTotpAsync_WithInvalidCode_ReturnsFail()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };
        const string verificationCode = "000000";

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(
                user, It.IsAny<string>(), verificationCode))
            .ReturnsAsync(false);

        // Act
        var result = await _twoFactorService.EnableTotpAsync(userId, verificationCode);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        // SetTwoFactorEnabledAsync must NOT be called when verification fails
        _userManagerMock.Verify(x => x.SetTwoFactorEnabledAsync(It.IsAny<User>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task EnableTotpAsync_WithNonExistentUser_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _twoFactorService.EnableTotpAsync(userId, "123456");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    #endregion

    #region DisableTotpAsync

    [Fact]
    public async Task DisableTotpAsync_WithValidUser_DisablesWhenNoOtherMethods()
    {
        // Arrange
        var userId = Guid.NewGuid();
        // User has neither a confirmed phone number nor a confirmed email
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            PhoneNumber = null,
            PhoneNumberConfirmed = false,
            Email = null,
            EmailConfirmed = false
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.SetTwoFactorEnabledAsync(user, false))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _twoFactorService.DisableTotpAsync(userId);

        // Assert
        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(user), Times.Once);
        _userManagerMock.Verify(x => x.SetTwoFactorEnabledAsync(user, false), Times.Once);
    }

    [Fact]
    public async Task DisableTotpAsync_WithOtherMethodsAvailable_KeepsTwoFactorEnabled()
    {
        // Arrange
        var userId = Guid.NewGuid();
        // User has a confirmed phone number, so another 2FA method exists
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            PhoneNumber = "13800138000",
            PhoneNumberConfirmed = true,
            Email = null,
            EmailConfirmed = false
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _twoFactorService.DisableTotpAsync(userId);

        // Assert
        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(user), Times.Once);
        // SetTwoFactorEnabledAsync(false) must NOT be called when another method is still available
        _userManagerMock.Verify(x => x.SetTwoFactorEnabledAsync(It.IsAny<User>(), false), Times.Never);
    }

    [Fact]
    public async Task DisableTotpAsync_WithConfirmedEmail_KeepsTwoFactorEnabled()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            PhoneNumber = null,
            PhoneNumberConfirmed = false,
            Email = "test@example.com",
            EmailConfirmed = true
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _twoFactorService.DisableTotpAsync(userId);

        // Assert
        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(user), Times.Once);
        _userManagerMock.Verify(x => x.SetTwoFactorEnabledAsync(It.IsAny<User>(), false), Times.Never);
    }

    [Fact]
    public async Task DisableTotpAsync_WithNonExistentUser_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _twoFactorService.DisableTotpAsync(userId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(It.IsAny<User>()), Times.Never);
    }

    #endregion
}
