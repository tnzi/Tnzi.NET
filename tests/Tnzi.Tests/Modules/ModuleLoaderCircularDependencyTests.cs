
namespace Tnzi.Tests.Modules;

/// <summary>
/// ModuleLoader 循环依赖测试
/// </summary>
public class ModuleLoaderCircularDependencyTests
{
    [Fact]
    public void LoadModules_WithCircularDependency_ThrowsModuleCircularDependencyException()
    {
        // Arrange
        var services = new ServiceCollection();
        var loader = new ModuleLoader();

        // Act & Assert
        var exception = Assert.Throws<ModuleCircularDependencyException>(
            () => loader.LoadModules(services, typeof(CircularModuleA)));

        Assert.Contains("Circular dependency detected", exception.Message);
    }

    [Fact]
    public void LoadModules_WithThreeWayCircularDependency_ThrowsModuleCircularDependencyException()
    {
        // Arrange: A -> B -> C -> A
        var services = new ServiceCollection();
        var loader = new ModuleLoader();

        // Act & Assert
        var exception = Assert.Throws<ModuleCircularDependencyException>(
            () => loader.LoadModules(services, typeof(TriangleModuleA)));

        Assert.Contains("Circular dependency detected", exception.Message);
    }

    [Fact]
    public void LoadModules_WithSelfDependency_ThrowsModuleCircularDependencyException()
    {
        // Arrange: A -> A
        var services = new ServiceCollection();
        var loader = new ModuleLoader();

        // Act & Assert
        var exception = Assert.Throws<ModuleCircularDependencyException>(
            () => loader.LoadModules(services, typeof(SelfDependentModule)));

        Assert.Contains("Circular dependency detected", exception.Message);
    }
}

#region Circular Dependency Test Modules

/// <summary>
/// 循环依赖测试模块 A（依赖 B）
/// </summary>
[DependsOn(typeof(CircularModuleB))]
public class CircularModuleA : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 循环依赖测试模块 B（依赖 A）
/// </summary>
[DependsOn(typeof(CircularModuleA))]
public class CircularModuleB : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 三角循环依赖测试模块 A
/// </summary>
[DependsOn(typeof(TriangleModuleB))]
public class TriangleModuleA : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 三角循环依赖测试模块 B
/// </summary>
[DependsOn(typeof(TriangleModuleC))]
public class TriangleModuleB : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 三角循环依赖测试模块 C
/// </summary>
[DependsOn(typeof(TriangleModuleA))]
public class TriangleModuleC : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 自我依赖测试模块
/// </summary>
[DependsOn(typeof(SelfDependentModule))]
public class SelfDependentModule : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

#endregion