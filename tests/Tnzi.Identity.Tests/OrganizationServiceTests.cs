
namespace Tnzi.Identity.Tests;

public class OrganizationServiceTests
{
    private readonly Mock<IRepository<Organization, Guid>> _organizationRepositoryMock;
    private readonly Mock<Microsoft.EntityFrameworkCore.DbContext> _dbContextMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<ICurrentUser> _currentUserMock;
    private readonly Mock<Tnzi.Caching.ICache> _cacheMock;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    private readonly OrganizationService _organizationService;

    public OrganizationServiceTests()
    {
        // Initialize Mapster
        var config = new TypeAdapterConfig();
        // 如果有特定的映射配置可以在这里添加
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _organizationRepositoryMock = new Mock<IRepository<Organization, Guid>>();
        _dbContextMock = new Mock<Microsoft.EntityFrameworkCore.DbContext>();
        _eventBusMock = new Mock<IEventBus>();
        _currentUserMock = new Mock<ICurrentUser>();
        _cacheMock = new Mock<Tnzi.Caching.ICache>();

        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _organizationService = new OrganizationService(
            _organizationRepositoryMock.Object,
            _serviceProviderMock.Object,
            _dbContextMock.Object,
            _eventBusMock.Object,
            _currentUserMock.Object,
            currentTenant: null,
            multiTenancyOptions: null,
            _cacheMock.Object,
            _userManagerMock.Object
        );
    }

    [Fact]
    public async Task GetByIdAsync_WithValidId_ReturnsOrganizationDto()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var organization = new Organization
        {
            Id = orgId,
            Name = "Test Org",
            Code = "TEST",
            IsEnabled = true
        };

        _cacheMock.Setup(x => x.GetAsync<OrganizationDto>(It.IsAny<string>()))
            .ReturnsAsync((OrganizationDto?)null);

        _organizationRepositoryMock.Setup(x => x.GetAsync(orgId))
            .ReturnsAsync(organization);

        _cacheMock.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<OrganizationDto>(), It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _organizationService.GetByIdAsync(orgId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(orgId, result.Data.Id);
        Assert.Equal("Test Org", result.Data.Name);
    }

    [Fact]
    public async Task GetByIdAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        var orgId = Guid.NewGuid();

        _cacheMock.Setup(x => x.GetAsync<OrganizationDto>(It.IsAny<string>()))
            .ReturnsAsync((OrganizationDto?)null);

        _organizationRepositoryMock.Setup(x => x.GetAsync(orgId))
            .ReturnsAsync((Organization?)null);

        // Act
        var result = await _organizationService.GetByIdAsync(orgId);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task CreateAsync_WithValidInput_CreatesOrganization()
    {
        // Arrange
        var input = new CreateOrganizationDto
        {
            Name = "New Org",
            Code = "NEW",
            Remark = "Test organization"
        };

        _organizationRepositoryMock.Setup(x => x.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Organization, bool>>>()))
            .ReturnsAsync(false);

        _organizationRepositoryMock.Setup(x => x.GetAsync(input.ParentId ?? Guid.Empty))
            .ReturnsAsync((Organization?)null);

        _organizationRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<Organization>()))
            .Returns(Task.CompletedTask);

        _cacheMock.Setup(x => x.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.OrganizationCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _organizationService.CreateAsync(input);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(input.Name, result.Data.Name);
        Assert.Equal(input.Code, result.Data.Code);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateCode_ReturnsFailResult()
    {
        // Arrange
        var input = new CreateOrganizationDto
        {
            Name = "New Org",
            Code = "EXISTING"
        };

        _organizationRepositoryMock.Setup(x => x.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Organization, bool>>>()))
            .ReturnsAsync(true);

        // Act
        var result = await _organizationService.CreateAsync(input);

        // Assert - 服务返回 Fail 而非抛异常
        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.Message!);
    }

    [Fact]
    public async Task UpdateAsync_WithValidInput_UpdatesOrganization()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var organization = new Organization
        {
            Id = orgId,
            Name = "Old Name",
            Code = "OLD",
            Path = $"/{orgId}/",
            Level = 1,
            IsEnabled = true,
            SortOrder = 1
        };
        var input = new UpdateOrganizationDto
        {
            Name = "New Name",
            Code = "NEW",
            IsEnabled = true,
            SortOrder = 1
        };

        // GetRequiredAsync 是接口方法，需要直接 Mock
        _organizationRepositoryMock.Setup(x => x.GetRequiredAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        // 同时设置 GetAsync（GetRequiredAsync 内部可能调用它）
        _organizationRepositoryMock.Setup(x => x.GetAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _organizationRepositoryMock.Setup(x => x.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Organization, bool>>>()))
            .ReturnsAsync(false);

        _organizationRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Organization>()))
            .Returns(Task.CompletedTask);

        // ClearOrganizationCacheAsync 会清除组织树缓存和特定组织缓存
        _cacheMock.Setup(x => x.RemoveAsync(Tnzi.Caching.CacheKeys.Identity.OrganizationTree))
            .Returns(Task.CompletedTask);
        _cacheMock.Setup(x => x.RemoveAsync(Tnzi.Caching.CacheKeys.Identity.Organization(orgId)))
            .Returns(Task.CompletedTask);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.OrganizationUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _organizationService.UpdateAsync(orgId, input);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(input.Name, result.Data.Name);
    }

    [Fact]
    public async Task DeleteAsync_WithValidId_DeletesOrganization()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var organization = new Organization { Id = orgId, Name = "Test Org" };

        // DeleteAsync 使用 GetRequiredAsync
        _organizationRepositoryMock.Setup(x => x.GetRequiredAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _organizationRepositoryMock.Setup(x => x.GetAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _organizationRepositoryMock.Setup(x => x.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Organization, bool>>>()))
            .ReturnsAsync(false);

        _organizationRepositoryMock.Setup(x => x.DeleteAsync(orgId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _cacheMock.Setup(x => x.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.OrganizationDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _organizationService.DeleteAsync(orgId);

        // Assert
        _organizationRepositoryMock.Verify(x => x.DeleteAsync(orgId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssignUserToOrganizationAsync_WithValidInput_AssignsUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser" };
        var organization = new Organization { Id = orgId, Name = "Test Org" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _organizationRepositoryMock.Setup(x => x.GetAsync(orgId))
            .ReturnsAsync(organization);

        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserAssignedToOrganizationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _organizationService.AssignUserToOrganizationAsync(userId, orgId);

        // Assert
        Assert.Equal(orgId, user.OrganizationId);
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task RemoveUserFromOrganizationAsync_WithValidInput_RemovesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, UserName = "testuser", OrganizationId = Guid.NewGuid() };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        _eventBusMock.Setup(x => x.PublishAsync(It.IsAny<Tnzi.Identity.Events.UserRemovedFromOrganizationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _organizationService.RemoveUserFromOrganizationAsync(userId);

        // Assert
        Assert.Null(user.OrganizationId);
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Once);
    }
}
