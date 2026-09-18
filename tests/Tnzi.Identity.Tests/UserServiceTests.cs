
namespace Tnzi.Identity.Tests;

public partial class UserServiceTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<RoleManager<Role>> _roleManagerMock;
    private readonly Mock<IRepository<User, Guid>> _userRepositoryMock;
    private readonly Mock<IOrganizationService> _organizationServiceMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<ICurrentUser> _currentUserMock;
    private readonly Mock<Tnzi.Caching.ICache> _cacheMock;
    private readonly Mock<ISessionRevocationService> _sessionRevocationMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    private readonly UserService _userService;

    public UserServiceTests()
    {
        // 配置 Mapster 映射
        var config = new TypeAdapterConfig();
        config.NewConfig<User, UserDto>()
            .Map(dest => dest.IsEmailConfirmed, src => src.EmailConfirmed)
            .Map(dest => dest.IsPhoneNumberConfirmed, src => src.PhoneNumberConfirmed)
            .Map(dest => dest.IsLockedOut, src => src.LockoutEnd.HasValue && src.LockoutEnd.Value > DateTimeOffset.UtcNow)
            .Ignore(dest => dest.Roles); // Roles 在 MapUserToDtoAsync 中单独设置

        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var roleStore = new Mock<IRoleStore<Role>>();
        _roleManagerMock = new Mock<RoleManager<Role>>(roleStore.Object, null!, null!, null!, null!);

        _userRepositoryMock = new Mock<IRepository<User, Guid>>();
        _organizationServiceMock = new Mock<IOrganizationService>();
        _eventBusMock = new Mock<IEventBus>();
        _currentUserMock = new Mock<ICurrentUser>();
        _cacheMock = new Mock<Tnzi.Caching.ICache>();
        _sessionRevocationMock = new Mock<ISessionRevocationService>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        // ApplicationService.EventBus 是从 ServiceProvider 懒解析的，不走构造参数；
        // 不接上这一条，「失败时不得发事件」的断言就是空的。
        _serviceProviderMock.Setup(x => x.GetService(typeof(IEventBus))).Returns(_eventBusMock.Object);

        _userService = new UserService(
            _userManagerMock.Object,
            _roleManagerMock.Object,
            _userRepositoryMock.Object,
            _serviceProviderMock.Object,
            _organizationServiceMock.Object,
            _eventBusMock.Object,
            _currentUserMock.Object,
            _cacheMock.Object,
            sessionRevocation: _sessionRevocationMock.Object
        );
    }

    [Fact]
    public async Task CreateAsync_WithValidInput_ReturnsUserDto()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var input = new CreateUserDto
        {
            UserName = "testuser",
            Email = "test@example.com",
            Password = "Password123!"
        };
        var user = new User
        {
            Id = userId,
            UserName = input.UserName,
            Email = input.Email
        };

        var createdUser = new User
        {
            Id = userId,
            UserName = input.UserName,
            Email = input.Email
        };

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>(), input.Password))
            .ReturnsAsync((User u, string p) =>
            {
                u.Id = userId; // 设置用户ID
                return IdentityResult.Success;
            });

        _userManagerMock.Setup(x => x.GetRolesAsync(It.Is<User>(u => u.Id == userId)))
            .ReturnsAsync(new List<string>());

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserRegisteredEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _userService.CreateAsync(input);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(input.UserName, result.Data.UserName);
        Assert.Equal(input.Email, result.Data.Email);
    }

    [Fact]
    public async Task UpdateAsync_WithValidInput_ReturnsUpdatedUserDto()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            Email = "old@example.com"
        };
        var input = new UpdateUserDto
        {
            Email = "new@example.com"
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        // ★ 邮箱现在经 SetEmailAsync 写入（而不是直接赋值）：那是 UserManager 里唯一
        //   会顺带把 EmailConfirmed 清掉、并跑一遍唯一性校验的入口。
        _userManagerMock.Setup(x => x.SetEmailAsync(user, input.Email))
            .ReturnsAsync((User u, string e) => { u.Email = e; u.EmailConfirmed = false; return IdentityResult.Success; });

        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string>());

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _userService.UpdateAsync(userId, input);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(input.Email, result.Data.Email);
        // Nickname 存储在 UserDetail 中，不在 User 实体上
    }

    /// <summary>
    /// ★★★ 改地址必须清掉确认位。此前是 <c>user.Email = input.Email</c> 直接赋值，
    /// 于是换完地址仍带着「已验证」的章 —— 而框架下游拿那一位当作
    /// 「这个地址属于这个人」的断言（找回密码、邮箱 2FA、第三方按邮箱认领账号）。
    /// 断言走的是 SetEmailAsync 而不是断言 EmailConfirmed 的值：后者在 mock 上
    /// 由测试自己设定，证明不了生产代码走的是哪条路。
    /// </summary>
    [Fact]
    public async Task UpdateAsync_WhenEmailChanges_GoesThroughSetEmailAsync()
    {
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            Email = "old@example.com",
            EmailConfirmed = true
        };
        var input = new UpdateUserDto { Email = "new@example.com" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.SetEmailAsync(user, input.Email))
            .ReturnsAsync((User u, string e) => { u.Email = e; u.EmailConfirmed = false; return IdentityResult.Success; });
        _userManagerMock.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string>());

        var result = await _userService.UpdateAsync(userId, input);

        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.SetEmailAsync(user, "new@example.com"), Times.Once);
        Assert.False(user.EmailConfirmed);
    }

    /// <summary>
    /// 对照组：地址没变就不该动它 —— 否则每次保存资料都会把确认位清掉一次，
    /// 用户会莫名其妙被反复要求验证邮箱。
    /// </summary>
    [Fact]
    public async Task UpdateAsync_WhenEmailUnchanged_DoesNotTouchIt()
    {
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            Email = "same@example.com",
            EmailConfirmed = true
        };
        var input = new UpdateUserDto { Email = "same@example.com" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string>());

        var result = await _userService.UpdateAsync(userId, input);

        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.SetEmailAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
        Assert.True(user.EmailConfirmed);
    }

    /// <summary>
    /// ★ 详情写入的结果此前被整个丢掉：<c>await _userDetailService.CreateOrUpdateAsync(...)</c> 不看返回值。
    /// 于是详情那一侧的任何拒绝（头像文件归属探针的 403 / 存储模块缺席的 501）都变成「资料已更新」的 200，
    /// 而头像其实没写进去 —— 与「校验通过」在接口上完全一致。自助 <c>PUT users/profile</c> 走的就是这条路。
    /// </summary>
    [Fact]
    public async Task UpdateAsync_WhenTheDetailWriteIsRefused_PropagatesTheRefusal()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser", Email = "same@example.com" };
        var input = new UpdateUserDto { Nickname = "n", AvatarId = Guid.NewGuid() };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string>());
        var detailService = new Mock<IUserDetailService>();
        detailService.Setup(d => d.CreateOrUpdateAsync(userId, It.IsAny<CreateUserDetailDto>()))
            .ReturnsAsync(Result.Failure<UserDetailDto>("That file cannot be used as an avatar.", 403));
        var service = new UserService(
            _userManagerMock.Object,
            _roleManagerMock.Object,
            _userRepositoryMock.Object,
            _serviceProviderMock.Object,
            _organizationServiceMock.Object,
            _eventBusMock.Object,
            _currentUserMock.Object,
            _cacheMock.Object,
            userDetailService: detailService.Object,
            sessionRevocation: _sessionRevocationMock.Object);

        var result = await service.UpdateAsync(userId, input);

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal("That file cannot be used as an avatar.", result.Message);
    }

    [Fact]
    public async Task UpdateAsync_WithUserNotFound_ReturnsFailResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var input = new UpdateUserDto { Email = "new@example.com" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userService.UpdateAsync(userId, input);

        // Assert - 服务返回 Fail 而非抛异常
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task DeleteAsync_WithValidUserId_DeletesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.DeleteAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        await _userService.DeleteAsync(userId);

        // Assert
        _userManagerMock.Verify(x => x.DeleteAsync(user), Times.Once);
        _sessionRevocationMock.Verify(
            x => x.RevokeUserSessionsAsync(userId, SessionRevocationReason.AccountDeleted, null),
            Times.Once);
    }

    /// <summary>
    /// ★★ 批量删除也必须撤销会话，逐个删除做的每一步这里都要做。
    /// </summary>
    /// <remarks>
    /// access token 的每请求校验只看会话（<c>OnTokenValidated</c> 不查用户还在不在），
    /// 所以漏掉这一步的后果是：批量删掉的账号在令牌剩余寿命里照常通过认证，
    /// 而管理界面显示的是「已删除」。单个删除一直是对的，只有批量这条路漏了。
    /// </remarks>
    [Fact]
    public async Task DeleteManyAsync_RevokesEveryDeletedAccountsSessions()
    {
        var first = new User { Id = Guid.NewGuid(), UserName = "first" };
        var second = new User { Id = Guid.NewGuid(), UserName = "second" };
        var users = new List<User> { first, second };

        var queryable = users.BuildMock();
        _userRepositoryMock.As<IQueryable<User>>().Setup(q => q.Provider).Returns(queryable.Provider);
        _userRepositoryMock.As<IQueryable<User>>().Setup(q => q.Expression).Returns(queryable.Expression);
        _userRepositoryMock.As<IQueryable<User>>().Setup(q => q.ElementType).Returns(queryable.ElementType);
        _userRepositoryMock.As<IQueryable<User>>().Setup(q => q.GetEnumerator()).Returns(() => queryable.GetEnumerator());

        _userManagerMock.Setup(x => x.GetRolesAsync(It.IsAny<User>())).ReturnsAsync([]);
        _userManagerMock.Setup(x => x.DeleteAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);

        var result = await _userService.DeleteManyAsync([first.Id, second.Id]);

        Assert.True(result.Succeeded);
        _sessionRevocationMock.Verify(
            x => x.RevokeUserSessionsAsync(first.Id, SessionRevocationReason.AccountDeleted, null),
            Times.Once);
        _sessionRevocationMock.Verify(
            x => x.RevokeUserSessionsAsync(second.Id, SessionRevocationReason.AccountDeleted, null),
            Times.Once);
    }

    [Fact]
    public async Task EnableAsync_WithValidUserId_EnablesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        // ★ 顺序是契约：SetLockoutEndDateAsync 在 LockoutEnabled 为 false 时静默失败，
        //   所以「启用」必须先武装（true）再清 LockoutEnd。真 UserManager 上的行为
        //   由 IntegrationTests 的 UserLockoutIntegrationTests 覆盖；这里只钉调用序列与结果检查。
        var sequence = new MockSequence();
        _userManagerMock.InSequence(sequence).Setup(x => x.SetLockoutEnabledAsync(user, true))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.InSequence(sequence).Setup(x => x.SetLockoutEndDateAsync(user, null))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.InSequence(sequence).Setup(x => x.ResetAccessFailedCountAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserEnabledEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _userService.EnableAsync(userId);

        // Assert
        Assert.True(result.Succeeded, result.Message);
        _userManagerMock.Verify(x => x.SetLockoutEnabledAsync(user, false), Times.Never);
        _userManagerMock.Verify(x => x.SetLockoutEnabledAsync(user, true), Times.Once);
        _userManagerMock.Verify(x => x.SetLockoutEndDateAsync(user, null), Times.Once);
        _userManagerMock.Verify(x => x.ResetAccessFailedCountAsync(user), Times.Once);
    }

    /// <summary>
    /// Identity 的 Set*Async 失败是静默的 IdentityResult；「启用」不能在它失败时照样答成功。
    /// </summary>
    [Fact]
    public async Task EnableAsync_WhenClearingLockoutFails_ReturnsFailure_AndPublishesNothing()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.SetLockoutEnabledAsync(user, true))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.SetLockoutEndDateAsync(user, null))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "UserLockoutNotEnabled", Description = "Lockout is not enabled for this user." }));

        var result = await _userService.EnableAsync(userId);

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_UPDATE_FAILED, result.ErrorCode);
        _eventBusMock.Verify(
            x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserEnabledEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// ★★★ 「启用」不能把一个还没接受邀请的账号放出来。
    /// </summary>
    /// <remarks>
    /// 这道守卫看着多余，实则是本方法自身造成的：启用就是清掉 <c>LockoutEnd</c>，
    /// 于是 <c>LockedAccountLoginGuard</c> 如其所愿地放行 —— 一个没有密码、
    /// 没有二次验证、角色却已预设好的账号就对全部登录路径敞开了，
    /// 而验证码登录只需要收到一封邮件。让人进来的唯一途径必须是接受邀请本身。
    /// </remarks>
    [Fact]
    public async Task EnableAsync_OnAnInvitedAccount_IsRejected_AndDoesNotTouchLockout()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "newhire",
            PendingActions = PendingUserActions.InvitationPending
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        // Act
        var result = await _userService.EnableAsync(userId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_ACTIVATION_PENDING, result.ErrorCode);
        _userManagerMock.Verify(x => x.SetLockoutEnabledAsync(It.IsAny<User>(), It.IsAny<bool>()), Times.Never);
        _userManagerMock.Verify(x => x.SetLockoutEndDateAsync(It.IsAny<User>(), It.IsAny<DateTimeOffset?>()), Times.Never);
        Assert.Equal(PendingUserActions.InvitationPending, user.PendingActions);
    }

    [Fact]
    public async Task DisableAsync_WithValidUserId_DisablesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.SetLockoutEnabledAsync(user, true))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.SetLockoutEndDateAsync(user, It.IsAny<DateTimeOffset?>()))
            .ReturnsAsync(IdentityResult.Success);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserDisabledEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _userService.DisableAsync(userId, "Test reason");

        // Assert
        Assert.True(result.Succeeded, result.Message);
        _userManagerMock.Verify(x => x.SetLockoutEnabledAsync(user, true), Times.Once);
        _userManagerMock.Verify(x => x.SetLockoutEndDateAsync(user, It.IsAny<DateTimeOffset?>()), Times.Once);
    }

    [Fact]
    public async Task LockAsync_WithValidUserId_LocksUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var lockoutEnd = DateTime.UtcNow.AddHours(1);
        var user = new User { Id = userId, UserName = "testuser" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.SetLockoutEnabledAsync(user, true))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.SetLockoutEndDateAsync(user, lockoutEnd))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _userService.LockAsync(userId, lockoutEnd, "Test reason");

        // Assert
        Assert.True(result.Succeeded, result.Message);
        _userManagerMock.Verify(x => x.SetLockoutEnabledAsync(user, true), Times.Once);
        _userManagerMock.Verify(x => x.SetLockoutEndDateAsync(user, lockoutEnd), Times.Once);
    }

    [Fact]
    public async Task UnlockAsync_WithValidUserId_UnlocksUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        SetupLockoutClearing(user);

        // Act
        var result = await _userService.UnlockAsync(userId);

        // Assert
        Assert.True(result.Succeeded, result.Message);
        // 解锁与启用共用同一个原语：先武装再清 LockoutEnd（否则后者静默失败）。
        _userManagerMock.Verify(x => x.SetLockoutEnabledAsync(user, true), Times.Once);
        _userManagerMock.Verify(x => x.SetLockoutEndDateAsync(user, null), Times.Once);
        _userManagerMock.Verify(x => x.ResetAccessFailedCountAsync(user), Times.Once);
    }

    /// <summary>三个 Set*Async 都返回成功的最小装配。</summary>
    private void SetupLockoutClearing(User user)
    {
        _userManagerMock.Setup(x => x.SetLockoutEnabledAsync(user, true)).ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.SetLockoutEndDateAsync(user, null)).ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.ResetAccessFailedCountAsync(user)).ReturnsAsync(IdentityResult.Success);
    }

    [Fact]
    public async Task AssignRolesAsync_WithValidInput_AssignsRoles()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var user = new User { Id = userId, UserName = "testuser" };
        var roles = new List<Role>
        {
            new Role { Id = roleIds[0], Name = "Role1" },
            new Role { Id = roleIds[1], Name = "Role2" }
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        // 使用 MockQueryable 设置 Roles IQueryable（支持 async LINQ）
        var rolesQueryable = roles.BuildMock();
        _roleManagerMock.Setup(x => x.Roles).Returns(rolesQueryable);

        _userManagerMock.Setup(x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _userService.AssignRolesAsync(userId, roleIds);

        // Assert
        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.AddToRolesAsync(user, It.Is<IEnumerable<string>>(r => r.Count() == 2)), Times.Once);
    }

    [Fact]
    public async Task RemoveRolesAsync_WithValidInput_RemovesRoles()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleIds = new[] { Guid.NewGuid() };
        var user = new User { Id = userId, UserName = "testuser" };
        var role = new Role { Id = roleIds[0], Name = "Role1" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        // 使用 MockQueryable 设置 Roles IQueryable（支持 async LINQ）
        var rolesQueryable = new List<Role> { role }.BuildMock();
        _roleManagerMock.Setup(x => x.Roles).Returns(rolesQueryable);

        _userManagerMock.Setup(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _userService.RemoveRolesAsync(userId, roleIds);

        // Assert
        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.RemoveFromRolesAsync(user, It.Is<IEnumerable<string>>(r => r.Contains(role.Name!))), Times.Once);
    }

    #region ChangeEmailAsync

    [Fact]
    public async Task ChangeEmailAsync_WithValidInput_ChangesEmail()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            Email = "old@example.com"
        };
        var newEmail = "new@example.com";

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.SetEmailAsync(user, newEmail))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.GenerateEmailConfirmationTokenAsync(user))
            .ReturnsAsync("confirm_token");

        _userManagerMock.Setup(x => x.ConfirmEmailAsync(user, "confirm_token"))
            .ReturnsAsync(IdentityResult.Success);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserEmailChangedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _userService.ChangeEmailAsync(userId, newEmail);

        // Assert
        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.SetEmailAsync(user, newEmail), Times.Once);
        _userManagerMock.Verify(x => x.ConfirmEmailAsync(user, "confirm_token"), Times.Once);
    }

    [Fact]
    public async Task ChangeEmailAsync_WithUserNotFound_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userService.ChangeEmailAsync(userId, "new@example.com");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        _userManagerMock.Verify(x => x.SetEmailAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ChangeEmailAsync_WhenSetEmailFails_ReturnsFailResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser", Email = "old@example.com" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.SetEmailAsync(user, "new@example.com"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Email already taken" }));

        // Act
        var result = await _userService.ChangeEmailAsync(userId, "new@example.com");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public async Task ChangeEmailAsync_WhenConfirmFails_ReturnsFailResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser", Email = "old@example.com" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.SetEmailAsync(user, "new@example.com"))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.GenerateEmailConfirmationTokenAsync(user))
            .ReturnsAsync("confirm_token");

        _userManagerMock.Setup(x => x.ConfirmEmailAsync(user, "confirm_token"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Confirm failed" }));

        // Act
        var result = await _userService.ChangeEmailAsync(userId, "new@example.com");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    #endregion

    #region ChangePhoneNumberAsync

    [Fact]
    public async Task ChangePhoneNumberAsync_WithValidInput_ChangesPhoneNumber()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "testuser",
            PhoneNumber = "13800000000"
        };
        var newPhoneNumber = "13900000000";

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GenerateChangePhoneNumberTokenAsync(user, newPhoneNumber))
            .ReturnsAsync("phone_token");

        _userManagerMock.Setup(x => x.ChangePhoneNumberAsync(user, newPhoneNumber, "phone_token"))
            .ReturnsAsync(IdentityResult.Success);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserPhoneChangedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _userService.ChangePhoneNumberAsync(userId, newPhoneNumber);

        // Assert
        Assert.True(result.Succeeded);
        _userManagerMock.Verify(x => x.ChangePhoneNumberAsync(user, newPhoneNumber, "phone_token"), Times.Once);
    }

    [Fact]
    public async Task ChangePhoneNumberAsync_WithUserNotFound_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userService.ChangePhoneNumberAsync(userId, "13900000000");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        _userManagerMock.Verify(x => x.ChangePhoneNumberAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ChangePhoneNumberAsync_WhenChangeFails_ReturnsFailResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser", PhoneNumber = "13800000000" };
        var newPhoneNumber = "13900000000";

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GenerateChangePhoneNumberTokenAsync(user, newPhoneNumber))
            .ReturnsAsync("phone_token");

        _userManagerMock.Setup(x => x.ChangePhoneNumberAsync(user, newPhoneNumber, "phone_token"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Phone number invalid" }));

        // Act
        var result = await _userService.ChangePhoneNumberAsync(userId, newPhoneNumber);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    #endregion

    #region DeleteAsync Edge Cases

    [Fact]
    public async Task DeleteAsync_WithUserNotFound_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userService.DeleteAsync(userId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        _userManagerMock.Verify(x => x.DeleteAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenDeleteFails_ReturnsFailResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.DeleteAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Delete failed" }));

        // Act
        var result = await _userService.DeleteAsync(userId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    #endregion

    #region CreateAsync Edge Cases

    [Fact]
    public async Task CreateAsync_WhenUserManagerFails_ReturnsFailResult()
    {
        // Arrange
        var input = new CreateUserDto
        {
            UserName = "testuser",
            Email = "test@example.com",
            Password = "Password123!"
        };

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>(), input.Password))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Username already exists" }));

        // Act
        var result = await _userService.CreateAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    #endregion

    #region EnableAsync / DisableAsync Edge Cases

    [Fact]
    public async Task EnableAsync_WithUserNotFound_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userService.EnableAsync(userId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task DisableAsync_WithUserNotFound_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userService.DisableAsync(userId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task LockAsync_WithUserNotFound_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userService.LockAsync(userId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task UnlockAsync_WithUserNotFound_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userService.UnlockAsync(userId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    #endregion

    #region AssignRolesAsync Edge Cases

    [Fact]
    public async Task AssignRolesAsync_WithUserNotFound_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userService.AssignRolesAsync(userId, new[] { Guid.NewGuid() });

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task AssignRolesAsync_WithNonExistentRoles_ReturnsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };
        var nonExistentRoleId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        // 返回空列表，没有找到任何角色
        var emptyRoles = new List<Role>().BuildMock();
        _roleManagerMock.Setup(x => x.Roles).Returns(emptyRoles);

        // Act
        var result = await _userService.AssignRolesAsync(userId, new[] { nonExistentRoleId });

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    #endregion

    #region DeleteManyAsync

    [Fact]
    public async Task DeleteManyAsync_WithEmptyIds_ReturnsSuccess()
    {
        // Act
        var result = await _userService.DeleteManyAsync(Enumerable.Empty<Guid>());

        // Assert
        Assert.True(result.Succeeded);
    }

    #endregion

    // Note: FindByPhoneNumberAsync 测试需要 EF Core 的异步查询提供者，在单元测试中难以模拟
    // 建议使用集成测试或使用 EF Core InMemory 数据库进行测试
    // 这里暂时跳过这些测试
}

/// <summary>
/// 账号状态变化必须把在线凭据一并作废（「推」的一侧）。
/// </summary>
/// <remarks>
/// ★★ 此前停用一个账号只写了一个锁定时间。锁定判定挂在登录守卫上，只在签发<b>新</b>令牌时生效，
/// 而已签出去的 access token 会用到过期、刷新令牌更是可以无限续期 ——
/// 管理端显示「已停用」，被停用的一方照常在用，且没有任何迹象表明这一点。
/// </remarks>
public partial class UserServiceTests
{
    [Fact]
    public async Task DisableAsync_RevokesEverySessionOfThatUser()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u" };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        SetupLockoutWriteSuccess(user);

        var result = await _userService.DisableAsync(userId);

        Assert.True(result.Succeeded);
        _sessionRevocationMock.Verify(
            x => x.RevokeUserSessionsAsync(userId, SessionRevocationReason.AccountDisabled, null),
            Times.Once);
    }

    [Fact]
    public async Task LockAsync_RevokesEverySessionOfThatUser()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u" };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        SetupLockoutWriteSuccess(user);

        var result = await _userService.LockAsync(userId);

        Assert.True(result.Succeeded);
        _sessionRevocationMock.Verify(
            x => x.RevokeUserSessionsAsync(userId, SessionRevocationReason.AccountLocked, null),
            Times.Once);
    }

    /// <summary>对照组：解锁不撤销任何会话（解锁是放行，不是收权）。</summary>
    [Fact]
    public async Task UnlockAsync_RevokesNothing()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "u" };
        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        SetupLockoutClearing(user);

        await _userService.UnlockAsync(userId);

        _sessionRevocationMock.Verify(
            x => x.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()),
            Times.Never);
    }
}
