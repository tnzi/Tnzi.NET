
using Tnzi.Security.Authorization;

namespace Tnzi.Identity.Tests;

public class UserDetailServiceTests
{
    private readonly Mock<IRepository<UserDetail, Guid>> _repositoryMock;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IUserTenantScopeProvider> _scopeMock;
    private readonly Mock<IFunctionAuthorizationService> _functionAuthorizationMock;
    private readonly Mock<ICurrentUser> _currentUserMock;
    private readonly HashSet<Guid> _superAdmins = [];
    private readonly UserDetailService _userDetailService;

    public UserDetailServiceTests()
    {
        _repositoryMock = new Mock<IRepository<UserDetail, Guid>>();

        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _currentUserMock = new Mock<ICurrentUser>();
        _currentUserMock.SetupGet(c => c.IsAuthenticated).Returns(true);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ICurrentUser))).Returns(_currentUserMock.Object);

        // 默认：目标在范围内；越界的用例各自覆盖。
        _scopeMock = new Mock<IUserTenantScopeProvider>();
        _scopeMock.Setup(x => x.ContainsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _functionAuthorizationMock = new Mock<IFunctionAuthorizationService>();
        _functionAuthorizationMock.Setup(a => a.IsSuperAdminAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => _superAdmins.Contains(id));

        _userDetailService = new UserDetailService(
            _repositoryMock.Object,
            _userManagerMock.Object,
            _serviceProviderMock.Object,
            _scopeMock.Object,
            functionAuthorization: _functionAuthorizationMock.Object);
    }

    /// <summary>An existing account that a non-super-admin caller acts on.</summary>
    private User GivenATargetAccount(bool isSuperAdmin)
    {
        var actorId = Guid.NewGuid();
        var target = new User { Id = Guid.NewGuid(), UserName = isSuperAdmin ? "root" : "plain" };
        _currentUserMock.SetupGet(c => c.Id).Returns(actorId);
        if (isSuperAdmin)
        {
            _superAdmins.Add(target.Id);
        }

        _userManagerMock.Setup(x => x.FindByIdAsync(target.Id.ToString())).ReturnsAsync(target);
        GivenStoredDetails();
        return target;
    }

    private void AssertNothingWasWritten()
    {
        _repositoryMock.Verify(x => x.InsertAsync(It.IsAny<UserDetail>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDetail>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.DeleteAsync(It.IsAny<UserDetail>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The detail table holds exactly <paramref name="details"/>.</summary>
    private void GivenStoredDetails(params UserDetail[] details)
    {
        var rows = details.ToList().BuildMock();
        _repositoryMock.As<IQueryable<UserDetail>>().Setup(q => q.Provider).Returns(rows.Provider);
        _repositoryMock.As<IQueryable<UserDetail>>().Setup(q => q.Expression).Returns(rows.Expression);
        _repositoryMock.As<IQueryable<UserDetail>>().Setup(q => q.ElementType).Returns(rows.ElementType);
        _repositoryMock.As<IQueryable<UserDetail>>().Setup(q => q.GetEnumerator()).Returns(() => rows.GetEnumerator());
    }

    [Fact]
    public async Task CreateOrUpdateAsync_WithNonExistentUser_ReturnsFailResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var dto = new CreateUserDetailDto { FirstName = "Test" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userDetailService.CreateOrUpdateAsync(userId, dto);

        // Assert - 服务返回 Fail 而非抛异常
        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// ★★★ <c>admin/user-details</c> 与 <c>PUT admin/users/{id}</c> 写的是同一组字段，护栏也必须同一道：
    /// 持 <c>user.update</c> 的普通管理员不能借这条路改写超管的姓名 / 昵称 / 头像。
    /// </summary>
    [Fact]
    public async Task CreateOrUpdateAsync_ForASuperAdmin_ByANonSuperAdmin_IsForbiddenAndWritesNothing()
    {
        var root = GivenATargetAccount(isSuperAdmin: true);

        var result = await _userDetailService.CreateOrUpdateAsync(root.Id, new CreateUserDetailDto { Nickname = "owned" });

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.FORBIDDEN, result.ErrorCode);
        AssertNothingWasWritten();
    }

    [Fact]
    public async Task DeleteAsync_ForASuperAdmin_ByANonSuperAdmin_IsForbiddenAndDeletesNothing()
    {
        var root = GivenATargetAccount(isSuperAdmin: true);
        GivenStoredDetails(new UserDetail { Id = Guid.NewGuid(), UserId = root.Id, Nickname = "root" });

        var result = await _userDetailService.DeleteAsync(root.Id);

        Assert.Equal(403, result.Code);
        AssertNothingWasWritten();
    }

    /// <summary>
    /// ★★ 跨租户：<c>UserDetail</c> 被租户过滤器管着，而账号表不是：不先问范围，外租户管理员按 id 写进来的
    /// 是一行打着自己租户号的详情：对方已有详情时撞唯一索引（500），没有时种下一行对方自己永远读不到的数据。
    /// 越界与不存在同样答 404。
    /// </summary>
    [Fact]
    public async Task CreateOrUpdateAsync_ForAnAccountOutsideTheCallersTenant_Is404AndWritesNothing()
    {
        var foreign = GivenATargetAccount(isSuperAdmin: false);
        _scopeMock.Setup(x => x.ContainsAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _userDetailService.CreateOrUpdateAsync(foreign.Id, new CreateUserDetailDto { Nickname = "planted" });

        Assert.Equal(404, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_NOT_FOUND, result.ErrorCode);
        AssertNothingWasWritten();
    }

    [Fact]
    public async Task DeleteAsync_ForAnAccountOutsideTheCallersTenant_Is404AndDeletesNothing()
    {
        var foreign = GivenATargetAccount(isSuperAdmin: false);
        GivenStoredDetails(new UserDetail { Id = Guid.NewGuid(), UserId = foreign.Id, Nickname = "theirs" });
        _scopeMock.Setup(x => x.ContainsAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _userDetailService.DeleteAsync(foreign.Id);

        Assert.Equal(404, result.Code);
        AssertNothingWasWritten();
    }

    /// <summary>对照：本人改自己的资料（自助端点走同一个方法）不受护栏影响，超管也一样。</summary>
    [Fact]
    public async Task UpdateCurrentAsync_ForASuperAdminsOwnProfile_IsAllowed()
    {
        var root = GivenATargetAccount(isSuperAdmin: true);
        _currentUserMock.SetupGet(c => c.Id).Returns(root.Id);
        using var mapper = MapperExtensions.PushMapper(new Mapper(new TypeAdapterConfig()));

        var result = await _userDetailService.UpdateCurrentAsync(new CreateUserDetailDto { Nickname = "me" });

        Assert.True(result.Succeeded);
        _repositoryMock.Verify(x => x.InsertAsync(It.IsAny<UserDetail>(), It.IsAny<CancellationToken>()), Times.Once);
    }

}
