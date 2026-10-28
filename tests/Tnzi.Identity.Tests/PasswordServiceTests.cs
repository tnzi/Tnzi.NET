
using Tnzi.Security.Authorization;
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

    /// <summary>
    /// ★★ 重置链接寄往<b>账号上登记的邮箱</b>，不是请求里的写法：账号按规范化比较解析，
    /// 一个不同的字符串（长 s 大写成 S，或不区分全角半角的排序规则）也会解析到这个账号，而链接能改它的密码。
    /// </summary>
    [Fact]
    public async Task ForgotPasswordAsync_SendsTheLinkToTheAddressOnRecord()
    {
        var folded = (char)0x017F + "am@example.com";
        var owner = new User { Id = Guid.NewGuid(), UserName = "sam", Email = "sam@example.com" };
        _userManagerMock.Setup(x => x.FindByEmailAsync(folded)).ReturnsAsync(owner);
        _userManagerMock.Setup(x => x.GeneratePasswordResetTokenAsync(owner)).ReturnsAsync("reset_token");

        var result = await _passwordService.ForgotPasswordAsync(folded);

        Assert.True(result.Succeeded);
        _eventBusMock.Verify(
            x => x.PublishAsync(It.Is<Tnzi.Identity.Events.PasswordResetRequestedEvent>(e => e.Email == owner.Email), It.IsAny<CancellationToken>()),
            Times.Once);
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

    /// <summary>
    /// ★ 关掉邮件找回之后，已经发出、还在有效期内的重置链接也不能再改密码。
    /// 只挡发链接的那一头，开关关掉之前寄出的链接照样能用（与验证码找回两头都挡同一理由）。
    /// </summary>
    [Fact]
    public async Task ResetPasswordByTokenAsync_WhenEmailResetDisabled_IsRefusedBeforeTheTokenIsChecked()
    {
        var email = "test@example.com";
        var user = new User { Id = Guid.NewGuid(), UserName = "testuser", Email = email };
        _userManagerMock.Setup(x => x.FindByEmailAsync(email)).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.ResetPasswordAsync(user, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            Recovery = new RecoveryOptions { EnablePasswordResetByEmail = false }
        });
        var service = new PasswordService(
            _userManagerMock.Object,
            _identityOptionsMock.Object,
            _serviceProviderMock.Object,
            _eventBusMock.Object,
            _configurationMock.Object,
            _passwordPolicyServiceMock.Object,
            _currentUserMock.Object);

        var result = await service.ResetPasswordByTokenAsync(
            email, WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("issued_before_the_switch")), "NewPassword123!");

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        _userManagerMock.Verify(x => x.ResetPasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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

    /// <summary>
    /// ★★ 「当前密码」校验就是一次密码猜测：猜错要记进与登录同一套的失败计数。
    /// </summary>
    /// <remarks>
    /// <c>UserManager.ChangePasswordAsync</c> 答 PasswordMismatch 时既不动 <c>AccessFailedCount</c> 也不触发锁定
    /// （那套记账在 <c>SignInManager</c> 里），于是这个端点此前是一台不限次的猜密码机：
    /// 管理端变体对任何账号可用，自助变体对一枚被盗的会话可用。
    /// </remarks>
    [Fact]
    public async Task ChangePasswordAsync_WithAWrongCurrentPassword_FailsAndCountsTheAttempt()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "testuser" };
        _userManagerMock.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.SupportsUserLockout).Returns(true);
        _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(false);
        _userManagerMock.Setup(x => x.ChangePasswordAsync(user, "WrongPassword", "NewPassword123!"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityErrorDescriber().PasswordMismatch()));
        _userManagerMock.Setup(x => x.AccessFailedAsync(user)).ReturnsAsync(IdentityResult.Success);

        var result = await _passwordService.ChangePasswordAsync(user.Id, "WrongPassword", "NewPassword123!");

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        _userManagerMock.Verify(x => x.AccessFailedAsync(user), Times.Once);
    }

    /// <summary>
    /// 锁定之后不再回答「当前密码对不对」：只计数不拦截的话，锁定只挡住了登录页，猜测照样在这里继续。
    /// </summary>
    [Fact]
    public async Task ChangePasswordAsync_WhileTheAccountIsLockedOut_RefusesWithoutCheckingThePassword()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "testuser" };
        _userManagerMock.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.SupportsUserLockout).Returns(true);
        _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(true);

        var result = await _passwordService.ChangePasswordAsync(user.Id, "Guess123!", "NewPassword123!");

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_LOCKED, result.ErrorCode);
        _userManagerMock.Verify(x => x.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// ★★★ 管理端「修改密码」对超管是一个猜中即接管的入口：猜中当前密码的同时就把它换成了调用者选的值。
    /// 同一权限码下的重置密码早就挂着超管护栏，这里要同样拦下，而且拦在校验密码之前（否则拒绝本身就泄露了答案）。
    /// </summary>
    [Fact]
    public async Task ChangePasswordAsync_OnASuperAdmin_ByANonSuperAdmin_IsForbiddenBeforeThePasswordIsChecked()
    {
        var actorId = Guid.NewGuid();
        var target = new User { Id = Guid.NewGuid(), UserName = "root" };
        _currentUserMock.SetupGet(c => c.Id).Returns(actorId);
        _userManagerMock.Setup(x => x.FindByIdAsync(target.Id.ToString())).ReturnsAsync(target);
        var functionAuthorization = new Mock<IFunctionAuthorizationService>();
        functionAuthorization.Setup(a => a.IsSuperAdminAsync(target.Id)).ReturnsAsync(true);
        functionAuthorization.Setup(a => a.IsSuperAdminAsync(actorId)).ReturnsAsync(false);
        var service = new PasswordService(
            _userManagerMock.Object, _identityOptionsMock.Object, _serviceProviderMock.Object,
            _eventBusMock.Object, _configurationMock.Object, _passwordPolicyServiceMock.Object, _currentUserMock.Object,
            functionAuthorization: functionAuthorization.Object);

        var result = await service.ChangePasswordAsync(target.Id, "Guess123!", "NewPassword123!");

        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.FORBIDDEN, result.ErrorCode);
        _userManagerMock.Verify(x => x.ChangePasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _userManagerMock.Verify(x => x.CheckPasswordAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>对照：超管改自己的密码（自助路径走同一个方法）不受护栏影响。</summary>
    [Fact]
    public async Task ChangePasswordAsync_ForASuperAdminsOwnAccount_IsAllowed()
    {
        var target = new User { Id = Guid.NewGuid(), UserName = "root" };
        _currentUserMock.SetupGet(c => c.Id).Returns(target.Id);
        _userManagerMock.Setup(x => x.FindByIdAsync(target.Id.ToString())).ReturnsAsync(target);
        _userManagerMock.Setup(x => x.ChangePasswordAsync(target, "Current123!", "NewPassword123!")).ReturnsAsync(IdentityResult.Success);
        var functionAuthorization = new Mock<IFunctionAuthorizationService>();
        functionAuthorization.Setup(a => a.IsSuperAdminAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        var service = new PasswordService(
            _userManagerMock.Object, _identityOptionsMock.Object, _serviceProviderMock.Object,
            _eventBusMock.Object, _configurationMock.Object, _passwordPolicyServiceMock.Object, _currentUserMock.Object,
            functionAuthorization: functionAuthorization.Object);

        var result = await service.ChangePasswordAsync(target.Id, "Current123!", "NewPassword123!");

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// 持 <c>user.update</c> 的普通管理员重置超管的密码，就是借一个比超管小的授权拿下超管账号。
    /// </summary>
    [Fact]
    public async Task ResetPasswordByAdminAsync_OnASuperAdmin_ByANonSuperAdmin_IsForbidden()
    {
        var actorId = Guid.NewGuid();
        var target = new User { Id = Guid.NewGuid(), UserName = "root" };
        _currentUserMock.SetupGet(c => c.Id).Returns(actorId);
        _userManagerMock.Setup(x => x.FindByIdAsync(target.Id.ToString())).ReturnsAsync(target);
        var functionAuthorization = new Mock<IFunctionAuthorizationService>();
        functionAuthorization.Setup(a => a.IsSuperAdminAsync(target.Id)).ReturnsAsync(true);
        functionAuthorization.Setup(a => a.IsSuperAdminAsync(actorId)).ReturnsAsync(false);
        var service = new PasswordService(
            _userManagerMock.Object, _identityOptionsMock.Object, _serviceProviderMock.Object,
            _eventBusMock.Object, _configurationMock.Object, _passwordPolicyServiceMock.Object, _currentUserMock.Object,
            functionAuthorization: functionAuthorization.Object);

        var result = await service.ResetPasswordByAdminAsync(target.Id, "NewPassword123!");

        Assert.Equal(403, result.Code);
        _userManagerMock.Verify(x => x.ResetPasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
