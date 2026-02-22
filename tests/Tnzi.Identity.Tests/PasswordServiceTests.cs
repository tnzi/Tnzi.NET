
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

public class PasswordServiceTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IOptions<IdentityOptions>> _identityOptionsMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<IPasswordPolicyService> _passwordPolicyServiceMock;
    private readonly Mock<ICurrentUser> _currentUserMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    private readonly PasswordService _passwordService;

    public PasswordServiceTests()
    {
        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _identityOptionsMock = new Mock<IOptions<IdentityOptions>>();
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            Recovery = new RecoveryOptions
            {
                EnablePasswordResetByEmail = true
            }
        });

        _eventBusMock = new Mock<IEventBus>();
        _configurationMock = new Mock<IConfiguration>();
        _passwordPolicyServiceMock = new Mock<IPasswordPolicyService>();
        _currentUserMock = new Mock<ICurrentUser>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _passwordService = new PasswordService(
            _userManagerMock.Object,
            _identityOptionsMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            _configurationMock.Object,
            _passwordPolicyServiceMock.Object,
            _currentUserMock.Object
        );
    }

    [Fact]
    public async Task ForgotPasswordAsync_WithValidEmail_ReturnsSuccess()
    {
        // Arrange
        var email = "test@example.com";
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Email = email
        };
        var token = "reset_token";

        _userManagerMock.Setup(x => x.FindByEmailAsync(email))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(user))
            .ReturnsAsync(token);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.PasswordResetRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _passwordService.ForgotPasswordAsync(email);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ForgotPasswordAsync_WithNonExistentEmail_ReturnsSuccessForSecurity()
    {
        // Arrange
        var email = "nonexistent@example.com";

        _userManagerMock.Setup(x => x.FindByEmailAsync(email))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _passwordService.ForgotPasswordAsync(email);

        // Assert
        Assert.True(result.Succeeded); // 为了安全，即使邮箱不存在也返回成功
    }

    [Fact]
    public async Task ForgotPasswordAsync_WhenEmailResetDisabled_ReturnsFailure()
    {
        // Arrange
        var email = "test@example.com";
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            Recovery = new RecoveryOptions
            {
                EnablePasswordResetByEmail = false
            }
        });

        var service = new PasswordService(
            _userManagerMock.Object,
            _identityOptionsMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            _configurationMock.Object,
            _passwordPolicyServiceMock.Object,
            _currentUserMock.Object
        );

        // Act
        var result = await service.ForgotPasswordAsync(email);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ResetPasswordByTokenAsync_WithValidToken_ReturnsSuccess()
    {
        // Arrange
        var email = "test@example.com";
        var rawToken = "valid_token";
        // ResetPasswordByTokenAsync 现在期望 Base64Url 编码的 token
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));
        var newPassword = "NewPassword123!";
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Email = email
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(email))
            .ReturnsAsync(user);

        _passwordPolicyServiceMock.Setup(x => x.ValidatePasswordStrength(newPassword))
            .Returns((string?)null);

        _passwordPolicyServiceMock.Setup(x => x.CheckPasswordHistoryAsync(user.Id, newPassword))
            .ReturnsAsync(false);

        // 解码后会得到 rawToken
        _userManagerMock.Setup(x => x.ResetPasswordAsync(user, rawToken, newPassword))
            .ReturnsAsync(IdentityResult.Success);

        _passwordPolicyServiceMock.Setup(x => x.SavePasswordHistoryAsync(user.Id, It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserPasswordChangedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _passwordService.ResetPasswordByTokenAsync(email, encodedToken, newPassword);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ResetPasswordByTokenAsync_WithInvalidToken_ReturnsFailure()
    {
        // Arrange
        var email = "test@example.com";
        var token = "invalid_token";
        var newPassword = "NewPassword123!";
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Email = email
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(email))
            .ReturnsAsync(user);

        _passwordPolicyServiceMock.Setup(x => x.ValidatePasswordStrength(newPassword))
            .Returns((string?)null);

        _passwordPolicyServiceMock.Setup(x => x.CheckPasswordHistoryAsync(user.Id, newPassword))
            .ReturnsAsync(false);

        _userManagerMock.Setup(x => x.ResetPasswordAsync(user, token, newPassword))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Invalid token" }));

        // Act
        var result = await _passwordService.ResetPasswordByTokenAsync(email, token, newPassword);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithValidCurrentPassword_ChangesPassword()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var currentPassword = "OldPassword123!";
        var newPassword = "NewPassword123!";
        var user = new User
        {
            Id = userId,
            UserName = "testuser"
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, currentPassword))
            .ReturnsAsync(true);

        _passwordPolicyServiceMock.Setup(x => x.ValidatePasswordStrength(newPassword))
            .Returns((string?)null);

        _passwordPolicyServiceMock.Setup(x => x.CheckPasswordHistoryAsync(userId, newPassword))
            .ReturnsAsync(false);

        _userManagerMock.Setup(x => x.ChangePasswordAsync(user, currentPassword, newPassword))
            .ReturnsAsync(IdentityResult.Success);

        _passwordPolicyServiceMock.Setup(x => x.SavePasswordHistoryAsync(userId, It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserPasswordChangedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _passwordService.ChangePasswordAsync(userId, currentPassword, newPassword);

        // Assert
        _userManagerMock.Verify(x => x.ChangePasswordAsync(user, currentPassword, newPassword), Times.Once);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithInvalidCurrentPassword_ThrowsException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var currentPassword = "WrongPassword";
        var newPassword = "NewPassword123!";
        var user = new User
        {
            Id = userId,
            UserName = "testuser"
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, currentPassword))
            .ReturnsAsync(false);

        // Act & Assert
        // ChangePasswordAsync 在密码错误时会抛出异常，但具体异常类型可能不同
        await Assert.ThrowsAnyAsync<Exception>(
            () => _passwordService.ChangePasswordAsync(userId, currentPassword, newPassword));
    }

    [Fact]
    public async Task ResetPasswordByAdminAsync_WithValidInput_ResetsPassword()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var newPassword = "NewPassword123!";
        var user = new User
        {
            Id = userId,
            UserName = "testuser"
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _passwordPolicyServiceMock.Setup(x => x.ValidatePasswordStrength(newPassword))
            .Returns((string?)null);

        _passwordPolicyServiceMock.Setup(x => x.CheckPasswordHistoryAsync(userId, newPassword))
            .ReturnsAsync(false);

        _userManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(user))
            .ReturnsAsync("reset_token");

        _userManagerMock.Setup(x => x.ResetPasswordAsync(user, It.IsAny<string>(), newPassword))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.SetupSequence(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user)
            .ReturnsAsync(new User { Id = userId, PasswordHash = "hashed_password" });

        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        _passwordPolicyServiceMock.Setup(x => x.SavePasswordHistoryAsync(userId, It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserPasswordResetEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _passwordService.ResetPasswordByAdminAsync(userId, newPassword);

        // Assert
        _userManagerMock.Verify(x => x.ResetPasswordAsync(user, It.IsAny<string>(), newPassword), Times.Once);
    }
}