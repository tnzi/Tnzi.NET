
namespace Tnzi.Authorization.Tests.Services;

/// <summary>
/// FunctionAuthorizationService 单元测试
/// </summary>
/// <remarks>
/// 由于 IRepository 实现了 IQueryable<TEntity> 且服务内部使用了 LINQ 扩展方法，
/// 需要依赖 IQueryable 操作的测试将在集成测试中实现（使用 InMemory 数据库）。
/// 这些测试主要验证服务的可用性和基于 FindAsync 的基础功能。
/// </remarks>
public class FunctionAuthorizationServiceTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();

    #region Service Instantiation Tests

    [Fact]
    public void Constructor_WithAllDependencies_CreatesInstance()
    {
        // Arrange
        var moduleRepositoryMock = new Mock<IRepository<FunctionModule, Guid>>();
        var moduleFunctionRepositoryMock = new Mock<IRepository<ModuleFunction, Guid>>();
        var moduleUserRepositoryMock = new Mock<IRepository<ModuleUser, Guid>>();
        var moduleRoleRepositoryMock = new Mock<IRepository<ModuleRole, Guid>>();
        var roleFunctionRepositoryMock = new Mock<IRepository<RoleFunction, Guid>>();
        var userRoleServiceMock = new Mock<IUserRoleService>();

        // Act
        var service = new FunctionAuthorizationService(
            moduleRepositoryMock.Object,
            moduleFunctionRepositoryMock.Object,
            moduleUserRepositoryMock.Object,
            moduleRoleRepositoryMock.Object,
            roleFunctionRepositoryMock.Object,
            _serviceProviderMock.Object,
            userRoleServiceMock.Object
        );

        // Assert
        service.ShouldNotBeNull();
    }

    [Fact]
    public void Constructor_WithNullUserRoleService_CreatesInstance()
    {
        // Arrange
        var moduleRepositoryMock = new Mock<IRepository<FunctionModule, Guid>>();
        var moduleFunctionRepositoryMock = new Mock<IRepository<ModuleFunction, Guid>>();
        var moduleUserRepositoryMock = new Mock<IRepository<ModuleUser, Guid>>();
        var moduleRoleRepositoryMock = new Mock<IRepository<ModuleRole, Guid>>();
        var roleFunctionRepositoryMock = new Mock<IRepository<RoleFunction, Guid>>();

        // Act - IUserRoleService 是可选的
        var service = new FunctionAuthorizationService(
            moduleRepositoryMock.Object,
            moduleFunctionRepositoryMock.Object,
            moduleUserRepositoryMock.Object,
            moduleRoleRepositoryMock.Object,
            roleFunctionRepositoryMock.Object,
            _serviceProviderMock.Object,
            null
        );

        // Assert
        service.ShouldNotBeNull();
    }

    #endregion

    #region GetModuleByIdAsync Tests

    [Fact]
    public async Task GetModuleByIdAsync_WithExistingId_ReturnsModule()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        var module = new FunctionModule { Id = moduleId, Name = "TestModule", Code = "TM" };

        var moduleRepositoryMock = new Mock<IRepository<FunctionModule, Guid>>();
        // 服务使用 FindAsync(params object[] keys)
        moduleRepositoryMock.Setup(x => x.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync(module);

        var service = CreateService(moduleRepositoryMock);

        // Act
        var result = await service.GetModuleByIdAsync(moduleId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Id.ShouldBe(moduleId);
        result.Data.Name.ShouldBe("TestModule");
    }

    [Fact]
    public async Task GetModuleByIdAsync_WithNonExistingId_ReturnsNull()
    {
        // Arrange
        var moduleId = Guid.NewGuid();

        var moduleRepositoryMock = new Mock<IRepository<FunctionModule, Guid>>();
        moduleRepositoryMock.Setup(x => x.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync((FunctionModule?)null);

        var service = CreateService(moduleRepositoryMock);

        // Act
        var result = await service.GetModuleByIdAsync(moduleId);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Data.ShouldBeNull();
    }

    #endregion

    #region GetModuleFunctionByIdAsync Tests

    [Fact]
    public async Task GetModuleFunctionByIdAsync_WithExistingId_ReturnsFunction()
    {
        // Arrange
        var functionId = Guid.NewGuid();
        var function = new ModuleFunction
        {
            Id = functionId,
            Name = "TestFunction",
            Code = "TF",
            ModuleId = Guid.NewGuid()
        };

        var moduleFunctionRepositoryMock = new Mock<IRepository<ModuleFunction, Guid>>();
        moduleFunctionRepositoryMock.Setup(x => x.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync(function);

        var service = CreateService(null, moduleFunctionRepositoryMock);

        // Act
        var result = await service.GetModuleFunctionByIdAsync(functionId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Id.ShouldBe(functionId);
    }

    [Fact]
    public async Task GetModuleFunctionByIdAsync_WithNonExistingId_ReturnsNull()
    {
        // Arrange
        var functionId = Guid.NewGuid();

        var moduleFunctionRepositoryMock = new Mock<IRepository<ModuleFunction, Guid>>();
        moduleFunctionRepositoryMock.Setup(x => x.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync((ModuleFunction?)null);

        var service = CreateService(null, moduleFunctionRepositoryMock);

        // Act
        var result = await service.GetModuleFunctionByIdAsync(functionId);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Data.ShouldBeNull();
    }

    #endregion

    #region Helper Methods

    private FunctionAuthorizationService CreateService(
        Mock<IRepository<FunctionModule, Guid>>? moduleRepository = null,
        Mock<IRepository<ModuleFunction, Guid>>? moduleFunctionRepository = null,
        Mock<IRepository<ModuleUser, Guid>>? moduleUserRepository = null,
        Mock<IRepository<ModuleRole, Guid>>? moduleRoleRepository = null,
        Mock<IRepository<RoleFunction, Guid>>? roleFunctionRepository = null,
        IUserRoleService? userRoleService = null)
    {
        return new FunctionAuthorizationService(
            moduleRepository?.Object ?? new Mock<IRepository<FunctionModule, Guid>>().Object,
            moduleFunctionRepository?.Object ?? new Mock<IRepository<ModuleFunction, Guid>>().Object,
            moduleUserRepository?.Object ?? new Mock<IRepository<ModuleUser, Guid>>().Object,
            moduleRoleRepository?.Object ?? new Mock<IRepository<ModuleRole, Guid>>().Object,
            roleFunctionRepository?.Object ?? new Mock<IRepository<RoleFunction, Guid>>().Object,
            _serviceProviderMock.Object,
            userRoleService
        );
    }

    #endregion
}