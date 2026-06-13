
namespace Tnzi.Identity.Tests;

/// <summary>
/// 全局会话分页列表测试（GetSessionsAsync）— 无 userId 的全量列表、
/// includeRevoked 语义、按 userId 过滤、分页、UserName 关联填充。
/// </summary>
public class SessionListTests
{
    private readonly Mock<IRepository<UserSession, Guid>> _repositoryMock;
    private readonly Mock<IRepository<User, Guid>> _userRepositoryMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    public SessionListTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _repositoryMock = new Mock<IRepository<UserSession, Guid>>();
        _userRepositoryMock = new Mock<IRepository<User, Guid>>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
    }

    private DatabaseSessionService CreateService(bool withUserRepository = true)
    {
        if (withUserRepository)
        {
            _serviceProviderMock
                .Setup(x => x.GetService(typeof(IRepository<User, Guid>)))
                .Returns(_userRepositoryMock.Object);
        }

        return new DatabaseSessionService(_repositoryMock.Object, _serviceProviderMock.Object);
    }

    private void SetupSessionQueryable(List<UserSession> sessions)
    {
        var mock = sessions.BuildMock();
        _repositoryMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
        _repositoryMock.As<IQueryable<UserSession>>().Setup(q => q.Provider).Returns(mock.Provider);
        _repositoryMock.As<IQueryable<UserSession>>().Setup(q => q.Expression).Returns(mock.Expression);
        _repositoryMock.As<IQueryable<UserSession>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _repositoryMock.As<IQueryable<UserSession>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());
    }

    private void SetupUserQueryable(List<User> users)
    {
        var mock = users.BuildMock();
        _userRepositoryMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
        _userRepositoryMock.As<IQueryable<User>>().Setup(q => q.Provider).Returns(mock.Provider);
        _userRepositoryMock.As<IQueryable<User>>().Setup(q => q.Expression).Returns(mock.Expression);
        _userRepositoryMock.As<IQueryable<User>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _userRepositoryMock.As<IQueryable<User>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());
    }

    private static UserSession MakeSession(Guid userId, double lastActivityMinutesAgo, bool isRevoked = false) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        DeviceInfo = "Windows 11",
        IpAddress = "127.0.0.1",
        CreationTime = DateTime.UtcNow.AddHours(-1),
        LastActivityTime = DateTime.UtcNow.AddMinutes(-lastActivityMinutesAgo),
        IsRevoked = isRevoked,
        RevokedAt = isRevoked ? DateTime.UtcNow : null
    };

    [Fact]
    public async Task GetSessionsAsync_NoUserId_ReturnsAllActiveSessions_OrderedByLastActivityDesc()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        SetupSessionQueryable(
        [
            MakeSession(userA, lastActivityMinutesAgo: 30),
            MakeSession(userB, lastActivityMinutesAgo: 5),
            MakeSession(userA, lastActivityMinutesAgo: 10, isRevoked: true)
        ]);
        SetupUserQueryable([]);
        var service = CreateService();

        var result = await service.GetSessionsAsync(new SessionQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.TotalCount.ShouldBe(2); // revoked excluded by default
        result.Data.Items.Count.ShouldBe(2);
        result.Data.Items[0].UserId.ShouldBe(userB); // most recent activity first
        result.Data.Items[1].UserId.ShouldBe(userA);
    }

    [Fact]
    public async Task GetSessionsAsync_IncludeRevoked_ReturnsRevokedSessions()
    {
        var userA = Guid.NewGuid();
        SetupSessionQueryable(
        [
            MakeSession(userA, 30),
            MakeSession(userA, 10, isRevoked: true)
        ]);
        SetupUserQueryable([]);
        var service = CreateService();

        var result = await service.GetSessionsAsync(new SessionQueryDto
        {
            IncludeRevoked = true,
            PageIndex = 1,
            PageSize = 10
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(2);
        result.Data.Items.Count(s => s.IsRevoked).ShouldBe(1);
    }

    [Fact]
    public async Task GetSessionsAsync_WithUserId_FiltersToThatUser()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        SetupSessionQueryable(
        [
            MakeSession(userA, 30),
            MakeSession(userA, 20),
            MakeSession(userB, 10)
        ]);
        SetupUserQueryable([]);
        var service = CreateService();

        var result = await service.GetSessionsAsync(new SessionQueryDto
        {
            UserId = userA,
            PageIndex = 1,
            PageSize = 10
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(2);
        result.Data.Items.ShouldAllBe(s => s.UserId == userA);
    }

    [Fact]
    public async Task GetSessionsAsync_PopulatesUserNameFromUserTable()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        SetupSessionQueryable(
        [
            MakeSession(userA, 5),
            MakeSession(userB, 10)
        ]);
        SetupUserQueryable(
        [
            new User { Id = userA, UserName = "alice" },
            new User { Id = userB, UserName = "bob" }
        ]);
        var service = CreateService();

        var result = await service.GetSessionsAsync(new SessionQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data!.Items[0].UserName.ShouldBe("alice"); // userA — most recent activity
        result.Data.Items[1].UserName.ShouldBe("bob");
    }

    [Fact]
    public async Task GetSessionsAsync_UnknownUser_LeavesUserNameNull()
    {
        var userA = Guid.NewGuid();
        SetupSessionQueryable([MakeSession(userA, 5)]);
        SetupUserQueryable([]); // user not found (e.g. soft-deleted)
        var service = CreateService();

        var result = await service.GetSessionsAsync(new SessionQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data!.Items[0].UserName.ShouldBeNull();
    }

    [Fact]
    public async Task GetSessionsAsync_WithoutUserRepository_StillReturnsSessions()
    {
        var userA = Guid.NewGuid();
        SetupSessionQueryable([MakeSession(userA, 5)]);
        var service = CreateService(withUserRepository: false);

        var result = await service.GetSessionsAsync(new SessionQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(1);
        result.Data.Items[0].UserName.ShouldBeNull();
    }

    [Fact]
    public async Task GetSessionsAsync_Pagination_ReturnsCorrectPageAndTotal()
    {
        var userA = Guid.NewGuid();
        var sessions = new List<UserSession>();
        for (var i = 0; i < 5; i++)
        {
            sessions.Add(MakeSession(userA, lastActivityMinutesAgo: i + 1));
        }
        SetupSessionQueryable(sessions);
        SetupUserQueryable([]);
        var service = CreateService();

        var page2 = await service.GetSessionsAsync(new SessionQueryDto { PageIndex = 2, PageSize = 2 });

        page2.Succeeded.ShouldBeTrue();
        page2.Data!.TotalCount.ShouldBe(5);
        page2.Data.Items.Count.ShouldBe(2);
        // Page 2 holds the 3rd and 4th most recently active sessions
        page2.Data.Items[0].LastActivityTime.ShouldBe(sessions[2].LastActivityTime);
        page2.Data.Items[1].LastActivityTime.ShouldBe(sessions[3].LastActivityTime);
    }
}
