
namespace Tnzi.Tests.Modules;

/// <summary>
/// ModuleLoader 缺失依赖回归测试
/// 覆盖两个历史缺陷：
/// ① 传递依赖缺失的模块被静默加载（吞掉子模块的 ModuleMissingDependencyException）；
/// ② 第二条路径依赖同一失败模块时，因 visiting 残留误报循环依赖。
/// </summary>
public class ModuleLoaderMissingDependencyTests
{
    [Fact]
    public void LoadModules_WithTransitiveMissingDependency_ThrowsMissingDependencyNotSilentSuccess()
    {
        // Arrange: Top -> Mid -> Bad，其中 Bad 依赖一个非模块类型（string）
        var services = new ServiceCollection();
        var loader = new ModuleLoader();

        // Act & Assert：必须 fail-fast 抛出缺失依赖异常，而不是静默加载残缺的模块图
        var exception = Assert.Throws<ModuleMissingDependencyException>(
            () => loader.LoadModules(services, typeof(MissingDepTopModule)));

        // 失败的模块是 Bad，缺失的依赖是 string
        Assert.Equal(typeof(MissingDepBadModule), exception.ModuleType);
        Assert.Contains(typeof(string), exception.MissingDependencies);
        // 错误信息携带完整依赖链，便于定位
        Assert.Contains("MissingDepTopModule -> MissingDepMidModule -> MissingDepBadModule", exception.Message);
    }

    [Fact]
    public void LoadModules_WithTwoPathsToSameFailedModule_ThrowsMissingDependencyNotCircular()
    {
        // Arrange: Top -> [Mid -> Bad, Other -> Bad]
        // 两条路径都抵达同一个缺失依赖的 Bad 模块。
        // 旧实现会因第一条路径抛异常后 visiting 残留 Bad，
        // 使第二条路径把 Bad 误判为循环依赖。
        var services = new ServiceCollection();
        var loader = new ModuleLoader();

        // Act & Assert：异常类型必须是 MissingDependency 而非 CircularDependency
        var exception = Assert.Throws<ModuleMissingDependencyException>(
            () => loader.LoadModules(services, typeof(MissingDepBranchTopModule)));

        Assert.Equal(typeof(MissingDepBadModule), exception.ModuleType);
        Assert.Contains(typeof(string), exception.MissingDependencies);
    }
}

#region Missing Dependency Test Modules

/// <summary>
/// 缺失依赖的坏模块：依赖一个非 Tnzi 模块类型（string）
/// </summary>
[DependsOn(typeof(string))]
public class MissingDepBadModule : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 中间模块：依赖坏模块（传递缺失依赖）
/// </summary>
[DependsOn(typeof(MissingDepBadModule))]
public class MissingDepMidModule : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 顶层模块：依赖中间模块（Top -> Mid -> Bad）
/// </summary>
[DependsOn(typeof(MissingDepMidModule))]
public class MissingDepTopModule : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 另一条通往坏模块的分支：依赖坏模块
/// </summary>
[DependsOn(typeof(MissingDepBadModule))]
public class MissingDepOtherModule : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 分支顶层模块：同时依赖 Mid 和 Other（两条路径都通向 Bad）
/// </summary>
[DependsOn(typeof(MissingDepMidModule), typeof(MissingDepOtherModule))]
public class MissingDepBranchTopModule : TnziCoreModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;
}

#endregion
