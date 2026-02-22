
namespace Tnzi.Tests.Modules;

/// <summary>
/// ModuleLoader 测试类
/// </summary>
public class ModuleLoaderTests
{
    [Fact]
    public void LoadModules_WithSingleModule_ReturnsModule()
    {
        // Arrange
        var services = new ServiceCollection();
        var loader = new ModuleLoader();

        // Act
        var modules = loader.LoadModules(services, typeof(TestModuleA));

        // Assert
        Assert.NotNull(modules);
        Assert.Contains(modules, m => m.Type == typeof(TestModuleA));
    }

    [Fact]
    public void LoadModules_WithDependencies_RespectsOrder()
    {
        // Arrange
        var services = new ServiceCollection();
        var loader = new ModuleLoader();

        // Act - TestModuleC depends on TestModuleB, which depends on TestModuleA
        var modules = loader.LoadModules(services, typeof(TestModuleC));
        var moduleList = modules.ToList();

        // Assert
        Assert.Equal(3, moduleList.Count);

        // A 应该在 B 之前
        var indexA = moduleList.FindIndex(m => m.Type == typeof(TestModuleA));
        var indexB = moduleList.FindIndex(m => m.Type == typeof(TestModuleB));
        var indexC = moduleList.FindIndex(m => m.Type == typeof(TestModuleC));

        Assert.True(indexA < indexB, "TestModuleA should be loaded before TestModuleB");
        Assert.True(indexB < indexC, "TestModuleB should be loaded before TestModuleC");
    }

    [Fact]
    public void LoadModules_WithModuleTypes_RespectsTypePriority()
    {
        // Arrange
        var services = new ServiceCollection();
        var loader = new ModuleLoader();

        // Act
        var modules = loader.LoadModules(services, typeof(TestApplicationModule));
        var moduleList = modules.ToList();

        // Assert
        // Core 模块应该在 Application 模块之前
        var coreIndex = moduleList.FindIndex(m => m.Type == typeof(TestCoreModule));
        var appIndex = moduleList.FindIndex(m => m.Type == typeof(TestApplicationModule));

        Assert.True(coreIndex < appIndex, "Core module should be loaded before Application module");
    }
}

#region Test Modules

/// <summary>
/// 测试模块 A（无依赖）
/// </summary>
public class TestModuleA : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 测试模块 B（依赖 A）
/// </summary>
[DependsOn(typeof(TestModuleA))]
public class TestModuleB : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 测试模块 C（依赖 B）
/// </summary>
[DependsOn(typeof(TestModuleB))]
public class TestModuleC : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 测试核心模块
/// </summary>
public class TestCoreModule : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 测试应用模块（依赖核心模块）
/// </summary>
[DependsOn(typeof(TestCoreModule))]
public class TestApplicationModule : TnziApplicationModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

#endregion