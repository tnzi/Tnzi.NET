
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

public class PasswordServiceTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IOptionsSnapshot<IdentityOptions>> _identityOptionsMock;
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

        _identityOptionsMock = new Mock<IOptionsSnapshot<IdentityOptions>>();
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

    /// <summary>
    /// ★★★ 还没接受邀请的账号不能走找回密码。
    /// </summary>
    /// <remarks>
    /// 这条路径<b>不过登录守卫</b>（它不签发令牌），所以 <c>PendingActionsLoginGuard</c>
    /// 管不到它。不挡在这里，任何知道这个邮箱的人都能替一个还没入职的账号设上密码。
    /// ★ 回「若邮箱存在则已发送」而不是报错：端点匿名可达，区分开就成了账号状态的试探入口。
    /// </remarks>
    [Fact]
    public async Task ForgotPasswordAsync_OnAnInvitedAccount_SuppressesTheResetSilently()
    {
        // Arrange
        var email = "newhire@example.com";
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "newhire",
            Email = email,
            PendingActions = PendingUserActions.InvitationPending
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(email))
            .ReturnsAsync(user);

        // Act
        var result = await _passwordService.ForgotPasswordAsync(email);

        // Assert：对外与「邮箱不存在」同形，对内什么都不做
        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.GeneratePasswordResetTokenAsync(It.IsAny<User>()), Times.Never);
        _eventBusMock.Verify(
            x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.PasswordResetRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
    /// <summary>
    /// ★★★ 共享出口拒绝设回最近用过的密码，并把新密码写进历史。
    /// </summary>
    /// <remarks>
    /// 这两步是「第六条改密路径」（<c>RegistrationService.SetPasswordAsync</c>）此前漏掉的部分：
    /// 不查重，设回上一个密码会被接受；不写历史，下一次查重也查不到 —— 缺陷自我延续。
    /// </remarks>
    [Fact]
    public async Task ForceSetPasswordAsync_RejectsAPasswordAlreadyInHistory()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "u", PasswordHash = "old-hash" };

        _passwordPolicyServiceMock.Setup(x => x.ValidatePasswordStrength(It.IsAny<string>())).Returns((string?)null);
        _passwordPolicyServiceMock.Setup(x => x.CheckPasswordHistoryAsync(user.Id, "Recycled123!"))
            .ReturnsAsync(true);

        var result = await _passwordService.ForceSetPasswordAsync(user, "Recycled123!");

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        _userManagerMock.Verify(
            x => x.ResetPasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ForceSetPasswordAsync_RecordsTheNewPasswordInHistory()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "u", PasswordHash = "new-hash" };

        _passwordPolicyServiceMock.Setup(x => x.ValidatePasswordStrength(It.IsAny<string>())).Returns((string?)null);
        _passwordPolicyServiceMock.Setup(x => x.CheckPasswordHistoryAsync(user.Id, It.IsAny<string>()))
            .ReturnsAsync(false);
        _userManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("reset-token");
        _userManagerMock.Setup(x => x.ResetPasswordAsync(user, "reset-token", "Fresh123!"))
            .ReturnsAsync(IdentityResult.Success);

        var result = await _passwordService.ForceSetPasswordAsync(user, "Fresh123!");

        Assert.True(result.Succeeded);
        _passwordPolicyServiceMock.Verify(
            x => x.SavePasswordHistoryAsync(user.Id, "new-hash"), Times.Once);
    }
}
