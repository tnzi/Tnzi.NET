namespace Tnzi.Identity.Tests;

/// <summary>
/// 删用户这个动作与「别的模块以这个账号为主体的记录」之间的协调。
/// </summary>
/// <remarks>
/// 消费应用的员工档案、警员台账、薪酬主数据都以可空 <c>UserId</c> 松引用框架的 <c>User</c>，
/// 刻意不建外键（级联不是它们该做的决定）。代价是框架自带的删除端点不知道这些行存在，
/// 删完之后它们指着一个已删账号而**零症状**。这里守着两件事：
/// 删之前问一圈 <see cref="IUserUsageProvider"/>，任一说「在用」就 409 且一个字节不改；
/// 删成功之后发 <see cref="UserDeletedEvent"/>，让想善后的模块有得可听。
/// </remarks>
public class UserServiceDeletionCoordinationTests
{
    private readonly Mock<UserManager<User>> _userManager;
    private readonly Mock<RoleManager<Role>> _roleManager;
    private readonly Mock<IRepository<User, Guid>> _userRepository;
    private readonly Mock<IEventBus> _eventBus;
    private readonly Mock<ISessionRevocationService> _sessionRevocation;
    private readonly Mock<IServiceProvider> _serviceProvider;
    private readonly Mock<ICurrentUser> _currentUser;

    public UserServiceDeletionCoordinationTests()
    {
        var store = new Mock<IUserStore<User>>();
        _userManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var roleStore = new Mock<IRoleStore<Role>>();
        _roleManager = new Mock<RoleManager<Role>>(roleStore.Object, null!, null!, null!, null!);
        _userRepository = new Mock<IRepository<User, Guid>>();
        _eventBus = new Mock<IEventBus>();
        _sessionRevocation = new Mock<ISessionRevocationService>();
        _currentUser = new Mock<ICurrentUser>();
        _serviceProvider = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        _serviceProvider.Setup(x => x.GetService(typeof(IEventBus))).Returns(_eventBus.Object);

        _userManager.Setup(x => x.GetRolesAsync(It.IsAny<User>())).ReturnsAsync([]);
        _userManager.Setup(x => x.DeleteAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);
    }

    private UserService CreateService(params IUserUsageProvider[] providers)
        => new(
            _userManager.Object,
            _roleManager.Object,
            _userRepository.Object,
            _serviceProvider.Object,
            eventBus: _eventBus.Object,
            currentUser: _currentUser.Object,
            sessionRevocation: _sessionRevocation.Object,
            usageProviders: providers.Length == 0 ? null : providers);

    private User ArrangeUser(string name = "someone")
    {
        var user = new User { Id = Guid.NewGuid(), UserName = name };
        _userManager.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        return user;
    }

    private void ArrangeRepositoryQuery(params User[] users)
    {
        var queryable = users.ToList().BuildMock();
        _userRepository.As<IQueryable<User>>().Setup(q => q.Provider).Returns(queryable.Provider);
        _userRepository.As<IQueryable<User>>().Setup(q => q.Expression).Returns(queryable.Expression);
        _userRepository.As<IQueryable<User>>().Setup(q => q.ElementType).Returns(queryable.ElementType);
        _userRepository.As<IQueryable<User>>().Setup(q => q.GetEnumerator()).Returns(() => queryable.GetEnumerator());
    }

    private static IUserUsageProvider Claiming(string detail, Guid? onlyUserId = null)
    {
        var provider = new Mock<IUserUsageProvider>();
        provider
            .Setup(p => p.FindUsageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                onlyUserId == null || onlyUserId == id ? new UserUsage(detail) : null);
        return provider.Object;
    }

