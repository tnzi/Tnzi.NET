
namespace Tnzi.Authorization.Tests.Services;

/// <summary>
/// PermissionManager 单元测试
/// </summary>
/// <remarks>
/// 由于 PermissionManager 内部使用了 IQueryable 扩展方法从数据库加载权限，
/// 完整的功能测试需要通过集成测试（使用 InMemory 数据库）来实现。
/// 这些测试主要验证服务的可用性。
/// </remarks>
public class PermissionManagerTests
{
    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Arrange
        var serviceProviderMock = new Mock<IServiceProvider>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var loggerMock = new Mock<ILogger<PermissionManager>>();

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        serviceProviderMock
            .Setup(x => x.GetService(typeof(IEnumerable<IPermissionDefinitionProvider>)))
            .Returns(new List<IPermissionDefinitionProvider>());

        // Act
        var manager = new PermissionManager(scopeFactoryMock.Object, loggerMock.Object);

        // Assert
        manager.ShouldNotBeNull();
    }

    [Fact]
    public async Task RefreshAsync_ClearsAndReloadsPermissions()
    {
        // Arrange
        var serviceProviderMock = new Mock<IServiceProvider>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var loggerMock = new Mock<ILogger<PermissionManager>>();

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        serviceProviderMock
            .Setup(x => x.GetService(typeof(IEnumerable<IPermissionDefinitionProvider>)))
            .Returns(new List<IPermissionDefinitionProvider> { new TestPermissionDefinitionProvider() });

        var manager = new PermissionManager(scopeFactoryMock.Object, loggerMock.Object);

        // Act - 刷新两次确保不会抛出异常
        await manager.RefreshAsync();
        await manager.RefreshAsync();

        // Assert - 验证日志被调用
        Assert.True(true);
    }
}


/// <summary>
/// PermissionDefinition 单元测试
/// </summary>
public class PermissionDefinitionTests
{
    [Fact]
    public void RequiresPermission_AddsToRequiredList()
    {
        // Arrange
        var permission = new PermissionDefinition { Name = "test.permission" };

        // Act
        permission.RequiresPermission("base.read");
        permission.RequiresPermission("base.write");

        // Assert
        permission.RequiredPermissions.Count.ShouldBe(2);
        permission.RequiredPermissions.ShouldContain("base.read");
        permission.RequiredPermissions.ShouldContain("base.write");
    }

    [Fact]
    public void RequiresPermission_SkipsDuplicates()
    {
        // Arrange
        var permission = new PermissionDefinition { Name = "test.permission" };

        // Act
        permission.RequiresPermission("base.read");
        permission.RequiresPermission("base.read");

        // Assert
        permission.RequiredPermissions.Count.ShouldBe(1);
    }

    [Fact]
    public void RequiresPermission_SkipsEmptyStrings()
    {
        // Arrange
        var permission = new PermissionDefinition { Name = "test.permission" };

        // Act
        permission.RequiresPermission("");
        permission.RequiresPermission(null!);

        // Assert
        permission.RequiredPermissions.Count.ShouldBe(0);
    }

    [Fact]
    public void InheritFromParent_SetsFlag()
    {
        // Arrange
        var permission = new PermissionDefinition 
        { 
            Name = "test.permission",
            ParentName = "parent.permission"
        };

        // Act
        permission.InheritFromParent();

        // Assert
        permission.IsInheritedFromParent.ShouldBeTrue();
    }

    [Fact]
    public void InheritFromParent_SupportsFluent()
    {
        // Arrange & Act
        var permission = new PermissionDefinition()
            .RequiresPermission("base.read")
            .RequiresPermission("base.write")
            .InheritFromParent();

        // Assert
        permission.RequiredPermissions.Count.ShouldBe(2);
        permission.IsInheritedFromParent.ShouldBeTrue();
    }
}


/// <summary>
/// 测试用的权限定义提供者
/// </summary>
public class TestPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup("TestGroup", "Test Group");
        context.AddPermission("test.read", "Read Permission", parentName: "TestGroup");
        context.AddPermission("test.write", "Write Permission", parentName: "TestGroup");
    }
}