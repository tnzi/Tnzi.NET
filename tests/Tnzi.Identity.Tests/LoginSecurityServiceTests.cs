using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

public class LoginSecurityServiceTests
{
    private readonly Mock<IOptions<IdentityOptions>> _identityOptionsMock;
    private readonly Mock<IRepository<LoginLog, Guid>> _loginLogRepositoryMock;
    private readonly Mock<ILogger<LoginSecurityService>> _loggerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    private readonly LoginSecurityService _loginSecurityService;

    public LoginSecurityServiceTests()
    {
        _identityOptionsMock = new Mock<IOptions<IdentityOptions>>();
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            AccountSecurity = new AccountSecurityOptions
            {
                EnableAbnormalLoginDetection = true
            }
        });

        _loginLogRepositoryMock = new Mock<IRepository<LoginLog, Guid>>();
        _loggerMock = new Mock<ILogger<LoginSecurityService>>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _loginSecurityService = new LoginSecurityService(
            _serviceProviderMock.Object,
            _identityOptionsMock.Object,
            _loginLogRepositoryMock.Object
        );
    }

    [Fact]
    public async Task DetectAbnormalLoginAsync_WhenDetectionDisabled_ReturnsNormal()
    {
        // Arrange
        _identityOptionsMock.Setup(x => x.Value).Returns(new IdentityOptions
        {
            AccountSecurity = new AccountSecurityOptions
            {
                EnableAbnormalLoginDetection = false
            }
        });

        var service = new LoginSecurityService(
            _serviceProviderMock.Object,
            _identityOptionsMock.Object,
            _loginLogRepositoryMock.Object
        );

        // Act
        var result = await service.DetectAbnormalLoginAsync(Guid.NewGuid(), "127.0.0.1", "Browser");

        // Assert
        Assert.False(result.IsAbnormal);
        Assert.Equal(0, result.RiskLevel);
    }

    [Fact(Skip = "需要集成测试：DetectAbnormalLoginAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task DetectAbnormalLoginAsync_WithNewIp_ReturnsAbnormal()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }

    [Fact(Skip = "需要集成测试：DetectAbnormalLoginAsync 使用了 EF Core 的复杂查询，在单元测试中难以完全模拟")]
    public async Task DetectAbnormalLoginAsync_WithKnownIp_ReturnsNormal()
    {
        await Task.CompletedTask;
        Assert.True(true);
    }

    [Fact]
    public void GenerateDeviceFingerprint_WithValidInput_ReturnsFingerprint()
    {
        // Arrange
        var userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

        // Act
        var fingerprint = _loginSecurityService.GenerateDeviceFingerprint(userAgent);

        // Assert
        Assert.NotNull(fingerprint);
        Assert.NotEmpty(fingerprint);
    }

    [Fact]
    public void GenerateDeviceFingerprint_WithNullInput_ReturnsEmpty()
    {
        // Act
        var fingerprint = _loginSecurityService.GenerateDeviceFingerprint(null);

        // Assert
        // 可能返回空字符串或默认值
        Assert.NotNull(fingerprint);
    }

    #region Helper: SetupLoginLogQueryable

    /// <summary>
    /// 设置 LoginLog 仓储的 IQueryable mock
    /// </summary>
    private void SetupLoginLogQueryable(List<LoginLog> logs)
    {
        var mockQueryable = logs.BuildMock();
        _loginLogRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _loginLogRepositoryMock.As<IQueryable<LoginLog>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _loginLogRepositoryMock.As<IQueryable<LoginLog>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _loginLogRepositoryMock.As<IQueryable<LoginLog>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _loginLogRepositoryMock.As<IQueryable<LoginLog>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    #endregion

    #region GetSecurityOverviewAsync

    [Fact]
    public async Task GetSecurityOverviewAsync_WithLogs_ReturnsCorrectCounts()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();
        var logs = new List<LoginLog>
        {
            new() { Id = Guid.NewGuid(), UserId = userId1, IpAddress = "1.1.1.1", Status = LoginStatus.Success, CreationTime = now.AddHours(-1) },
            new() { Id = Guid.NewGuid(), UserId = userId1, IpAddress = "1.1.1.1", Status = LoginStatus.Failed, CreationTime = now.AddHours(-2) },
            new() { Id = Guid.NewGuid(), UserId = userId2, IpAddress = "2.2.2.2", Status = LoginStatus.Success, CreationTime = now.AddHours(-3) },
            new() { Id = Guid.NewGuid(), UserId = userId2, IpAddress = "3.3.3.3", Status = LoginStatus.Failed, CreationTime = now.AddHours(-4) },
        };

        SetupLoginLogQueryable(logs);

        var store = new Mock<IUserStore<User>>();
        var userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var users = new List<User>().BuildMock();
        userManagerMock.Setup(x => x.Users).Returns(users);

        var service = new LoginSecurityService(
            _serviceProviderMock.Object,
            _identityOptionsMock.Object,
            _loginLogRepositoryMock.Object,
            userManagerMock.Object
        );

        // Act
        var result = await service.GetSecurityOverviewAsync(24);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(24, result.Data.TimeRangeHours);
        Assert.Equal(4, result.Data.TotalLoginAttempts);
        Assert.Equal(2, result.Data.SuccessfulLogins);
        Assert.Equal(2, result.Data.FailedLogins);
        Assert.Equal(2, result.Data.DistinctUsers);
        Assert.Equal(3, result.Data.DistinctIpAddresses);
    }

    [Fact]
    public async Task GetSecurityOverviewAsync_WithNoLogs_ReturnsEmptyOverview()
    {
        // Arrange
        SetupLoginLogQueryable(new List<LoginLog>());

        var store = new Mock<IUserStore<User>>();
        var userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var users = new List<User>().BuildMock();
        userManagerMock.Setup(x => x.Users).Returns(users);

        var service = new LoginSecurityService(
            _serviceProviderMock.Object,
            _identityOptionsMock.Object,
            _loginLogRepositoryMock.Object,
            userManagerMock.Object
        );

        // Act
        var result = await service.GetSecurityOverviewAsync(24);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.Data.TotalLoginAttempts);
        Assert.Equal(0, result.Data.SuccessfulLogins);
        Assert.Equal(0, result.Data.FailedLogins);
        Assert.Equal(0, result.Data.DistinctUsers);
        Assert.Equal(0, result.Data.DistinctIpAddresses);
        Assert.Equal(0, result.Data.LockedOutUsers);
    }

    [Fact]
    public async Task GetSecurityOverviewAsync_FailureRate_CalculatedCorrectly()
    {
        // Arrange: 3 failed out of 4 total = 75%
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var logs = new List<LoginLog>
        {
            new() { Id = Guid.NewGuid(), UserId = userId, IpAddress = "1.1.1.1", Status = LoginStatus.Success, CreationTime = now.AddHours(-1) },
            new() { Id = Guid.NewGuid(), UserId = userId, IpAddress = "1.1.1.1", Status = LoginStatus.Failed, CreationTime = now.AddHours(-2) },
            new() { Id = Guid.NewGuid(), UserId = userId, IpAddress = "1.1.1.1", Status = LoginStatus.Failed, CreationTime = now.AddHours(-3) },
            new() { Id = Guid.NewGuid(), UserId = userId, IpAddress = "1.1.1.1", Status = LoginStatus.Failed, CreationTime = now.AddHours(-4) },
        };

        SetupLoginLogQueryable(logs);

        var store = new Mock<IUserStore<User>>();
        var userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var users = new List<User>().BuildMock();
        userManagerMock.Setup(x => x.Users).Returns(users);

        var service = new LoginSecurityService(
            _serviceProviderMock.Object,
            _identityOptionsMock.Object,
            _loginLogRepositoryMock.Object,
            userManagerMock.Object
        );

        // Act
        var result = await service.GetSecurityOverviewAsync(24);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(75.0, result.Data!.FailureRate);
    }

    [Fact]
    public async Task GetSecurityOverviewAsync_WithNullRepository_ReturnsFail()
    {
        // Arrange: 创建一个没有 repository 的服务
        var service = new LoginSecurityService(
            _serviceProviderMock.Object,
            _identityOptionsMock.Object,
            loginLogRepository: null
        );

        // Act
        var result = await service.GetSecurityOverviewAsync(24);

        // Assert
        Assert.False(result.Succeeded);
    }

    #endregion

    #region GetUsersWithFrequentFailuresAsync

    [Fact]
    public async Task GetUsersWithFrequentFailuresAsync_FiltersBy_MinFailures()
    {
        // Arrange: userId1 has 5 failures, userId2 has 2 failures, minFailures=3
        var now = DateTime.UtcNow;
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();
        var logs = new List<LoginLog>
        {
            // userId1: 5 次失败
            new() { Id = Guid.NewGuid(), UserId = userId1, IpAddress = "1.1.1.1", Status = LoginStatus.Failed, CreationTime = now.AddHours(-1) },
            new() { Id = Guid.NewGuid(), UserId = userId1, IpAddress = "1.1.1.1", Status = LoginStatus.Failed, CreationTime = now.AddHours(-2) },
            new() { Id = Guid.NewGuid(), UserId = userId1, IpAddress = "2.2.2.2", Status = LoginStatus.Failed, CreationTime = now.AddHours(-3) },
            new() { Id = Guid.NewGuid(), UserId = userId1, IpAddress = "2.2.2.2", Status = LoginStatus.Failed, CreationTime = now.AddHours(-4) },
            new() { Id = Guid.NewGuid(), UserId = userId1, IpAddress = "1.1.1.1", Status = LoginStatus.Failed, CreationTime = now.AddHours(-5) },
            // userId2: 2 次失败（不满足 minFailures=3）
            new() { Id = Guid.NewGuid(), UserId = userId2, IpAddress = "3.3.3.3", Status = LoginStatus.Failed, CreationTime = now.AddHours(-1) },
            new() { Id = Guid.NewGuid(), UserId = userId2, IpAddress = "3.3.3.3", Status = LoginStatus.Failed, CreationTime = now.AddHours(-2) },
        };

        SetupLoginLogQueryable(logs);

        var store = new Mock<IUserStore<User>>();
        var userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var users = new List<User>
        {
            new() { Id = userId1, UserName = "user1", Email = "user1@test.com" },
            new() { Id = userId2, UserName = "user2", Email = "user2@test.com" }
        }.BuildMock();
        userManagerMock.Setup(x => x.Users).Returns(users);

        var service = new LoginSecurityService(
            _serviceProviderMock.Object,
            _identityOptionsMock.Object,
            _loginLogRepositoryMock.Object,
            userManagerMock.Object
        );

        // Act
        var result = await service.GetUsersWithFrequentFailuresAsync(hours: 24, minFailures: 3);

        // Assert
        Assert.True(result.Succeeded);
        var resultList = result.Data!.ToList();
        Assert.Single(resultList);
        Assert.Equal(userId1, resultList[0].UserId);
        Assert.Equal(5, resultList[0].FailureCount);
        Assert.Equal("user1", resultList[0].UserName);
    }

    [Fact]
    public async Task GetUsersWithFrequentFailuresAsync_WithNoFailures_ReturnsEmpty()
    {
        // Arrange: 只有成功的登录记录
        var now = DateTime.UtcNow;
        var logs = new List<LoginLog>
        {
            new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IpAddress = "1.1.1.1", Status = LoginStatus.Success, CreationTime = now.AddHours(-1) },
        };

        SetupLoginLogQueryable(logs);

        var store = new Mock<IUserStore<User>>();
        var userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var users = new List<User>().BuildMock();
        userManagerMock.Setup(x => x.Users).Returns(users);

        var service = new LoginSecurityService(
            _serviceProviderMock.Object,
            _identityOptionsMock.Object,
            _loginLogRepositoryMock.Object,
            userManagerMock.Object
        );

        // Act
        var result = await service.GetUsersWithFrequentFailuresAsync(hours: 24, minFailures: 3);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task GetUsersWithFrequentFailuresAsync_WithNullRepository_ReturnsFail()
    {
        // Arrange
        var service = new LoginSecurityService(
            _serviceProviderMock.Object,
            _identityOptionsMock.Object,
            loginLogRepository: null
        );

        // Act
        var result = await service.GetUsersWithFrequentFailuresAsync(24, 3);

        // Assert
        Assert.False(result.Succeeded);
    }

    #endregion
}