    private static IUserUsageProvider Silent()
    {
        var provider = new Mock<IUserUsageProvider>();
        provider
            .Setup(p => p.FindUsageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserUsage?)null);
        return provider.Object;
    }

    [Fact]
    public async Task DeleteAsync_WhenAProviderClaimsTheAccount_RefusesWith409AndTouchesNothing()
    {
        var user = ArrangeUser();
        var service = CreateService(Silent(), Claiming("Staff record STF-001 is linked to this account; retire the staff member instead."));

        var result = await service.DeleteAsync(user.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_IN_USE, result.ErrorCode);
        Assert.Contains("STF-001", result.Message);
        _userManager.Verify(x => x.DeleteAsync(It.IsAny<User>()), Times.Never);
        _sessionRevocation.Verify(
            x => x.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()),
            Times.Never);
        _eventBus.Verify(x => x.PublishAsync(It.IsAny<UserDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WithNoProviders_DeletesExactlyAsBefore()
    {
        var user = ArrangeUser();
        var service = CreateService();

        var result = await service.DeleteAsync(user.Id);

        Assert.True(result.Succeeded);
        _userManager.Verify(x => x.DeleteAsync(user), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenEveryProviderIsSilent_Deletes()
    {
        var user = ArrangeUser();
        var service = CreateService(Silent(), Silent());

        var result = await service.DeleteAsync(user.Id);

        Assert.True(result.Succeeded);
        _userManager.Verify(x => x.DeleteAsync(user), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_OnSuccess_PublishesUserDeletedEventWithActor()
    {
        var user = ArrangeUser("gone");
        var actor = Guid.NewGuid();
        _currentUser.SetupGet(c => c.Id).Returns(actor);
        UserDeletedEvent? published = null;
        _eventBus
            .Setup(x => x.PublishAsync(It.IsAny<UserDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<UserDeletedEvent, CancellationToken>((e, _) => published = e)
            .Returns(Task.CompletedTask);
        var service = CreateService();

        await service.DeleteAsync(user.Id);

        Assert.NotNull(published);
        Assert.Equal(user.Id, published!.UserId);
        Assert.Equal("gone", published.UserName);
        Assert.Equal(actor, published.DeletedBy);
        Assert.True(published.DeletionTime <= DateTime.UtcNow);
    }

    [Fact]
    public async Task DeleteAsync_WhenTheStoreRefuses_PublishesNoEvent()
    {
        var user = ArrangeUser();
        _userManager.Setup(x => x.DeleteAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "nope" }));
        var service = CreateService();

        var result = await service.DeleteAsync(user.Id);

        Assert.False(result.Succeeded);
        _eventBus.Verify(x => x.PublishAsync(It.IsAny<UserDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// 批量删除先把整批问完再动手：第二个人被认领时，第一个人也不能已经没了。
    /// 半批成功的删除既不是操作员点的那一下，也没有任何返回值能说清楚删掉了谁。
    /// </summary>
    [Fact]
    public async Task DeleteManyAsync_WhenAnyAccountIsClaimed_DeletesNoneAndNamesTheBlockedOne()
    {
        var free = new User { Id = Guid.NewGuid(), UserName = "free" };
        var claimed = new User { Id = Guid.NewGuid(), UserName = "claimed" };
        ArrangeRepositoryQuery(free, claimed);
        var service = CreateService(Claiming("Officer roster still lists this account.", onlyUserId: claimed.Id));

        var result = await service.DeleteManyAsync([free.Id, claimed.Id]);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_IN_USE, result.ErrorCode);
        Assert.Contains("claimed", result.Message);
        Assert.Contains("Officer roster", result.Message);
        _userManager.Verify(x => x.DeleteAsync(It.IsAny<User>()), Times.Never);
    }

    /// <summary>
    /// 自助注销走同一道门：员工名册上的人自己把登录注销掉，名册照样指着一个已删账号。
    /// </summary>
    [Fact]
    public async Task DeleteAccountAsync_WhenAProviderClaimsTheAccount_RefusesBeforeLockingOrSoftDeleting()
    {
        var user = ArrangeUser("self");
        var service = CreateService(Claiming("Staff record is linked to this account; ask an administrator to retire it first."));

        var result = await service.DeleteAccountAsync(user.Id);

        Assert.Equal(409, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_IN_USE, result.ErrorCode);
        Assert.False(user.IsDeleted);
        _userManager.Verify(x => x.SetLockoutEndDateAsync(It.IsAny<User>(), It.IsAny<DateTimeOffset?>()), Times.Never);
        _userManager.Verify(x => x.UpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAccountAsync_OnSuccess_AlsoPublishesUserDeletedEvent()
    {
        var user = ArrangeUser("self");
        _userManager.Setup(x => x.SetLockoutEnabledAsync(user, true)).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(x => x.SetLockoutEndDateAsync(user, It.IsAny<DateTimeOffset?>())).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        var service = CreateService(Silent());

        var result = await service.DeleteAccountAsync(user.Id);

        Assert.True(result.Succeeded, result.Message);
        _eventBus.Verify(x => x.PublishAsync(It.Is<UserDeletedEvent>(e => e.UserId == user.Id), It.IsAny<CancellationToken>()), Times.Once);
        _eventBus.Verify(x => x.PublishAsync(It.IsAny<UserAccountDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteManyAsync_OnSuccess_PublishesOneUserDeletedEventPerAccount()
    {
        var first = new User { Id = Guid.NewGuid(), UserName = "first" };
        var second = new User { Id = Guid.NewGuid(), UserName = "second" };
        ArrangeRepositoryQuery(first, second);
        var seen = new List<Guid>();
        _eventBus
            .Setup(x => x.PublishAsync(It.IsAny<UserDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<UserDeletedEvent, CancellationToken>((e, _) => seen.Add(e.UserId))
            .Returns(Task.CompletedTask);
        var service = CreateService(Silent());

        var result = await service.DeleteManyAsync([first.Id, second.Id]);

        Assert.True(result.Succeeded);
        Assert.Equal([first.Id, second.Id], seen);
    }
}
