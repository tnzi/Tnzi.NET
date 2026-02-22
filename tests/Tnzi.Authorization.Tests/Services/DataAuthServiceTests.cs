
namespace Tnzi.Authorization.Tests.Services;

/// <summary>
/// DataAuthService 单元测试
/// </summary>
/// <remarks>
/// 由于 DataAuthService 内部使用了大量 IQueryable 操作和表达式树构建，
/// 部分测试难以通过纯 Mock 实现。这些测试主要验证服务的可用性和边界条件。
/// 完整的功能测试需要通过集成测试（使用 InMemory 数据库）来实现。
/// </remarks>
public class DataAuthServiceTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();

    #region Service Instantiation Tests

    [Fact]
    public void Constructor_WithAllDependencies_CreatesInstance()
    {
        // Arrange
        var entityInfoRepositoryMock = new Mock<IRepository<EntityInfo, Guid>>();
        var entityRoleRepositoryMock = new Mock<IRepository<EntityRole, Guid>>();
        var userRoleServiceMock = new Mock<IUserRoleService>();

        // Act
        var service = new DataAuthService(
            entityInfoRepositoryMock.Object,
            entityRoleRepositoryMock.Object,
            _serviceProviderMock.Object,
            userRoleServiceMock.Object
        );

        // Assert
        service.ShouldNotBeNull();
    }

    #endregion

    #region GetUserEntityRolesAsync Tests

    [Fact]
    public async Task GetUserEntityRolesAsync_WithUserHavingNoRoles_ReturnsEmpty()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var entityInfoRepositoryMock = new Mock<IRepository<EntityInfo, Guid>>();
        var entityRoleRepositoryMock = new Mock<IRepository<EntityRole, Guid>>();
        var userRoleServiceMock = new Mock<IUserRoleService>();

        userRoleServiceMock.Setup(x => x.GetUserRoleIdsAsync(userId))
            .ReturnsAsync(new List<Guid>());

        var service = new DataAuthService(
            entityInfoRepositoryMock.Object,
            entityRoleRepositoryMock.Object,
            _serviceProviderMock.Object,
            userRoleServiceMock.Object
        );

        // Act
        var result = await service.GetUserEntityRolesAsync(userId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBeEmpty();
    }

    #endregion

    #region CheckDataPermissionAsync Tests

    [Fact]
    public async Task CheckDataPermissionAsync_ServiceInstantiationWorks()
    {
        // Arrange
        var entityInfoRepositoryMock = new Mock<IRepository<EntityInfo, Guid>>();
        var entityRoleRepositoryMock = new Mock<IRepository<EntityRole, Guid>>();
        var userRoleServiceMock = new Mock<IUserRoleService>();

        var service = new DataAuthService(
            entityInfoRepositoryMock.Object,
            entityRoleRepositoryMock.Object,
            _serviceProviderMock.Object,
            userRoleServiceMock.Object
        );

        // Assert
        service.ShouldNotBeNull();
    }

    #endregion
}